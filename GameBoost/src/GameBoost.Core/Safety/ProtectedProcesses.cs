namespace GameBoost.Core.Safety;

/// <summary>
/// Listas de processos. Documentadas em docs/PROTECAO.md.
/// Nomes sempre sem extensao e comparados sem diferenciar maiusculas.
/// </summary>
public static class ProtectedProcesses
{
    /// <summary>Nucleo do Windows. Encerrar qualquer um destes derruba a sessao ou o sistema.</summary>
    public static readonly IReadOnlySet<string> Criticos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "memory compression", "secure system",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "spoolsv", "dwm", "explorer", "fontdrvhost", "sihost",
        "taskhostw", "ctfmon", "runtimebroker", "shellexperiencehost",
        "startmenuexperiencehost", "searchhost", "searchindexer", "audiodg",
        "conhost", "dllhost", "wudfhost", "wmiprvse", "trustedinstaller",
        "tiworker", "logonui", "userinit", "dashost", "applicationframehost",
        "textinputhost", "systemsettings", "backgroundtaskhost"
    };

    /// <summary>
    /// Anti-cheats. Fechar qualquer um destes derruba o jogo ou gera ban.
    /// Regra dura: nunca aparecem sequer selecionaveis.
    /// </summary>
    public static readonly IReadOnlySet<string> AntiCheats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "easyanticheat", "easyanticheat_eos", "easyanticheat_setup",
        "beservice", "bedaisy",
        "vgc", "vgtray", "vgk",
        "faceitservice", "faceit",
        "pnkbstra", "pnkbstrb", "pnkbstrk",
        "xigncode", "xhunter1",
        "gameguard", "gamemon", "npggnt",
        "nprotect", "ricochet", "eaanticheat", "eaantichea",
        "battleye", "steamservice", "anticheatexpert", "ace-base", "ace-guard",
        "mhyprot", "sgguard"
    };

    /// <summary>
    /// Antivirus e EDR conhecidos. A deteccao real e por WMI (SecurityCenter2),
    /// esta lista e a rede de seguranca para quando o WMI falhar.
    /// </summary>
    public static readonly IReadOnlySet<string> Seguranca = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "msmpeng", "nissrv", "securityhealthservice", "securityhealthsystray",
        "mpdefendercoreservice", "smartscreen",
        "avp", "avpui", "kavfs", "ksde", "ksdeui", "kpm", "klnagent", "avpsus",
        "avastsvc", "avastui", "avgsvc", "avgui",
        "bdagent", "vsserv", "bdservicehost",
        "mcshield", "mfemms", "mfevtps",
        "ns", "nortonsecurity", "nsservice",
        "ekrn", "egui",
        "sophosui", "savservice", "sedservice",
        "cbservice", "csfalconservice", "csfalconcontainer",
        "sentinelagent", "sentinelctl", "sentinelstaticengine",
        "cylancesvc", "tmbmsrv", "tmccsf", "coreserviceshell",
        "wrsa", "webroot", "malwarebytes", "mbamservice", "mbamtray"
    };

    /// <summary>
    /// Launchers e ferramentas que o usuario pode querer fechar, mas que nunca
    /// vem pre-marcadas (regra 3). Aparecem na lista, desmarcadas.
    /// </summary>
    public static readonly IReadOnlySet<string> NuncaPreMarcados = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "steam", "steamwebhelper", "epicgameslauncher", "epicwebhelper",
        "battle.net", "agent", "riotclientservices", "riotclientux",
        "galaxyclient", "galaxyclienthelper", "gog galaxy",
        "eadesktop", "eabackgroundservice", "origin",
        "ubisoftconnect", "upc", "uplay",
        "xboxapp", "gamingservices", "xbox",
        "rtss", "rivatuner", "msiafterburner",
        "obs64", "obs32", "obs", "streamlabs obs", "xsplit.core",
        "nvidia share", "nvcontainer", "radeonsoftware", "amddvr",
        // IDEs, editores e assistentes de desenvolvimento. Costumam ter trabalho
        // nao salvo aberto, entao nunca vem marcados (regra 3).
        "devenv", "code", "code - insiders", "vscodium", "cursor", "windsurf",
        "antigravity", "claude", "chatgpt", "zed", "sublime_text", "notepad++",
        "rider64", "idea64", "pycharm64", "webstorm64", "clion64", "goland64",
        "phpstorm64", "datagrip64", "android studio", "studio64", "eclipse",
        "atom", "brackets", "godot", "unity", "unityhub", "ue4editor", "ue5editor",
        "blender", "obsidian", "notion", "figma",
        "photoshop", "illustrator", "premiere pro", "afterfx",
        "excel", "winword", "powerpnt", "outlook",
        "vmware", "vmware-vmx", "virtualbox", "vboxheadless", "docker desktop",

        // Sincronizacao de nuvem: encerrar no meio de um envio deixa arquivo
        // pela metade na nuvem. O spec (secao 5.6) manda avisar, nunca marcar.
        "onedrive", "onedrive.sync.service", "filecoauth", "dropbox",
        "googledrivefs", "megasync", "syncthing", "nextcloud"
    };

    /// <summary>
    /// Instaladores e atualizadores em execucao. Encerrar um destes no meio do
    /// trabalho pode deixar a instalacao corrompida, entao nunca vem marcado.
    /// Detectado por padrao de nome, nao por lista: instalador novo aparece
    /// toda semana.
    /// </summary>
    public static bool ParecerInstalador(string processName)
    {
        var nome = Normalizar(processName);

        string[] marcadores =
        {
            "setup", "install", "updater", "update", "upgrade",
            "patch", "msiexec", "redist", "unins"
        };

        if (marcadores.Any(m => nome.Contains(m, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Instalador extraido para a pasta temporaria mantem o .tmp no nome.
        return nome.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalizar(string processName)
    {
        var nome = processName.Trim();
        if (nome.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            nome = nome[..^4];
        return nome;
    }
}
