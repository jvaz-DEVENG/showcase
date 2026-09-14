using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBoost.Core.Abstractions;
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
/// <summary>Uma linha da lista de restauracao.</summary>
public sealed class AppParaReabrir
{
    public AppParaReabrir(Core.Modules.GameMode.ClosedApp item)
    {
        Item = item;
    }

    public Core.Modules.GameMode.ClosedApp Item { get; }

    public string Nome => Item.Nome;

    public string Detalhe => Item.Essencial
        ? "marcado como essencial"
        : "não reabre sozinho porque não está marcado com estrela";
}

public sealed partial class InicioPageViewModel : ModulePageViewModel
{
    private readonly GameModeModule _gameMode;
    private readonly IStateBackup _backup;
    private readonly IRollbackEngine _rollback;
    private readonly ISessionStore _sessions;
    private readonly HealthReportModule _saude;
    private readonly IMemoryService _memoria;
    private readonly IProcessService _processos;
    private readonly IGameBoostLogger _log;
    private CancellationTokenSource? _ctsSaude;

    public InicioPageViewModel(
        GameModeModule gameMode,
        IStateBackup backup,
        IRollbackEngine rollback,
        ISessionStore sessions,
        HealthReportModule saude,
        IMemoryService memoria,
        IProcessService processos,
        IGameBoostLogger log)
        : base(gameMode, backup)
    {
        _gameMode = gameMode;
        _memoria = memoria;
        _processos = processos;
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
            AtualizarListaDeRestauracao();
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

    /// <summary>
    /// Apps encerrados que ainda nao voltaram. Sem estrela eles nao reabrem
    /// sozinhos, e ate agora o app anunciava "N aguardando na lista de
    /// restauracao" sem existir lista nenhuma para agir.
    /// </summary>
    public ObservableCollection<AppParaReabrir> ParaReabrir { get; } = new();

    public bool TemAppsParaReabrir => ParaReabrir.Count > 0;

    private void AtualizarListaDeRestauracao()
    {
        ParaReabrir.Clear();

        foreach (var app in _sessions.Load().AppsEncerrados.Where(a => !a.Reaberto))
        {
            if (!string.IsNullOrWhiteSpace(app.ExecutablePath))
                ParaReabrir.Add(new AppParaReabrir(app));
        }

        OnPropertyChanged(nameof(TemAppsParaReabrir));
    }

    /// <summary>Reabre um app da lista, e so ele.</summary>
    [RelayCommand]
    private void Reabrir(AppParaReabrir? alvo)
    {
        if (alvo?.Item.ExecutablePath is null)
            return;

        var ok = _processos.Start(
            alvo.Item.ExecutablePath, alvo.Item.Argumentos, alvo.Item.WorkingDirectory);

        if (!ok)
        {
            Status = $"Não foi possível reabrir {alvo.Nome}. "
                   + "Alguns apps da Microsoft Store só abrem pelo menu Iniciar.";
            return;
        }

        // Marcar no arquivo, senao ele reaparece na lista na proxima abertura.
        var sessao = _sessions.Load();

        foreach (var app in sessao.AppsEncerrados.Where(a =>
                     string.Equals(a.Nome, alvo.Item.Nome, StringComparison.OrdinalIgnoreCase)))
        {
            app.Reaberto = true;
        }

        _sessions.Save(sessao);

        ParaReabrir.Remove(alvo);
        OnPropertyChanged(nameof(TemAppsParaReabrir));

        Status = $"{alvo.Nome} reaberto.";
        _log.Info("gamemode", "Reabrir", alvo.Nome, "pela lista de restauração");
    }

    /// <summary>Encerra a sessao no arquivo e limpa a lista da tela.</summary>
    private void EncerrarSessao()
    {
        var sessao = _sessions.Load();

        if (!sessao.Ativo && sessao.AppsEncerrados.Count == 0)
            return;

        sessao.Ativo = false;
        _sessions.Save(sessao);

        AtualizarListaDeRestauracao();
    }

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

        // O que nao tinha estrela nao volta sozinho: aparece aqui com botao.
        AtualizarListaDeRestauracao();
    }

    /// <summary>
    /// Libera memoria sem ativar o Modo Game. E o item "Limpar RAM" da bandeja.
    ///
    /// O ganho e medido, nao estimado: mede a RAM livre antes, roda, mede
    /// depois e mostra a diferenca. Boosters costumam anunciar aqui um numero
    /// inventado; quando o Windows ja estava bem de memoria, o numero honesto
    /// e "quase nada", e e isso que aparece.
    /// </summary>
    [RelayCommand]
    private void LimparRam()
    {
        if (!EhAdministrador)
        {
            Status = "Liberar memoria precisa de privilegios de administrador.";
            return;
        }

        var antes = _memoria.GetSnapshot();

        var processos = 0;

        foreach (var p in _processos.GetProcesses())
        {
            if (_processos.TrimWorkingSet(p.Pid) > 0)
                processos++;
        }

        var purgou = _memoria.PurgeStandbyList();
        var depois = _memoria.GetSnapshot();
        var ganho = depois.AvailableBytes - antes.AvailableBytes;

        Status = ganho > 0
            ? $"{Core.Modules.GameMode.GameModeModule.Formatar(ganho)} a mais de RAM livre "
            + $"({processos} processos{(purgou ? ", Standby List purgada" : string.Empty)})."
            : "A memoria ja estava livre: nao havia o que liberar. "
            + "Isso e normal em maquina com RAM sobrando.";

        _log.Info("gamemode", "LimparRam", null, Status);
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

        // Encerrar a sessao no arquivo, e nao so na tela.
        //
        // Sem isto, o session.json continuava dizendo Ativo=true para sempre, e
        // o aviso "foi fechado com o Modo Game ainda ativo" voltava em TODA
        // abertura — mesmo com zero pendencias. Um aviso que aparece sempre
        // deixa de ser aviso.
        if (!DryRun)
            EncerrarSessao();

        ModoGameAtivo = false;
        AvisoDeRestauracao = null;
        Status = falhas == 0
            ? $"{resultados.Count} alteracoes revertidas."
            : $"{resultados.Count - falhas} revertidas, {falhas} falharam. Veja o log.";

        _log.Info("App", "ReverterTudo", null, Status);
        OnPropertyChanged(nameof(TemPendencias));
    }
}
