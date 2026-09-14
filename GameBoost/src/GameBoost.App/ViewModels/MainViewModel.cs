using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBoost.Core;
using GameBoost.Core.Logging;
using GameBoost.Core.Modules;
using GameBoost.Core.Modules.GameMode;
using GameBoost.Core.State;

namespace GameBoost.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly GameModeModule _gameMode;
    private readonly IStateBackup _backup;
    private readonly IRollbackEngine _rollback;
    private readonly ISessionStore _sessions;
    private readonly IGameBoostLogger _log;

    private CancellationTokenSource? _cts;

    public MainViewModel(
        GameModeModule gameMode,
        IStateBackup backup,
        IRollbackEngine rollback,
        ISessionStore sessions,
        IGameBoostLogger log)
    {
        _gameMode = gameMode;
        _backup = backup;
        _rollback = rollback;
        _sessions = sessions;
        _log = log;

        EhAdministrador = CoreServices.RodandoComoAdministrador();

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

        Status = EhAdministrador
            ? "Pronto. Clique em Varrer para ver o que esta aberto."
            : "Modo somente leitura: sem privilegios de administrador as varreduras funcionam, mas nada pode ser alterado.";
    }

    public ObservableCollection<ActionItemViewModel> Itens { get; } = new();

    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _resumo = string.Empty;
    [ObservableProperty] private string? _avisoDeRestauracao;
    [ObservableProperty] private string? _ganhoMedido;
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private int _progresso;
    [ObservableProperty] private bool _modoGameAtivo;
    [ObservableProperty] private bool _ehAdministrador;
    [ObservableProperty] private bool _dryRun;

    public bool PodeAgir => !Ocupado && EhAdministrador;
    public int Selecionados => Itens.Count(i => i.Selecionado);

    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task VarrerAsync()
    {
        if (Ocupado)
            return;

        _cts = new CancellationTokenSource();
        Ocupado = true;
        Progresso = 0;
        Itens.Clear();
        GanhoMedido = null;

        var progresso = new Progress<ModuleProgress>(p =>
        {
            Status = p.Etapa;
            Progresso = p.Percentual;
        });

        try
        {
            var resultado = await _gameMode.ScanAsync(progresso, _cts.Token);

            foreach (var item in resultado.Itens)
                Itens.Add(new ActionItemViewModel(item));

            Resumo = resultado.Resumo;
            Status = resultado.Avisos.Count > 0
                ? string.Join("  ", resultado.Avisos)
                : "Varredura concluida. Revise a lista antes de confirmar.";
        }
        catch (OperationCanceledException)
        {
            Status = "Varredura cancelada.";
        }
        finally
        {
            Ocupado = false;
            Progresso = 100;
            OnPropertyChanged(nameof(Selecionados));
        }
    }

    [RelayCommand]
    private async Task AtivarAsync()
    {
        if (Ocupado || !EhAdministrador)
            return;

        var selecionados = Itens.Where(i => i.Selecionado && i.PodeSelecionar).Select(i => i.Id).ToList();

        if (selecionados.Count == 0 && !DryRun)
        {
            var resposta = MessageBox.Show(
                "Nenhum app foi marcado. O Modo Game vai apenas ajustar o sistema (energia, gravacao, Windows Update). Continuar?",
                "GameBoost", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (resposta != MessageBoxResult.Yes)
                return;
        }
        else if (!DryRun)
        {
            // Tela de confirmacao unica: nada e encerrado antes do usuario confirmar.
            var nomes = string.Join("\n  ", Itens.Where(i => i.Selecionado).Take(12).Select(i => i.Titulo));
            var extra = selecionados.Count > 12 ? $"\n  ... e mais {selecionados.Count - 12}" : string.Empty;

            var resposta = MessageBox.Show(
                $"O GameBoost vai encerrar {selecionados.Count} app(s):\n\n  {nomes}{extra}\n\n" +
                "Cada um recebe primeiro o pedido de fechamento normal, com 3 segundos para salvar.\n\n" +
                "Continuar?",
                "Confirmar Modo Game", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (resposta != MessageBoxResult.Yes)
                return;
        }

        _cts = new CancellationTokenSource();
        Ocupado = true;

        try
        {
            var resultado = await _gameMode.ApplyAsync(selecionados, DryRun, _cts.Token);

            Resumo = resultado.Resumo;
            GanhoMedido = resultado.GanhoMedido;
            ModoGameAtivo = !DryRun;
            AvisoDeRestauracao = null;

            Status = resultado.Falhas > 0
                ? $"{resultado.Sucessos} acoes concluidas, {resultado.Falhas} falharam. Veja o log."
                : "Modo Game ativo.";
        }
        catch (OperationCanceledException)
        {
            Status = "Ativacao cancelada.";
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private async Task DesligarAsync()
    {
        if (Ocupado || !EhAdministrador)
            return;

        _cts = new CancellationTokenSource();
        Ocupado = true;

        try
        {
            var resultado = await _gameMode.RevertAsync(Array.Empty<string>(), DryRun, _cts.Token);

            Resumo = resultado.Resumo;
            GanhoMedido = resultado.GanhoMedido;
            ModoGameAtivo = DryRun && ModoGameAtivo;
            AvisoDeRestauracao = null;
            Status = "Sistema devolvido ao estado anterior.";
        }
        finally
        {
            Ocupado = false;
        }
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
    }

    [RelayCommand]
    private void Cancelar() => _cts?.Cancel();

    partial void OnOcupadoChanged(bool value) => OnPropertyChanged(nameof(PodeAgir));

    partial void OnEhAdministradorChanged(bool value) => OnPropertyChanged(nameof(PodeAgir));
}
