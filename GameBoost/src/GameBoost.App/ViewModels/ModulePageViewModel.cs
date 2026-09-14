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

    /// <summary>
    /// Tudo o que a varredura achou. A lista que a tela mostra e a
    /// <see cref="Itens"/>, que e esta filtrada pela busca.
    /// </summary>
    private readonly List<ActionItemViewModel> _todos = new();

    public ObservableCollection<ActionItemViewModel> Itens { get; } = new();

    /// <summary>
    /// Texto da busca. Procura no titulo e na descricao, sem acento e sem
    /// diferenciar maiuscula: quem procura "notepad" tem que achar "Notepad", e
    /// quem procura "gravacao" tem que achar "Gravação".
    /// </summary>
    [ObservableProperty] private string _busca = string.Empty;

    partial void OnBuscaChanged(string value) => AplicarBusca();

    private void AplicarBusca()
    {
        Itens.Clear();

        var termo = Normalizar(Busca);

        foreach (var item in _todos)
        {
            if (termo.Length == 0
                || Normalizar(item.Titulo).Contains(termo, StringComparison.Ordinal)
                || Normalizar(item.Item.Descricao).Contains(termo, StringComparison.Ordinal))
            {
                Itens.Add(item);
            }
        }

        OnPropertyChanged(nameof(TextoDoFiltro));
        OnPropertyChanged(nameof(ListaVaziaPorBusca));
    }

    /// <summary>Tira acento e caixa para a busca casar do jeito que se digita.</summary>
    private static string Normalizar(string texto)
    {
        var decomposto = texto.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposto.Length);

        foreach (var c in decomposto)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    public bool ListaVaziaPorBusca => Busca.Length > 0 && Itens.Count == 0;

    public string TextoDoFiltro => Busca.Length == 0
        ? string.Empty
        : $"{Itens.Count} de {_todos.Count} itens";

    /// <summary>
    /// Quantos estao marcados agora, independente da busca.
    ///
    /// Conta sobre <see cref="_todos"/>, nao sobre <see cref="Itens"/>: filtrar
    /// a lista nao pode desmarcar nada nem esconder da contagem o que vai ser
    /// aplicado.
    /// </summary>
    public int SelecionadosNoTotal => _todos.Count(i => i.Selecionado);

    public string TextoDaSelecao => SelecionadosNoTotal == 0
        ? "Nada marcado."
        : SelecionadosNoTotal == 1
            ? "1 item marcado."
            : $"{SelecionadosNoTotal} itens marcados.";

    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _resumo = string.Empty;
    [ObservableProperty] private string? _ganhoMedido;
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private int _progresso;
    [ObservableProperty] private bool _ehAdministrador;
    [ObservableProperty] private bool _dryRun;
    [ObservableProperty] private bool _jaVarreu;

    public bool PodeAgir => !Ocupado && EhAdministrador;

    public int Selecionados => SelecionadosNoTotal;

    public string TextoAplicar => Selecionados == 1
        ? "Aplicar 1 selecionado"
        : $"Aplicar {Selecionados} selecionados";

    public bool TemPendencias => _backup.Pendentes.Any(r => r.Modulo == _modulo.Id);

    /// <summary>Texto da confirmacao antes de aplicar. Cada modulo explica o seu.</summary>
    protected abstract string MontarConfirmacao(IReadOnlyList<ActionItemViewModel> selecionados);

    /// <summary>
    /// Varrer de novo sozinho depois de aplicar ou reverter.
    ///
    /// Ligado onde a varredura e barata — ler registro e servico custa
    /// milissegundos. Desligado onde ela demora: a lista de apps instalados
    /// leva quase um minuto, e revarrer sozinho deixaria a tela travada logo
    /// depois de uma acao.
    ///
    /// Quando esta desligado, a tela avisa que a lista pode estar
    /// desatualizada em vez de mostrar um estado antigo como se fosse o atual.
    /// </summary>
    protected virtual bool RevarrerAposAgir => false;

    [RelayCommand]
    public async Task VarrerAsync()
    {
        if (Ocupado)
            return;

        _cts = new CancellationTokenSource();
        Ocupado = true;
        Progresso = 0;
        GanhoMedido = null;

        foreach (var antigo in _todos)
            antigo.PropertyChanged -= AoMudarSelecao;

        _todos.Clear();
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
                _todos.Add(vm);
            }

            AplicarBusca();

            Resumo = resultado.Resumo;
            Status = resultado.Avisos.Count > 0
                ? string.Join("  ", resultado.Avisos)
                : "Varredura concluida. Revise a lista antes de confirmar.";
            JaVarreu = true;

            AposVarrer(resultado);
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

        var selecionados = _todos.Where(i => i.Selecionado && i.PodeSelecionar).ToList();

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

        await AtualizarListaAsync(DryRun);
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

        await AtualizarListaAsync(DryRun);
    }

    [RelayCommand]
    private void Cancelar() => _cts?.Cancel();

    [RelayCommand]
    private void MarcarTodos()
    {
        foreach (var item in _todos.Where(i => i.PodeSelecionar))
            item.Selecionado = true;
    }

    [RelayCommand]
    private void DesmarcarTodos()
    {
        foreach (var item in _todos)
            item.Selecionado = false;
    }

    protected virtual bool Confirmar(IReadOnlyList<ActionItemViewModel> selecionados)
    {
        var resposta = MessageBox.Show(
            MontarConfirmacao(selecionados),
            $"Confirmar - {Titulo}", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        return resposta == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Gancho para a pagina que tem conteudo proprio alem da lista. A pagina de
    /// Rede usa isto para preencher as tabelas de latencia e DNS, que nao cabem
    /// no formato de ActionItem.
    /// </summary>
    protected virtual void AposVarrer(ScanResult resultado)
    {
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

    /// <summary>
    /// Depois de agir, a lista mostra o estado de ANTES ate alguem varrer de
    /// novo. Isso fez o badge de um servico continuar dizendo "automatico"
    /// depois de ele ja ter virado "manual": a tela parecia nao ter feito nada.
    /// </summary>
    private async Task AtualizarListaAsync(bool foiDryRun)
    {
        if (foiDryRun || !JaVarreu)
            return;

        if (RevarrerAposAgir)
        {
            var buscaAnterior = Busca;
            await VarrerAsync();
            Busca = buscaAnterior;
            return;
        }

        Status = Status.Length == 0
            ? "A lista ainda mostra o estado de antes. Varra de novo para ver como ficou."
            : Status + "  A lista ainda mostra o estado de antes; varra de novo para ver como ficou.";
    }

    protected void NotificarSelecao()
    {
        OnPropertyChanged(nameof(Selecionados));
        OnPropertyChanged(nameof(SelecionadosNoTotal));
        OnPropertyChanged(nameof(TextoAplicar));
        OnPropertyChanged(nameof(TextoDaSelecao));
    }

    partial void OnOcupadoChanged(bool value) => OnPropertyChanged(nameof(PodeAgir));

    partial void OnEhAdministradorChanged(bool value) => OnPropertyChanged(nameof(PodeAgir));
}
