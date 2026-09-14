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

    /// <summary>
    /// Deteccao barata, so por nome conhecido. Existe para o monitor de
    /// gargalos, que roda a cada segundo e nao pode pagar a varredura completa:
    /// aquela consulta o WMI e enumera janelas, o que custaria mais que toda a
    /// coleta de metricas junta.
    /// </summary>
    public string? DetectarPorNome(IEnumerable<string> nomesDeProcesso)
    {
        foreach (var nome in nomesDeProcesso)
        {
            var normalizado = Safety.ProtectedProcesses.Normalizar(nome);

            if (!NaoSaoJogos.Contains(normalizado) && JogosConhecidos.Contains(normalizado))
                return nome;
        }

        return null;
    }

    /// <summary>
    /// Instalado pela Steam, mas nao e jogo. Sem esta lista, o Wallpaper Engine
    /// vira "o jogo" so por morar em steamapps\common, e o Modo Game acaba
    /// subindo a prioridade do papel de parede em vez da do jogo de verdade.
    /// Foi exatamente o que aconteceu no primeiro teste com privilegios.
    /// </summary>
    private static readonly IReadOnlySet<string> NaoSaoJogos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "wallpaper64", "wallpaper32", "wallpaperservice64", "webwallpaper32",
        "vrmonitor", "vrserver", "vrcompositor", "vrdashboard", "steamvr",
        "aseprite", "blender", "krita", "obs64", "3dsmax", "unity", "unityhub",
        "rpcs3", "pcsx2", "dolphin", "retroarch",
        "steamwebhelper", "gameoverlayui", "steamerrorreporter",

        // Atualizadores e serviços que moram DENTRO da pasta do launcher.
        //
        // O "Agent" do Battle.net vive em ProgramData\\Battle.net\\Agent e foi
        // anunciado como jogo numa sessão real: ele engorda enquanto baixa
        // atualização, passou do limite de memória, e a pasta bate com a lista
        // de launchers. Nome genérico dentro de pasta de launcher é quase
        // sempre infraestrutura, não jogo.
        "agent", "battle.net helper", "blizzard error handler",
        "epicgameslauncher", "epicwebhelper", "unrealcefsubprocess",
        "eabackgroundservice", "eadesktop", "eacrashreporter",
        "upc", "uplaywebcore", "ubisoftgamelauncher", "ubisoftconnect",
        "riotclientservices", "riotclientux", "riotclientcrashhandler",
        "galaxyclient", "galaxycommunication", "goggalaxynotifications",
        "launcher", "updater", "update", "crashhandler", "crashreporter",
        "bootstrapper", "installer", "setup", "helper", "service"
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

    /// <summary>
    /// Nome que descreve função, não produto.
    ///
    /// Jogo tem nome próprio. "Agent", "Launcher" e "Updater" são o que o
    /// programa faz, e dentro de uma pasta de launcher é sempre a
    /// infraestrutura dele.
    /// </summary>
    private static bool NomeGenerico(string nome)
    {
        string[] genericos =
        {
            "agent", "launcher", "updater", "update", "helper", "service",
            "host", "daemon", "bootstrap", "bootstrapper", "crashhandler",
            "crashreporter", "installer", "setup", "client", "overlay"
        };

        return genericos.Contains(nome, StringComparer.OrdinalIgnoreCase);
    }

    private static DetectedGame? Avaliar(ProcessInfo p)
    {
        var nome = Safety.ProtectedProcesses.Normalizar(p.Name);

        if (NaoSaoJogos.Contains(nome))
            return null;

        if (JogosConhecidos.Contains(nome))
            return new DetectedGame(p, "nome de jogo conhecido", 100);

        if (!string.IsNullOrEmpty(p.ExecutablePath))
        {
            var caminho = p.ExecutablePath.Replace('/', '\\');
            if (PastasDeJogo.Any(pasta => caminho.Contains(pasta, StringComparison.OrdinalIgnoreCase)))
            {
                // Pasta de launcher tem jogo e tem a maquinaria do launcher.
                // Um nome que so descreve funcao ("agent", "launcher",
                // "updater") descreve a maquinaria.
                if (NomeGenerico(nome))
                    return null;

                // Estar na pasta do launcher nao basta: utilitarios instalados
                // pela Steam moram no mesmo lugar. Jogo em execucao ocupa
                // memoria de jogo.
                if (p.WorkingSetBytes < 300L * 1024 * 1024)
                    return null;

                return new DetectedGame(p, "executavel dentro de uma pasta de launcher", 80);
            }
        }

        // Heuristica fraca: processo pesado com janela propria. So vale se nada
        // melhor aparecer, e por isso a confianca baixa.
        if (p.WorkingSetBytes > 1_500L * 1024 * 1024 && !string.IsNullOrWhiteSpace(p.WindowTitle))
            return new DetectedGame(p, "processo pesado com janela em primeiro plano", 30);

        return null;
    }
}
