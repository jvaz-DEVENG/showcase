using System.Diagnostics;
using GameBoost.Core.Logging;
using GameBoost.Core.Modules.GameMode;

namespace GameBoost.Core.Modules.Bottleneck;

public sealed record MonitorTick(MetricsSnapshot Snapshot, IReadOnlyList<Finding> Findings);

/// <summary>
/// Coleta a cada 1 s com buffer de 5 min (secao 5.5).
///
/// Roda numa Task propria, nunca na thread da UI (regra 10). Se um tique
/// atrasa, o proximo nao se acumula: o intervalo e medido do fim de um ciclo
/// ao inicio do seguinte, entao a coleta nunca vira uma fila de trabalho
/// atrasado quando a maquina esta sob carga.
/// </summary>
public sealed class BottleneckMonitor : IDisposable
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan Janela = TimeSpan.FromMinutes(5);

    private readonly IMetricsCollector _coletor;
    private readonly FindingEngine _motor;
    private readonly SystemFactsReader _fatos;
    private readonly GameDetector _detector;
    private readonly IGameBoostLogger _log;

    private readonly MetricsBuffer _buffer = new((int)(Janela.TotalSeconds / Intervalo.TotalSeconds));
    private CancellationTokenSource? _cts;
    private Thread? _thread;

    /// <summary>Disparado a cada coleta. O assinante e responsavel por voltar para a UI.</summary>
    public event Action<MonitorTick>? AoColetar;

    public BottleneckMonitor(
        IMetricsCollector coletor,
        FindingEngine motor,
        SystemFactsReader fatos,
        GameDetector detector,
        IGameBoostLogger log)
    {
        _coletor = coletor;
        _motor = motor;
        _fatos = fatos;
        _detector = detector;
        _log = log;
    }

    public bool Rodando => _thread is { IsAlive: true };

    public MetricsBuffer Buffer => _buffer;

    public MetricsSnapshot? Ultimo => _buffer.Ultimo;

    /// <summary>
    /// Custo medio de CPU da propria coleta, para provar o orcamento da secao 5.5.
    /// Medido pelo tempo de CPU da thread de coleta, nao pelo tempo de parede.
    /// </summary>
    public double CustoMedioDeCpuPercent { get; private set; }

    /// <summary>Duracao media de um ciclo em milissegundos, util para diagnosticar lentidao.</summary>
    public double DuracaoMediaDoCicloMs { get; private set; }

    public void Iniciar()
    {
        if (Rodando)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // Thread dedicada, e nao Task no pool, por dois motivos: o custo da
        // coleta e medido por GetThreadTimes, que so faz sentido se for sempre
        // a mesma thread (um await devolveria a continuacao em outra), e a
        // prioridade abaixo do normal garante que o monitor cede lugar para o
        // jogo em vez de disputar com ele.
        _thread = new Thread(() => Laco(token))
        {
            IsBackground = true,
            Name = "GameBoost.Bottleneck",
            Priority = ThreadPriority.BelowNormal
        };

        _thread.Start();

        _log.Info("Bottleneck", "Iniciar", null, $"coleta a cada {Intervalo.TotalSeconds:0}s, janela de {Janela.TotalMinutes:0} min");
    }

    public void Parar()
    {
        _cts?.Cancel();

        _thread?.Join(TimeSpan.FromSeconds(3));
        _thread = null;
        _log.Info("Bottleneck", "Parar", null,
            $"custo medio de CPU da coleta: {CustoMedioDeCpuPercent:0.000}%, ciclo medio de {DuracaoMediaDoCicloMs:0} ms");
    }

    private void Laco(CancellationToken ct)
    {
        var cronometro = new Stopwatch();
        var nucleos = Environment.ProcessorCount;
        var amostrasDeCusto = 0;
        var cpuDaThreadAntes = Native.NativeMethodsBridge.TempoDeCpuDaThreadAtual();

        // Primeira coleta e so linha de base dos deltas: nao gera finding.
        try
        {
            _coletor.Coletar();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            _log.Error("Bottleneck", "Coletar", null, "falha na leitura inicial", ex);
        }

        while (!ct.IsCancellationRequested)
        {
            if (ct.WaitHandle.WaitOne(Intervalo))
                return;

            cronometro.Restart();
            cpuDaThreadAntes = Native.NativeMethodsBridge.TempoDeCpuDaThreadAtual();

            try
            {
                var snapshot = _coletor.Coletar();
                _buffer.Adicionar(snapshot);

                var jogo = _detector.DetectarPorNome(snapshot.Processos.Select(p => p.Nome));

                var contexto = new FindingContext
                {
                    Atual = snapshot,
                    Historico = _buffer.Todos(),
                    Fatos = _fatos.Ler(),
                    JogoEmExecucao = jogo
                };

                var findings = _motor.Avaliar(contexto);
                AoColetar?.Invoke(new MonitorTick(snapshot, findings));
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                          or UnauthorizedAccessException
                                          or System.ComponentModel.Win32Exception)
            {
                _log.Error("Bottleneck", "Coletar", null, "falha numa coleta, o laco continua", ex);
            }

            cronometro.Stop();

            // Custo real: tempo de CPU consumido pela thread de coleta sobre o
            // intervalo, dividido pelos nucleos (100% = a maquina inteira).
            // Usar o cronometro aqui contaria espera de WMI como se fosse CPU.
            var cpuDaThread = (Native.NativeMethodsBridge.TempoDeCpuDaThreadAtual() - cpuDaThreadAntes).TotalMilliseconds;
            var custo = cpuDaThread / Intervalo.TotalMilliseconds * 100.0 / nucleos;

            amostrasDeCusto++;
            CustoMedioDeCpuPercent += (custo - CustoMedioDeCpuPercent) / amostrasDeCusto;
            DuracaoMediaDoCicloMs += (cronometro.Elapsed.TotalMilliseconds - DuracaoMediaDoCicloMs) / amostrasDeCusto;
        }
    }

    public void Dispose()
    {
        Parar();
        _cts?.Dispose();
        (_coletor as IDisposable)?.Dispose();
    }
}
