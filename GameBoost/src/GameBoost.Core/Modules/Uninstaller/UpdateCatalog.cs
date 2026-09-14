namespace GameBoost.Core.Modules.Uninstaller;

/// <summary>
/// Como cada atualização deve ser tratada (seção 5.3, aba "Atualizações").
/// </summary>
public enum ClasseDeAtualizacao
{
    /// <summary>Atualização comum: o winget baixa, instala e pronto.</summary>
    Normal,

    /// <summary>
    /// O app tem atualizador próprio e se vira sozinho. Forçar pelo winget pode
    /// brigar com o atualizador dele.
    /// </summary>
    AtualizaSozinho,

    /// <summary>Runtime ou componente do qual outros programas dependem.</summary>
    Runtime,

    /// <summary>Driver. O GameBoost nunca instala driver.</summary>
    Driver,

    /// <summary>O winget não sabe a versão instalada e pode reinstalar à toa.</summary>
    Incerta
}

/// <summary>
/// Classificação das atualizações do winget.
///
/// A lista de "atualiza sozinho" é a parte que mais importa. Steam, Discord e
/// navegador se atualizam em segundo plano e, se o winget instalar por cima, o
/// resultado pode ser duas versões brigando ou uma instalação quebrada. O spec
/// manda mostrá-los com a tag e explicar; aqui a tag também carrega o porquê.
/// </summary>
public static class UpdateCatalog
{
    /// <summary>
    /// Ids do winget de apps com atualizador próprio. A comparação é por
    /// prefixo do id, que é estável: "Valve.Steam", "Mozilla.Firefox".
    /// </summary>
    private static readonly string[] AtualizamSozinhos =
    {
        // Launchers de jogo
        "Valve.Steam", "EpicGames.EpicGamesLauncher", "XP99VR1BPSBQJ2",
        "Blizzard.BattleNet", "ElectronicArts.EADesktop", "Ubisoft.Connect",
        "GOG.Galaxy", "RiotGames.", "Nvidia.GeForceExperience", "Nvidia.GeForceNow",
        "Nvidia.NvidiaApp", "Amazon.Games", "Rockstar.RockstarGamesLauncher",

        // Navegadores
        "Google.Chrome", "Google.ChromeRemoteDesktopHost", "Mozilla.Firefox",
        "Microsoft.Edge", "Opera.Opera", "Brave.Brave", "Vivaldi.Vivaldi",

        // Comunicação e mídia
        "Discord.Discord", "Spotify.Spotify", "Zoom.Zoom", "SlackTechnologies.Slack",
        "Telegram.TelegramDesktop", "WhatsApp.WhatsApp",

        // Ferramentas com auto-update agressivo
        "Obsidian.Obsidian", "Docker.DockerDesktop", "Microsoft.VisualStudioCode",
        "Google.Antigravity", "Anysphere.Cursor", "Notion.Notion", "Figma.Figma",
        "Unity.UnityHub", "JetBrains.Toolbox", "Postman.Postman"
    };

    /// <summary>Runtimes e componentes dos quais outros programas dependem.</summary>
    private static readonly string[] Runtimes =
    {
        "Microsoft.VCRedist", "Microsoft.DotNet", "Microsoft.WindowsDesktopRuntime",
        "Microsoft.AspNetCore", "Oracle.JavaRuntimeEnvironment", "Oracle.JDK",
        "EclipseAdoptium.Temurin", "Microsoft.OpenJDK", "Microsoft.DirectX",
        "Microsoft.EdgeWebView", "Microsoft.AppInstaller", "Microsoft.WSL",
        "Microsoft.VCLibs", "Python.Launcher"
    };

    private static readonly string[] Drivers =
    {
        "Nvidia.GeForceDriver", "AMD.", "Intel.", "Realtek.", "Nefarius.",
        "Logitech.", "Razer.", "Corsair.", "SteelSeries."
    };

    /// <summary>
    /// Apps cuja desatualização é problema de **segurança**, não de conforto.
    /// São os que abrem arquivo vindo da internet: navegador, compactador,
    /// leitor de PDF, runtime de Java.
    ///
    /// Esta lista vira o Finding do Diagnóstico ("N desatualizados, M de
    /// segurança"), e é o motivo de a aba existir. O resto é higiene.
    /// </summary>
    private static readonly string[] Seguranca =
    {
        "Google.Chrome", "Mozilla.Firefox", "Microsoft.Edge", "Opera.Opera",
        "Brave.Brave", "Vivaldi.Vivaldi",
        "7zip.7zip", "RARLab.WinRAR", "PeaZip.PeaZip",
        "Oracle.JavaRuntimeEnvironment", "Oracle.JDK", "EclipseAdoptium.Temurin",
        "Adobe.Acrobat.Reader", "SumatraPDF.SumatraPDF", "Foxit.FoxitReader",
        "VideoLAN.VLC", "OpenSSL.", "Git.Git", "OpenVPNTechnologies.OpenVPN",
        "PuTTY.PuTTY", "WinSCP.WinSCP", "Mozilla.Thunderbird"
    };

    public static ClasseDeAtualizacao Classificar(AtualizacaoDisponivel a)
    {
        if (Bate(a.Id, Drivers))
            return ClasseDeAtualizacao.Driver;

        if (Bate(a.Id, Runtimes))
            return ClasseDeAtualizacao.Runtime;

        if (Bate(a.Id, AtualizamSozinhos))
            return ClasseDeAtualizacao.AtualizaSozinho;

        // A incerteza vem por último: um runtime com versão incerta continua
        // sendo runtime, e é esse o aviso que importa.
        return a.VersaoIncerta ? ClasseDeAtualizacao.Incerta : ClasseDeAtualizacao.Normal;
    }

    public static bool EhDeSeguranca(AtualizacaoDisponivel a) => Bate(a.Id, Seguranca);

    /// <summary>
    /// Casa o id do winget com um padrao da lista.
    ///
    /// Prefixo cru nao serve: "Google.Chrome" casaria com
    /// "Google.ChromeRemoteDesktopHost", e o host de acesso remoto apareceria
    /// classificado como navegador e como atualizacao de seguranca. O id do
    /// winget e hierarquico e separado por ponto, entao o casamento exige
    /// **fronteira**: ou o id e igual ao padrao, ou continua com um ponto.
    ///
    /// Padrao terminado em ponto ("AMD.", "Intel.", "RiotGames.") continua
    /// sendo prefixo de familia inteira, e isso e proposital.
    /// </summary>
    private static bool Bate(string id, string[] lista)
    {
        foreach (var padrao in lista)
        {
            if (padrao.EndsWith('.'))
            {
                if (id.StartsWith(padrao, StringComparison.OrdinalIgnoreCase))
                    return true;

                continue;
            }

            if (id.Equals(padrao, StringComparison.OrdinalIgnoreCase))
                return true;

            if (id.Length > padrao.Length
                && id[padrao.Length] == '.'
                && id.StartsWith(padrao, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string Categoria(ClasseDeAtualizacao classe) => classe switch
    {
        ClasseDeAtualizacao.AtualizaSozinho => "Se atualizam sozinhos",
        ClasseDeAtualizacao.Runtime => "Runtimes e componentes",
        ClasseDeAtualizacao.Driver => "Drivers",
        ClasseDeAtualizacao.Incerta => "O winget não sabe a versão instalada",
        _ => "Atualizações"
    };

    /// <summary>Texto que explica, em cada caso, por que tratar diferente.</summary>
    public static string Explicacao(ClasseDeAtualizacao classe) => classe switch
    {
        ClasseDeAtualizacao.AtualizaSozinho =>
            "Este app se atualiza sozinho. Forçar pelo winget pode conflitar com o "
          + "atualizador dele e, em alguns casos, instalar uma segunda cópia. O normal é "
          + "deixar quieto: ele se resolve na próxima vez que abrir.",

        ClasseDeAtualizacao.Runtime =>
            "Componente do qual outros programas dependem. Atualizar costuma ser seguro, "
          + "mas se algum programa seu depender da versão antiga, ele pode parar de abrir. "
          + "Não marque junto com um lote grande: se der problema, você não vai saber qual "
          + "foi.",

        ClasseDeAtualizacao.Driver =>
            "Isto é driver. O GameBoost não instala driver, nem pelo winget: driver errado "
          + "deixa a máquina sem vídeo, sem som ou sem rede, e o desfazer é pelo Modo de "
          + "Segurança. Baixe pela página do fabricante.",

        ClasseDeAtualizacao.Incerta =>
            "O winget não conseguiu ler a versão instalada, então ele não sabe se há mesmo "
          + "atualização — só que a versão do repositório é diferente. Atualizar aqui pode "
          + "significar reinstalar o que você já tem.",

        _ => string.Empty
    };
}
