namespace GameBoost.App.ViewModels;

/// <summary>
/// Pagina de modulo que ainda nao existe. Mostra o que vai fazer e em qual fase
/// chega, em vez de sumir do menu: o usuario ve o mapa inteiro do produto.
/// </summary>
public sealed class PlaceholderPageViewModel : PageViewModelBase
{
    public PlaceholderPageViewModel(
        string nome,
        string titulo,
        string subtitulo,
        string icone,
        int fase,
        string secaoDoSpec,
        IReadOnlyList<string> oQueVaiFazer)
    {
        Nome = nome;
        Titulo = titulo;
        Subtitulo = subtitulo;
        Icone = icone;
        Fase = fase;
        SecaoDoSpec = secaoDoSpec;
        OQueVaiFazer = oQueVaiFazer;
    }

    public override string Nome { get; }
    public override string Titulo { get; }
    public override string Subtitulo { get; }
    public override string Icone { get; }
    public override bool Implementada => false;

    public int Fase { get; }
    public string SecaoDoSpec { get; }
    public IReadOnlyList<string> OQueVaiFazer { get; }

    public string AvisoDeFase => $"Disponivel na Fase {Fase}";
    public string Referencia => $"Especificado na secao {SecaoDoSpec} do GAMEBOOST_V2_SPEC.md";
}
