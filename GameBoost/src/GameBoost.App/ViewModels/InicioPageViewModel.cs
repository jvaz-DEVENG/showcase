using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBoost.Core.Logging;
using GameBoost.Core.Modules;
using GameBoost.Core.Modules.GameMode;
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
    private readonly IGameBoostLogger _log;

    public InicioPageViewModel(
        GameModeModule gameMode,
        IStateBackup backup,
        IRollbackEngine rollback,
        ISessionStore sessions,
        IGameBoostLogger log)
        : base(gameMode, backup)
    {
        _gameMode = gameMode;
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

    /// <summary>
    /// Reservado para a Fase 1: pontuacao 0 a 100 por area e os 5 principais
    /// findings do Relatorio de Saude (secoes 5.12 e 5.5).
    /// </summary>
    public bool PontuacaoDisponivel => false;

    public string PontuacaoPlaceholder =>
        "A pontuacao de saude e o diagnostico de gargalos aparecem aqui na Fase 1.";

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
