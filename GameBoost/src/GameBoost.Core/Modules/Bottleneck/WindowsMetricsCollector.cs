using System.Net.NetworkInformation;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Native;

namespace GameBoost.Core.Modules.Bottleneck;

public interface IMetricsCollector
{
    /// <summary>Um instante da maquina. A primeira chamada e so a linha de base dos deltas.</summary>
    MetricsSnapshot Coletar();
}

/// <summary>
/// Coleta um instante da maquina a cada chamada.
///
/// Tudo que tem API nativa usa API nativa (regra 9), e cada fonte falha de
/// forma isolada: se a GPU nao puder ser lida, o resto do snapshot continua
/// valido e a GPU vira "indisponivel" na UI, nunca zero (regra 4).
/// </summary>
public sealed class WindowsMetricsCollector : IMetricsCollector, IDisposable
{
    private readonly IMemoryService _memoria;
    private readonly IGameBoostLogger _log;
    private readonly IClock _relogio;
    private readonly int _nucleos = Environment.ProcessorCount;

    private SystemInformation.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] _cpuAnterior = Array.Empty<SystemInformation.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
    private Dictionary<int, (TimeSpan Cpu, long Io)> _processosAnteriores = new();
    private DateTimeOffset _momentoAnterior;
    private long _redeAnterior = -1;

    private readonly GpuCounterReader _gpu;
    private readonly ITemperatureProvider _temperaturas;

    public WindowsMetricsCollector(
        IMemoryService memoria,
        IGameBoostLogger log,
        IClock relogio,
        ITemperatureProvider? temperaturas = null)
    {
        _memoria = memoria;
        _log = log;
        _relogio = relogio;
        _gpu = new GpuCounterReader(log);
        _temperaturas = temperaturas ?? new WmiTemperatureProvider(log);
    }

    public MetricsSnapshot Coletar()
    {
        var agora = _relogio.Now;
        var intervalo = _momentoAnterior == default
            ? TimeSpan.Zero
            : agora - _momentoAnterior;

        var cpu = LerCpu();
        var memoria = _memoria.GetSnapshot();
        var standby = SystemInformation.LerStandbyList() ?? 0;
        var processos = LerProcessos(intervalo);
        var rede = LerRede(intervalo);
        var disco = LerDiscoDoSistema();

        _momentoAnterior = agora;

        return new MetricsSnapshot
        {
            Momento = agora,
            CpuPercent = cpu,
            NucleosLogicos = _nucleos,
            RamTotalBytes = memoria.TotalBytes,
            RamDisponivelBytes = memoria.AvailableBytes,
            StandbyBytes = standby,
            GpuPercent = _gpu.LerUso(),
            GpuMemoriaBytes = _gpu.LerMemoria(),
            DiscoSistemaUsadoPercent = disco,
            RedeBytesPorSegundo = rede,
            TemperaturaCpu = _temperaturas.LerCpu(),
            TemperaturaGpu = _temperaturas.LerGpu(),
            Processos = processos
        };
    }

    /// <summary>
    /// Uso de CPU pelo delta de tempo ocioso: 100 - (idle / total). E como o
    /// proprio Gerenciador de Tarefas calcula.
    /// </summary>
    private double LerCpu()
    {
        var atual = SystemInformation.LerTemposDeProcessador(_nucleos);
        if (atual.Length == 0)
            return 0;

        if (_cpuAnterior.Length != atual.Length)
        {
            _cpuAnterior = atual;
            return 0;
        }

        long idle = 0, total = 0;
        for (var i = 0; i < atual.Length; i++)
        {
            var deltaIdle = atual[i].IdleTime - _cpuAnterior[i].IdleTime;
            // KernelTime ja inclui o tempo ocioso.
            var deltaTotal = (atual[i].KernelTime - _cpuAnterior[i].KernelTime)
                           + (atual[i].UserTime - _cpuAnterior[i].UserTime);

            idle += deltaIdle;
            total += deltaTotal;
        }

        _cpuAnterior = atual;

        if (total <= 0)
            return 0;

        var uso = 100.0 * (total - idle) / total;
        return Math.Clamp(uso, 0, 100);
    }

    /// <summary>
    /// CPU e memoria por processo, numa unica chamada ao kernel, agrupado por
    /// nome: o Chrome com 40 processos vira uma linha so, que e o que o usuario
    /// quer ver.
    ///
    /// Usa NtQuerySystemInformation em vez de Process.GetProcesses() por causa
    /// do orcamento da secao 5.5. Ver Native/ProcessInformation.cs.
    /// </summary>
    private IReadOnlyList<ProcessUsage> LerProcessos(TimeSpan intervalo)
    {
        var brutos = ProcessInformation.Listar();

        if (brutos.Count == 0)
            return Array.Empty<ProcessUsage>();

        var atuais = new Dictionary<int, (TimeSpan Cpu, long Io)>(brutos.Count);
        var porNome = new Dictionary<string, (double Cpu, long Ram, long Io, int Qtd)>(StringComparer.OrdinalIgnoreCase);

        foreach (var bruto in brutos)
        {
            if (bruto.Pid <= 4)
                continue;

            var cpuTotal = TimeSpan.FromTicks(bruto.TempoDeCpu100ns);
            atuais[bruto.Pid] = (cpuTotal, 0);

            double cpuPercent = 0;
            if (intervalo > TimeSpan.Zero && _processosAnteriores.TryGetValue(bruto.Pid, out var antes))
            {
                var delta = (cpuTotal - antes.Cpu).TotalMilliseconds;

                // Normaliza pelo numero de nucleos: 100% = a maquina inteira.
                cpuPercent = delta / (intervalo.TotalMilliseconds * _nucleos) * 100.0;
                cpuPercent = Math.Clamp(cpuPercent, 0, 100);
            }

            var nome = Safety.ProtectedProcesses.Normalizar(bruto.Nome);

            if (porNome.TryGetValue(nome, out var acumulado))
            {
                porNome[nome] = (acumulado.Cpu + cpuPercent,
                                 acumulado.Ram + bruto.WorkingSetBytes,
                                 acumulado.Io,
                                 acumulado.Qtd + 1);
            }
            else
            {
                porNome[nome] = (cpuPercent, bruto.WorkingSetBytes, 0, 1);
            }
        }

        _processosAnteriores = atuais;

        return porNome
            .Select(kv => new ProcessUsage(0, kv.Key, kv.Value.Cpu, kv.Value.Ram, kv.Value.Io, kv.Value.Qtd))
            .OrderByDescending(u => u.CpuPercent)
            .ThenByDescending(u => u.WorkingSetBytes)
            .ToList();
    }

    private NetworkInterface[]? _interfaces;
    private DateTimeOffset _interfacesLidasEm;

    private Medida LerRede(TimeSpan intervalo)
    {
        try
        {
            // Enumerar interfaces custa caro e a lista quase nunca muda:
            // revalida a cada 30 s em vez de a cada segundo.
            if (_interfaces is null || _relogio.Now - _interfacesLidasEm > TimeSpan.FromSeconds(30))
            {
                _interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(i => i.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToArray();
                _interfacesLidasEm = _relogio.Now;
            }

            long total = 0;
            foreach (var iface in _interfaces)
            {
                if (iface.OperationalStatus != OperationalStatus.Up)
                    continue;

                var stats = iface.GetIPStatistics();
                total += stats.BytesReceived + stats.BytesSent;
            }

            if (_redeAnterior < 0 || intervalo <= TimeSpan.Zero)
            {
                _redeAnterior = total;
                return Medida.Indisponivel;
            }

            var delta = total - _redeAnterior;
            _redeAnterior = total;

            // Contador reiniciou (interface caiu e voltou).
            if (delta < 0)
                return Medida.Indisponivel;

            return Medida.De(delta / intervalo.TotalSeconds);
        }
        catch (NetworkInformationException ex)
        {
            _log.Warn("Bottleneck", "LerRede", null, ex.Message);
            return Medida.Indisponivel;
        }
    }

    private static double LerDiscoDoSistema()
    {
        try
        {
            var raiz = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (string.IsNullOrEmpty(raiz))
                return 0;

            var unidade = new DriveInfo(raiz);
            if (!unidade.IsReady || unidade.TotalSize == 0)
                return 0;

            return (unidade.TotalSize - unidade.TotalFreeSpace) * 100.0 / unidade.TotalSize;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }

    public void Dispose()
    {
        _gpu.Dispose();
        (_temperaturas as IDisposable)?.Dispose();
    }
}
