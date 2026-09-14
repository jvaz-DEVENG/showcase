using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBoost.Core;
using GameBoost.Core.Modules;
using GameBoost.Core.State;

namespace GameBoost.App.ViewModels;

/// <summary>
/// Padrao de pagina de modulo da secao 6, pronto para reuso: cabecalho com
/// botao Varrer e resumo, lista generica de ActionItem agrupada por categoria,
/// rodape fixo com "Aplicar N selecionados" e "Reverter".
///
/// Todo modulo das fases seguintes so precisa implementar IModule e herdar
/// daqui — nenhuma tela nova.
/// </summary>
public abstract partial class ModulePageViewModel : PageViewModelBase
{
    private readonly IModule _modulo;
    private readonly IStateBackup _backup;
    private CancellationTokenSource? _cts;

    protected ModulePageViewModel(IModule modulo, IStateBackup backup)
    {
        _modulo = modulo;
        _backup = backup;
        EhAdministrador = CoreServices.RodandoComoAdministrador();

        Status = EhAdministrador
            ? "Clique em Varrer para ver o que pode ser feito."
            : "Modo somente leitura: sem privilegios de administrador as varreduras funcionam, mas nada pode ser alterado.";
    }

    public ObservableCollection<ActionItemViewModel> Itens { get; } = new();

    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _resumo = string.Empty;
    [ObservableProperty] private string? _ganhoMedido;
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private int _progresso;
    [ObservableProperty] private bool _ehAdministrador;
    [ObservableProperty] private bool _dryRun;
    [ObservableProperty] private bool _jaVarreu;

    public bool PodeAgir => !Ocupado && EhAdministrador;

    public int Selecionados => Itens.Count(i => i.Selecionado);

    public string TextoAplicar => Selecionados == 1
        ? "Aplicar 1 selecionado"
        : $"Aplicar {Selecionados} selecionados";

    public bool TemPendencias => _backup.Pendentes.Any(r => r.Modulo == _modulo.Id);

    /// <summary>Texto da confirmacao antes de aplicar. Cada modulo explica o seu.</summary>
    protected abstract string MontarConfirmacao(IReadOnlyList<ActionItemViewModel> selecionados);

    [RelayCommand]
    public async Task VarrerAsync()
    {
        if (Ocupado)
            return;

        _cts = new CancellationTokenSource();
        Ocupado = true;
        Progresso = 0;
        GanhoMedido = null;

        foreach (var antigo in Itens)
            antigo.PropertyChanged -= AoMudarSelecao;

        Itens.Clear();

        var progresso = new Progress<ModuleProgress>(p =>
        {
            Status = p.Etapa;
            Progresso = p.Percentual;
        });

        try
        {
            var resultado = await _modulo.ScanAsync(progresso, _cts.Token);

            foreach (var item in resultado.Itens)
            {
                var vm = new ActionItemViewModel(item);
                vm.PropertyChanged += AoMudarSelecao;
                Itens.Add(vm);
            }

            Resumo = resultado.Resumo;
            Status = resultado.Avisos.Count > 0
                ? string.Join("  ", resultado.Avisos)
                : "Varredura concluida. Revise a lista antes de confirmar.";
            JaVarreu = true;
        }
        catch (OperationCanceledException)
        {
            Status = "Varredura cancelada.";
        }
        finally
        {
            Ocupado = false;
            Progresso = 100;
            NotificarSelecao();
        }
    }

    [RelayCommand]
    private async Task AplicarAsync()
    {
        if (Ocupado || !EhAdministrador)
            return;

        var selecionados = Itens.Where(i => i.Selecionado && i.PodeSelecionar).ToList();

        if (!DryRun && !Confirmar(selecionados))
            return;

        _cts = new CancellationTokenSource();
        Ocupado = true;

        try
        {
            var resultado = await _modulo.ApplyAsync(
                selecionados.Select(i => i.Id).ToList(), DryRun, _cts.Token);

            Resumo = resultado.Resumo;
            GanhoMedido = resultado.GanhoMedido;
            Status = resultado.Falhas > 0
                ? $"{resultado.Sucessos} acoes concluidas, {resultado.Falhas} falharam. Veja o log."
                : "Pronto.";

            AposAplicar(resultado);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelado.";
        }
        finally
        {
            Ocupado = false;
            OnPropertyChanged(nameof(TemPendencias));
        }
    }

    [RelayCommand]
    private async Task ReverterAsync()
    {
        if (Ocupado || !EhAdministrador)
            return;

        var pendentes = _backup.Pendentes.Where(r => r.Modulo == _modulo.Id).ToList();

        if (pendentes.Count == 0)
        {
            Status = "Nada a reverter nesta pagina.";
            return;
        }

        var resposta = MessageBox.Show(
            $"Desfazer {pendentes.Count} alteracao(oes) de sistema feitas por {Titulo}?",
            "Reverter", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (resposta != MessageBoxResult.Yes)
            return;

        Ocupado = true;

        try
        {
            var resultado = await _modulo.RevertAsync(
                pendentes.Select(r => r.Id).ToList(), DryRun, CancellationToken.None);

            Resumo = resultado.Resumo;
            GanhoMedido = resultado.GanhoMedido;
            Status = resultado.Falhas > 0
                ? $"{resultado.Sucessos} revertidas, {resultado.Falhas} falharam."
                : "Sistema devolvido ao estado anterior.";

            AposReverter(resultado);
        }
        finally
        {
            Ocupado = false;
            OnPropertyChanged(nameof(TemPendencias));
        }
    }

    [RelayCommand]
    private void Cancelar() => _cts?.Cancel();

    [RelayCommand]
    private void MarcarTodos()
    {
        foreach (var item in Itens.Where(i => i.PodeSelecionar))
            item.Selecionado = true;
    }

    [RelayCommand]
    private void DesmarcarTodos()
    {
        foreach (var item in Itens)
            item.Selecionado = false;
    }

    protected virtual bool Confirmar(IReadOnlyList<ActionItemViewModel> selecionados)
    {
        var resposta = MessageBox.Show(
            MontarConfirmacao(selecionados),
            $"Confirmar - {Titulo}", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        return resposta == MessageBoxResult.Yes;
    }

    protected virtual void AposAplicar(ApplyResult resultado)
    {
    }

    protected virtual void AposReverter(ApplyResult resultado)
    {
    }

    private void AoMudarSelecao(object? remetente, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActionItemViewModel.Selecionado))
            NotificarSelecao();
    }

    protected void NotificarSelecao()
    {
        OnPropertyChanged(nameof(Selecionados));
        OnPropertyChanged(nameof(TextoAplicar));
    }

    partial void OnOcupadoChanged(bool value) => OnPropertyChanged(nameof(PodeAgir));

    partial void OnEhAdministradorChanged(bool value) => OnPropertyChanged(nameof(PodeAgir));
}
