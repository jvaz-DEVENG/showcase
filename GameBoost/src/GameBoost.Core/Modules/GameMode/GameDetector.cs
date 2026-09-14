using GameBoost.Core.Abstractions;

namespace GameBoost.Core.Modules.GameMode;

public sealed record DetectedGame(ProcessInfo Process, string Motivo, int Confianca);

/// <summary>
/// Descobre qual processo e o jogo. Ordem de confianca: pasta de launcher,
/// nome conhecido, consumo de memoria com janela visivel.
/// </summary>
public sealed class GameDetector
{
    private static readonly string[] PastasDeJogo =
    {
        @"\steamapps\common\", @"\epic games\", @"\riot games\",
        @"\battle.net\", @"\gog galaxy\games\", @"\gog games\",
        @"\xboxgames\", @"\ea games\", @"\origin games\",
        @"\ubisoft\ubisoft game launcher\games\", @"\games\"
    };

    private static readonly IReadOnlySet<string> JogosConhecidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "valorant", "valorant-win64-shipping",
        "cs2", "csgo", "dota2",
        "leagueoflegends", "league of legends",
        "cyberpunk2077", "witcher3",
        "fortniteclient-win64-shipping",
        "r5apex", "apex",
        "gta5", "gtav", "rdr2",
        "eldenring", "sekiro", "darksouls3",
        "hd2", "helldivers2",
        "overwatch", "hearthstone", "wow", "wowclassic", "diablo iv", "diabloiv",
        "rocketleague", "minecraft", "javaw",
        "pubg", "tslgame", "rust", "rustclient",
        "destiny2", "warframe", "bf2042", "bf1", "codmw", "cod",
        "starfield", "baldursgate3", "bg3", "bg3_dx11",
        "palworld-win64-shipping", "marvelrivals", "deltaforceclient"
    };

    public DetectedGame? Detectar(IEnumerable<ProcessInfo> processos)
    {
        DetectedGame? melhor = null;

        foreach (var p in processos)
        {
            var candidato = Avaliar(p);
            if (candidato is null)
                continue;

            if (melhor is null || candidato.Confianca > melhor.Confianca)
                melhor = candidato;
        }

        return melhor;
    }

    private static DetectedGame? Avaliar(ProcessInfo p)
    {
        var nome = Safety.ProtectedProcesses.Normalizar(p.Name);

        if (JogosConhecidos.Contains(nome))
            return new DetectedGame(p, "nome de jogo conhecido", 100);

        if (!string.IsNullOrEmpty(p.ExecutablePath))
        {
            var caminho = p.ExecutablePath.Replace('/', '\\');
            if (PastasDeJogo.Any(pasta => caminho.Contains(pasta, StringComparison.OrdinalIgnoreCase)))
                return new DetectedGame(p, "executavel dentro de uma pasta de launcher", 80);
        }

        // Heuristica fraca: processo pesado com janela propria. So vale se nada
        // melhor aparecer, e por isso a confianca baixa.
        if (p.WorkingSetBytes > 1_500L * 1024 * 1024 && !string.IsNullOrWhiteSpace(p.WindowTitle))
            return new DetectedGame(p, "processo pesado com janela em primeiro plano", 30);

        return null;
    }
}
