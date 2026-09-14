namespace GameBoost.Core.Safety;

/// <summary>Caminhos que nenhum modulo pode apagar ou mover (secao 9 do spec).</summary>
public static class ProtectedPaths
{
    /// <summary>Pastas pessoais: conteudo do usuario nunca e alvo automatico.</summary>
    public static IReadOnlyList<string> PastasDoUsuario() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.Favorites)
    }.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

    /// <summary>Pastas de sistema: so alcancaveis pela whitelist explicita de cada modulo.</summary>
    public static IReadOnlyList<string> PastasDeSistema() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        Environment.GetFolderPath(Environment.SpecialFolder.SystemX86)
    }.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

    /// <summary>
    /// Segmentos que, aparecendo em qualquer nivel do caminho, tornam a pasta intocavel.
    /// Protege saves, sincronizacao de nuvem e repositorios de codigo.
    /// </summary>
    public static readonly IReadOnlyList<string> SegmentosProibidos = new[]
    {
        ".git", "onedrive", "dropbox", "google drive", "icloud",
        "saves", "savegames", "saved games", "savedgames", "my games"
    };
}
