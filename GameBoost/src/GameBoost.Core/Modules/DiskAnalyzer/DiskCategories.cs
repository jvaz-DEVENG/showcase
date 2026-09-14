namespace GameBoost.Core.Modules.DiskAnalyzer;

/// <summary>
/// Classifica arquivos em categorias que significam algo para o usuário
/// (seção 5.4). "12 GB de vídeo" diz mais que "12 GB em .mkv".
/// </summary>
public static class DiskCategories
{
    private static readonly IReadOnlySet<string> Video = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts" };

    private static readonly IReadOnlySet<string> Imagem = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".raw", ".cr2", ".nef", ".psd", ".heic" };

    private static readonly IReadOnlySet<string> Musica = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a", ".wma" };

    private static readonly IReadOnlySet<string> Instalador = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".exe", ".msi", ".iso", ".msix", ".appx" };

    private static readonly IReadOnlySet<string> Compactado = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".zip", ".rar", ".7z", ".tar", ".gz", ".xz" };

    private static readonly IReadOnlySet<string> Documento = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".odt", ".csv" };

    private static readonly IReadOnlySet<string> Codigo = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".go", ".rs", ".rb", ".php", ".sql" };

    private static readonly string[] PastasDeJogo =
    {
        @"\steamapps\", @"\epic games\", @"\riot games\", @"\battle.net\",
        @"\gog galaxy\games\", @"\gog games\", @"\xboxgames\", @"\ea games\",
        @"\origin games\", @"\ubisoft\", @"\my games\"
    };

    private static readonly string[] PastasDeCache =
    {
        @"\cache\", @"\cache2\", @"\temp\", @"\tmp\", @"\appdata\local\temp\",
        @"\code cache\", @"\gpucache\", @"\dxcache\", @"\glcache\", @"\d3dscache\",
        @"\shadercache\", @"\nv_cache\", @"\softwaredistribution\", @"\node_modules\",
        @"\.nuget\", @"\obj\", @"\bin\debug\", @"\bin\release\"
    };

    /// <summary>
    /// A pasta manda mais que a extensão: um .dll dentro de steamapps é parte
    /// do jogo, e um .exe dentro de um cache não é instalador guardado.
    /// </summary>
    public static DiskCategory Classificar(string caminho, long tamanho)
    {
        var minusculo = caminho.ToLowerInvariant().Replace('/', '\\');

        if (PastasDeJogo.Any(p => minusculo.Contains(p, StringComparison.Ordinal)))
            return DiskCategory.Jogos;

        if (PastasDeCache.Any(p => minusculo.Contains(p, StringComparison.Ordinal)))
            return DiskCategory.Caches;

        if (EhDeSistema(minusculo))
            return DiskCategory.Sistema;

        var extensao = Path.GetExtension(minusculo);

        if (Video.Contains(extensao))
            return DiskCategory.Videos;

        if (Imagem.Contains(extensao))
            return DiskCategory.Imagens;

        if (Musica.Contains(extensao))
            return DiskCategory.Musica;

        if (Documento.Contains(extensao))
            return DiskCategory.Documentos;

        if (Codigo.Contains(extensao))
            return DiskCategory.Codigo;

        // Instalador só conta como tal se for grande: um .exe de 200 KB é
        // programa, não pacote de instalação esquecido.
        if ((Instalador.Contains(extensao) || Compactado.Contains(extensao))
            && tamanho > 5L * 1024 * 1024)
        {
            return DiskCategory.Instaladores;
        }

        return DiskCategory.Outros;
    }

    private static bool EhDeSistema(string caminhoMinusculo)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();

        return caminhoMinusculo.StartsWith(windows, StringComparison.Ordinal)
            || caminhoMinusculo.Contains(@"\windows.old\", StringComparison.Ordinal)
            || caminhoMinusculo.Contains(@"\$windows.~", StringComparison.Ordinal);
    }

    public static string Nome(DiskCategory categoria) => categoria switch
    {
        DiskCategory.Jogos => "Jogos",
        DiskCategory.Videos => "Vídeos",
        DiskCategory.Imagens => "Imagens",
        DiskCategory.Instaladores => "Instaladores",
        DiskCategory.Caches => "Caches",
        DiskCategory.Sistema => "Sistema",
        DiskCategory.Documentos => "Documentos",
        DiskCategory.Codigo => "Código",
        DiskCategory.Musica => "Música",
        _ => "Outros"
    };

    /// <summary>Cor do treemap, em hexadecimal. Uma por categoria, estável.</summary>
    public static string Cor(DiskCategory categoria) => categoria switch
    {
        DiskCategory.Jogos => "#FF00C2A8",
        DiskCategory.Videos => "#FF9B6DFF",
        DiskCategory.Imagens => "#FF3FB950",
        DiskCategory.Instaladores => "#FFD29922",
        DiskCategory.Caches => "#FF6E7681",
        DiskCategory.Sistema => "#FF388BFD",
        DiskCategory.Documentos => "#FFDB6D28",
        DiskCategory.Codigo => "#FFF778BA",
        DiskCategory.Musica => "#FF56D4DD",
        _ => "#FF484F58"
    };
}
