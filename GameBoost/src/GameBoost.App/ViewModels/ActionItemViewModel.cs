using CommunityToolkit.Mvvm.ComponentModel;
using GameBoost.Core.Modules;

namespace GameBoost.App.ViewModels;

/// <summary>
/// Envolve um ActionItem com o estado de selecao da UI. A tela de lista e
/// generica: todo modulo produz ActionItem e reusa esta mesma view.
/// </summary>
public sealed partial class ActionItemViewModel : ObservableObject
{
    public ActionItemViewModel(ActionItem item)
    {
        Item = item;
        _selecionado = item.PreMarcado && !item.Bloqueado;
    }

    public ActionItem Item { get; }

    [ObservableProperty]
    private bool _selecionado;

    public string Id => Item.Id;
    public string Titulo => Item.Titulo;
    public string Descricao => Item.Descricao;
    public string Categoria => Item.Categoria;
    public RiskLevel Risco => Item.Risco;
    public string GanhoEstimado => Item.GanhoEstimado;
    public bool Bloqueado => Item.Bloqueado;
    public bool PodeSelecionar => !Item.Bloqueado;

    public string TextoDeRisco => Item.Risco switch
    {
        RiskLevel.Alto => "Risco alto",
        RiskLevel.Medio => "Risco medio",
        _ => "Risco baixo"
    };

    /// <summary>Conteudo do botao "?": o que faz, qual o risco, como desfazer.</summary>
    public string Explicacao =>
        $"O que faz: {Item.Descricao}\n\n" +
        $"Risco: {TextoDeRisco}\n\n" +
        $"Como desfazer: {Item.ComoDesfazer}" +
        (Item.MotivoBloqueio is null ? string.Empty : $"\n\nProtegido: {Item.MotivoBloqueio}");
}
