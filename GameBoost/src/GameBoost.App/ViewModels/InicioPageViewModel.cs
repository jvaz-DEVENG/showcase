using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBoost.Core.Logging;
using GameBoost.Core.Modules;
using GameBoost.Core.Modules.Bottleneck;
using GameBoost.Core.Modules.GameMode;
using GameBoost.Core.Modules.HealthReport;
using GameBoost.Core.State;

namespace GameBoost.App.ViewModels;

/// <summary>
/// Pagina Inicio: Modo Game, pontuacao e findings (secao 6).
///
/// Herda o padrao de pagina de modulo, mas troca o rodape generico
/// "Aplicar N selecionados" pelos botoes do Modo Game, que e o que o usuario
/// espera encontrar na abertura.
/// </summary>
public sealed partial class InicioPageViewModel : ModulePageViewModel
{
    private readonly GameModeModule _gameMode;
    private readonly IStateBackup _backup;
    private readonly IRollbackEngine _rollback;
    private readonly ISessionStore _sessions;
    private readonly HealthReportModule _saude;
    private readonly IGameBoostLogger _log;
    private CancellationTokenSource? _ctsSaude;

    public InicioPageViewModel(
        GameModeModule gameMode,
        IStateBackup backup,
        IRollbackEngine rollback,
        ISessionStore sessions,
        HealthReportModule saude,
        IGameBoostLogger log)
        : base(gameMode, backup)
    {
        _gameMode = gameMode;
        _saude = saude;
        _backup = backup;
        _rollback = rollback;
        _sessions = sessions;
        _log = log;

        // Aviso na abertura se o app morreu com o Modo Game ativo (secao 1).
        var sessao = _sessions.Load();
        if (sessao.Ativo)
        {
            ModoGameAtivo = true;
            AvisoDeRestauracao =
                "O GameBoost foi fechado com o Modo Game ainda ativo. " +
                "Clique em Restaurar agora para devolver o sistema ao estado anterior.";
        }
        else if (_backup.TemPendencias)
        {
            AvisoDeRestauracao =
                $"Existem {_backup.Pendentes.Count} alteracoes de sistema pendentes de reversao.";
        }
    }

    public override string Nome => "Inicio";
    public override string Titulo => "Modo Game";
    public override string Subtitulo => "Encerra o que voce confirmar, libera RAM e prepara o sistema para o jogo.";
    public override string Icone => "\uE80F";

    [ObservableProperty] private bool _modoGameAtivo;
    [ObservableProperty] private string? _avisoDeRestauracao;

    // ------------------------------------------------------------------
    // Pontuacao de saude e principais findings (secoes 5.12 e 5.5)
    // ------------------------------------------------------------------

    public ObservableCollection<AreaScore> Areas { get; } = new();
    public ObservableCollection<FindingViewModel> PrincipaisFindings { get; } = new();

    [ObservableProperty] private bool _pontuacaoDisponivel;
    [ObservableProperty] private bool _analisando;
    [ObservableProperty] private int _notaGeral;
    [ObservableProperty] private string _conceito = string.Empty;
    [ObservableProperty] private string _progressoDaAnalise = string.Empty;

    public string PontuacaoPlaceholder =>
        "Clique em Analisar para medir a maquina e ver a nota de saude com os principais achados.";

    /// <summary>Roda o relatorio de saude completo: mede por alguns segundos e pontua.</summary>
    [RelayCommand]
    private async Task AnalisarSaudeAsync()
    {
        if (Analisando)
            return;

        _ctsSaude = new CancellationTokenSource();
        Analisando = true;
        ProgressoDaAnalise = "Medindo a maquina...";

        var progresso = new Progress<ModuleProgress>(p => ProgressoDaAnalise = p.Etapa);

        try
        {
            var relatorio = await _saude.GerarAsync(progresso, _ctsSaude.Token);

            NotaGeral = relatorio.Pontuacao.NotaGeral;
            Conceito = relatorio.Pontuacao.Conceito;

            Areas.Clear();
            foreach (var area in relatorio.Pontuacao.Areas)
                Areas.Add(area);

            PrincipaisFindings.Clear();
            foreach (var f in relatorio.Pontuacao.Principais)
                PrincipaisFindings.Add(new FindingViewModel(f));

            PontuacaoDisponivel = true;
            ProgressoDaAnalise = PrincipaisFindings.Count == 0
                ? "Nada fora do lugar."
                : $"{PrincipaisFindings.Count} achados. Detalhes no Diagnostico.";
        }
        catch (OperationCanceledException)
        {
            ProgressoDaAnalise = "Analise cancelada.";
        }
        finally
        {
            Analisando = false;
        }
    }

    [RelayCommand]
    private void CancelarAnalise() => _ctsSaude?.Cancel();

    /// <summary>Exportar o relatorio para mandar a quem da suporte (secao 5.12).</summary>
    [RelayCommand]
    private async Task ExportarRelatorioAsync()
    {
        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"gameboost-saude-{DateTime.Now:yyyy-MM-dd-HHmm}.html",
            Filter = "Pagina HTML (*.html)|*.html|Dados JSON (*.json)|*.json",
            Title = "Salvar relatorio de saude"
        };

        if (dialogo.ShowDialog() != true)
            return;

        Analisando = true;
        ProgressoDaAnalise = "Medindo a maquina para o relatorio...";

        try
        {
            var relatorio = await _saude.GerarAsync(
                new Progress<ModuleProgress>(p => ProgressoDaAnalise = p.Etapa),
                CancellationToken.None);

            var json = dialogo.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            var conteudo = json
                ? HtmlReportWriter.GerarJson(relatorio)
                : HtmlReportWriter.GerarHtml(relatorio);

            File.WriteAllText(dialogo.FileName, conteudo, new System.Text.UTF8Encoding(false));

            ProgressoDaAnalise = $"Relatorio salvo em {dialogo.FileName}";
            _log.Info("Inicio", "ExportarRelatorio", dialogo.FileName, $"nota {relatorio.Pontuacao.NotaGeral}");
        }
        catch (IOException ex)
        {
            ProgressoDaAnalise = $"Nao foi possivel salvar: {ex.Message}";
        }
        finally
        {
            Analisando = false;
        }
    }

    protected override string MontarConfirmacao(IReadOnlyList<ActionItemViewModel> selecionados)
    {
        if (selecionados.Count == 0)
            return "Nenhum app foi marcado. O Modo Game vai apenas ajustar o sistema "
                 + "(energia, gravacao, Windows Update). Continuar?";

        var nomes = string.Join("\n  ", selecionados.Take(12).Select(i => i.Titulo));
        var extra = selecionados.Count > 12 ? $"\n  ... e mais {selecionados.Count - 12}" : string.Empty;

        return $"O GameBoost vai encerrar {selecionados.Count} app(s):\n\n  {nomes}{extra}\n\n"
             + "Cada um recebe primeiro o pedido de fechamento normal, com 3 segundos para salvar.\n\n"
             + "Continuar?";
    }

    protected override void AposAplicar(ApplyResult resultado)
    {
        if (resultado.DryRun)
            return;

        ModoGameAtivo = true;
        AvisoDeRestauracao = null;
    }

    protected override void AposReverter(ApplyResult resultado)
    {
        if (resultado.DryRun)
            return;

        ModoGameAtivo = false;
        AvisoDeRestauracao = null;
    }

    /// <summary>Botao Reverter tudo: sempre visivel, desfaz qualquer alteracao pendente.</summary>
    [RelayCommand]
    private void ReverterTudo()
    {
        if (!EhAdministrador)
            return;

        if (!_backup.TemPendencias)
        {
            Status = "Nada a reverter: nenhuma alteracao pendente.";
            return;
        }

        var pendentes = _backup.Pendentes.Count;
        var resposta = MessageBox.Show(
            $"Desfazer {pendentes} alteracao(oes) de sistema feitas pelo GameBoost?",
            "Reverter tudo", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (resposta != MessageBoxResult.Yes)
            return;

        var resultados = _rollback.ReverterTudo(DryRun);
        var falhas = resultados.Count(r => !r.Sucesso);

        ModoGameAtivo = false;
        AvisoDeRestauracao = null;
        Status = falhas == 0
            ? $"{resultados.Count} alteracoes revertidas."
            : $"{resultados.Count - falhas} revertidas, {falhas} falharam. Veja o log.";

        _log.Info("App", "ReverterTudo", null, Status);
        OnPropertyChanged(nameof(TemPendencias));
    }
}
