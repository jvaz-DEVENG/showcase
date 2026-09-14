using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBoost.Core;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Modules.Bottleneck;
using GameBoost.Core.Modules.GameMode;
using GameBoost.Core.Modules.HealthReport;

namespace GameBoost.App.ViewModels;

/// <summary>Um dos cinco medidores do painel.</summary>
public sealed partial class MedidorViewModel : ObservableObject
{
    public MedidorViewModel(string nome, string unidade, double maximo)
    {
        Nome = nome;
        Unidade = unidade;
        Maximo = maximo;
    }

    public string Nome { get; }
    public string Unidade { get; }
    public double Maximo { get; }

    [ObservableProperty] private double _valor;
    [ObservableProperty] private string _texto = "--";
    [ObservableProperty] private bool _disponivel = true;
    [ObservableProperty] private string? _detalhe;
    [ObservableProperty] private IReadOnlyList<double> _serie = Array.Empty<double>();

    /// <summary>Percentual para a barra, mesmo quando a unidade nao e porcentagem.</summary>
    public double Percentual => Maximo <= 0 ? 0 : Math.Clamp(Valor / Maximo * 100.0, 0, 100);

    partial void OnValorChanged(double value) => OnPropertyChanged(nameof(Percentual));
}

public sealed partial class FindingViewModel : ObservableObject
{
    public FindingViewModel(Finding finding)
    {
        Finding = finding;
    }

    public Finding Finding { get; }

    public string Titulo => Finding.Titulo;
    public string Detalhe => Finding.Detalhe;
    public string? TextoDaAcao => Finding.TextoDaAcao;
    public bool TemAcao => Finding.Acao != FindingAcao.Nenhuma && Finding.TextoDaAcao is not null;
    public FindingSeverity Severidade => Finding.Severidade;

    public string TextoDeSeveridade => Finding.Severidade switch
    {
        FindingSeverity.Critico => "Critico",
        FindingSeverity.Atencao => "Atencao",
        _ => "Informativo"
    };
}

/// <summary>
/// Painel de diagnostico (secao 5.5): cinco medidores ao vivo, grafico de 60 s,
/// top 10 processos e a lista de findings em linguagem humana.
///
/// A coleta roda em Task propria dentro do BottleneckMonitor; aqui so
/// devolvemos cada tique para a thread da UI (regra 10).
/// </summary>
public sealed partial class DiagnosticoPageViewModel : PageViewModelBase, IDisposable
{
    private readonly BottleneckMonitor _monitor;
    private readonly HealthReportModule _relatorio;
    private readonly IMemoryService _memoria;
    private readonly IGameBoostLogger _log;
    private bool _inscrito;

    public DiagnosticoPageViewModel(
        BottleneckMonitor monitor,
        HealthReportModule relatorio,
        IMemoryService memoria,
        IGameBoostLogger log)
    {
        _monitor = monitor;
        _relatorio = relatorio;
        _memoria = memoria;
        _log = log;

        Cpu = new MedidorViewModel("CPU", "%", 100);
        Gpu = new MedidorViewModel("GPU", "%", 100);
        Ram = new MedidorViewModel("Memoria", "%", 100);
        Disco = new MedidorViewModel("Disco", "%", 100);
        Rede = new MedidorViewModel("Rede", "MB/s", 10);

        Medidores = new ObservableCollection<MedidorViewModel> { Cpu, Gpu, Ram, Disco, Rede };

        EhAdministrador = CoreServices.RodandoComoAdministrador();
    }

    public override string Nome => "Diagnostico";
    public override string Titulo => "Diagnostico de gargalos";
    public override string Subtitulo => "O que esta consumindo a maquina agora, e quem e o culpado.";
    public override string Icone => "\uE9D9";

    public ObservableCollection<MedidorViewModel> Medidores { get; }
    public MedidorViewModel Cpu { get; }
    public MedidorViewModel Gpu { get; }
    public MedidorViewModel Ram { get; }
    public MedidorViewModel Disco { get; }
    public MedidorViewModel Rede { get; }

    public ObservableCollection<ProcessUsage> TopProcessos { get; } = new();
    public ObservableCollection<FindingViewModel> Findings { get; } = new();

    [ObservableProperty] private bool _monitorando;
    [ObservableProperty] private bool _ehAdministrador;
    [ObservableProperty] private string _status = "Clique em Iniciar para acompanhar a maquina em tempo real.";
    [ObservableProperty] private string? _temperaturaCpu;
    [ObservableProperty] private string? _jogoDetectado;
    [ObservableProperty] private string _custoDaColeta = string.Empty;

    public bool SemFindings => Findings.Count == 0 && Monitorando;

    /// <summary>Ao entrar na pagina o monitoramento comeca sozinho: e o que o usuario quer ver.</summary>
    public override void AoEntrar()
    {
        if (!Monitorando)
            Iniciar();
    }

    /// <summary>Sair da pagina para a coleta: nao faz sentido medir o que ninguem esta vendo.</summary>
    public override void AoSair() => Parar();

    [RelayCommand]
    private void Iniciar()
    {
        if (Monitorando)
            return;

        if (!_inscrito)
        {
            _monitor.AoColetar += AoColetar;
            _inscrito = true;
        }

        _monitor.Iniciar();
        Monitorando = true;
        Status = "Acompanhando a maquina. A coleta roda uma vez por segundo.";
    }

    [RelayCommand]
    private void Parar()
    {
        if (!Monitorando)
            return;

        _monitor.Parar();
        Monitorando = false;
        Status = $"Monitoramento parado. Custo medio da coleta: {_monitor.CustoMedioDeCpuPercent:0.00}% de CPU.";
    }

    private void AoColetar(MonitorTick tique)
    {
        // O monitor roda em Task propria; tudo daqui para baixo e UI.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        dispatcher.InvokeAsync(() => Aplicar(tique));
    }

    private void Aplicar(MonitorTick tique)
    {
        var s = tique.Snapshot;
        var historico = _monitor.Buffer.Ultimos(TimeSpan.FromSeconds(60));

        Cpu.Valor = s.CpuPercent;
        Cpu.Texto = $"{s.CpuPercent:0}%";
        Cpu.Detalhe = $"{s.NucleosLogicos} nucleos logicos";
        Cpu.Serie = historico.Select(h => h.CpuPercent).ToList();

        if (s.GpuPercent.Disponivel)
        {
            Gpu.Disponivel = true;
            Gpu.Valor = s.GpuPercent.Valor;
            Gpu.Texto = $"{s.GpuPercent.Valor:0}%";
            Gpu.Serie = historico.Select(h => h.GpuPercent.OuZero).ToList();
            Gpu.Detalhe = s.GpuMemoriaBytes.Disponivel
                ? $"{GameModeModule.Formatar((long)s.GpuMemoriaBytes.Valor)} de VRAM em uso"
                : null;
        }
        else
        {
            Gpu.Disponivel = false;
            Gpu.Texto = "indisponivel";
            Gpu.Detalhe = "Os contadores de GPU nao existem nesta maquina.";
        }

        Ram.Valor = s.RamUsadaPercent;
        Ram.Texto = $"{s.RamUsadaPercent:0}%";
        Ram.Detalhe = $"{GameModeModule.Formatar(s.RamDisponivelBytes)} livres de {GameModeModule.Formatar(s.RamTotalBytes)}"
                    + (s.StandbyBytes > 0 ? $"  ·  {GameModeModule.Formatar(s.StandbyBytes)} em cache" : string.Empty);
        Ram.Serie = historico.Select(h => h.RamUsadaPercent).ToList();

        Disco.Valor = s.DiscoSistemaUsadoPercent;
        Disco.Texto = $"{s.DiscoSistemaUsadoPercent:0}%";
        Disco.Detalhe = "ocupacao do disco do sistema";
        Disco.Serie = historico.Select(h => h.DiscoSistemaUsadoPercent).ToList();

        if (s.RedeBytesPorSegundo.Disponivel)
        {
            var mbps = s.RedeBytesPorSegundo.Valor / (1024.0 * 1024.0);
            Rede.Disponivel = true;
            Rede.Valor = mbps;
            Rede.Texto = $"{mbps:0.0} MB/s";
            Rede.Detalhe = "soma das interfaces ativas";
            Rede.Serie = historico
                .Select(h => h.RedeBytesPorSegundo.OuZero / (1024.0 * 1024.0))
                .ToList();
        }
        else
        {
            Rede.Disponivel = false;
            Rede.Texto = "--";
        }

        TemperaturaCpu = s.TemperaturaCpu.Disponivel
            ? $"CPU a {s.TemperaturaCpu.Valor:0} C"
            : null;

        TopProcessos.Clear();
        foreach (var p in s.Processos.Take(10))
            TopProcessos.Add(p);

        AtualizarFindings(tique.Findings);

        CustoDaColeta = $"A propria coleta custa {_monitor.CustoMedioDeCpuPercent:0.00}% de CPU.";
    }

    private void AtualizarFindings(IReadOnlyList<Finding> novos)
    {
        // Reconstroi so quando o conjunto muda: a lista nao pode piscar a cada segundo.
        var idsAtuais = Findings.Select(f => f.Finding.Id).ToList();
        var idsNovos = novos.Select(f => f.Id).ToList();

        if (idsAtuais.SequenceEqual(idsNovos))
            return;

        Findings.Clear();
        foreach (var f in novos)
            Findings.Add(new FindingViewModel(f));

        OnPropertyChanged(nameof(SemFindings));
    }

    /// <summary>Botao de acao de um finding. Cada acao leva para onde resolve.</summary>
    [RelayCommand]
    private void ExecutarAcao(FindingViewModel? item)
    {
        if (item is null)
            return;

        switch (item.Finding.Acao)
        {
            case FindingAcao.LimparRam:
            case FindingAcao.PurgarStandby:
                if (!EhAdministrador)
                {
                    Status = "Limpar memoria exige executar como administrador.";
                    return;
                }

                var antes = _memoria.GetSnapshot();
                var ok = _memoria.PurgeStandbyList();
                var depois = _memoria.GetSnapshot();
                var ganho = depois.AvailableBytes - antes.AvailableBytes;

                // Regra 4: mostra o ganho medido, inclusive quando e zero.
                Status = ok
                    ? ganho > 0
                        ? $"Standby List purgada: {GameModeModule.Formatar(ganho)} a mais de RAM livre."
                        : "Standby List purgada, mas sem ganho mensuravel de memoria."
                    : "Nao foi possivel purgar a Standby List.";
                break;

            case FindingAcao.AbrirSegurancaDoWindows:
                Abrir("windowsdefender://coreisolation");
                break;

            case FindingAcao.AbrirConfiguracoesDeVideo:
                Abrir("ms-settings:display-advanced");
                break;

            case FindingAcao.AtivarAltoDesempenho:
                Abrir("ms-settings:powersleep");
                break;

            case FindingAcao.AbrirSiteDoDriver:
                Abrir(EnderecoDoDriver(item.Finding));
                break;

            default:
                Status = item.Finding.TextoDaAcao is null
                    ? "Este achado e informativo."
                    : $"{item.Finding.TextoDaAcao}: disponivel quando o modulo correspondente chegar.";
                break;
        }
    }

    private static string EnderecoDoDriver(Finding finding)
    {
        if (finding.Titulo.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            return "https://www.nvidia.com/pt-br/drivers/";

        if (finding.Titulo.Contains("AMD", StringComparison.OrdinalIgnoreCase))
            return "https://www.amd.com/pt/support";

        if (finding.Titulo.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            return "https://www.intel.com.br/content/www/br/pt/download-center/home.html";

        return "https://www.google.com/search?q=atualizar+driver+de+video";
    }

    private void Abrir(string destino)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = destino, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.Warn("Diagnostico", "Abrir", destino, ex.Message);
            Status = "Nao foi possivel abrir esse destino.";
        }
    }

    public void Dispose()
    {
        if (_inscrito)
        {
            _monitor.AoColetar -= AoColetar;
            _inscrito = false;
        }

        _monitor.Parar();
    }
}
