namespace GameBoost.Core.Modules.Uninstaller;

/// <summary>
/// Quem nunca pode ser desinstalado, e quem é apenas sugerido (seção 5.3).
/// Documentado em docs/PROTECAO.md.
/// </summary>
public static class UninstallerProtection
{
    /// <summary>
    /// Trechos de nome que tornam o app intocável. Remover qualquer um destes
    /// quebra outros programas, o Windows ou o próprio jogo.
    /// </summary>
    private static readonly (string Trecho, string Motivo)[] Intocaveis =
    {
        ("visual c++", "Outros programas dependem deste runtime para abrir."),
        ("vc_redist", "Outros programas dependem deste runtime para abrir."),
        ("visual studio", "Remova pelo instalador do Visual Studio, não por aqui."),
        (".net runtime", "Programas em .NET param de abrir sem ele."),
        (".net host", "Programas em .NET param de abrir sem ele."),
        (".net sdk", "Necessário para compilar. Remova pelo instalador oficial."),
        ("asp.net core", "Programas em .NET param de abrir sem ele."),
        ("windows desktop runtime", "Programas em .NET param de abrir sem ele."),
        ("directx", "Jogos param de abrir sem o DirectX."),
        ("microsoft edge", "O WebView2 depende dele, e muitos aplicativos usam o WebView2."),
        ("webview2", "Vários aplicativos usam o WebView2 para desenhar a interface."),
        ("xbox identity", "Jogos do Game Pass e anti-cheats da Microsoft dependem disso."),
        ("gaming services", "Jogos do Game Pass dependem disso."),
        ("nvidia", "Componente de driver de vídeo. Use o instalador da NVIDIA."),
        ("amd software", "Componente de driver de vídeo. Use o instalador da AMD."),
        ("radeon", "Componente de driver de vídeo. Use o instalador da AMD."),
        ("intel(r)", "Componente de driver ou de chipset."),
        ("realtek", "Driver de áudio ou de rede."),
        ("audio driver", "Driver de áudio."),
        ("chipset", "Driver de chipset."),
        ("windows sdk", "Ferramenta de desenvolvimento. Remova pelo instalador oficial."),
        ("update for windows", "Atualização do Windows."),
        ("security update", "Atualização de segurança do Windows."),
        ("microsoft store", "Componente do Windows."),
        ("app installer", "Componente do Windows."),
        ("windows app runtime", "Aplicativos modernos dependem dele.")
    };

    /// <summary>
    /// Antivírus: desinstalar pelo GameBoost deixaria a máquina desprotegida e
    /// costuma exigir a ferramenta do próprio fabricante.
    /// </summary>
    private static readonly string[] Seguranca =
    {
        "kaspersky", "avast", "avg ", "bitdefender", "mcafee", "norton", "eset",
        "sophos", "malwarebytes", "trend micro", "f-secure", "panda security",
        "crowdstrike", "sentinelone", "webroot", "carbon black"
    };

    /// <summary>
    /// Bloatware conhecido (seção 5.3). Aparece com sugestão, **nunca**
    /// pré-marcado: o que é bloat para um é ferramenta para outro.
    /// </summary>
    private static readonly string[] Bloatware =
    {
        "candy crush", "bubble witch", "march of empires", "royal revolt",
        "microsoft solitaire", "3d viewer", "mixed reality portal",
        "clipchamp", "linkedin", "booking.com", "disney", "netflix",
        "spotify music", "tiktok", "instagram", "facebook", "twitter",
        "feedback hub", "get help", "tips", "microsoft news", "microsoft to do",
        "mcafee livesafe", "norton security scan", "wildtangent",
        "hp wolf", "hp jumpstarts", "dell customer connect", "lenovo now",
        "cortana", "power automate", "microsoft family", "skype", "onenote for windows",
        "movie moments", "paint 3d", "print 3d", "your phone", "phone link"
    };

    /// <summary>
    /// Pacotes da Store que o Windows marca como não removíveis, ou que
    /// quebram a interface se saírem.
    /// </summary>
    private static readonly string[] PacotesDeSistema =
    {
        "microsoft.windows.shellexperiencehost", "microsoft.windows.startmenuexperiencehost",
        "microsoft.windows.search", "microsoft.ui.xaml", "microsoft.vclibs",
        "microsoft.net.native", "microsoft.windowsstore", "microsoft.desktopappinstaller",
        "microsoft.windows.immersivecontrolpanel", "microsoft.accountscontrol",
        "microsoft.windows.cloudexperiencehost", "microsoft.windows.contentdeliverymanager",
        "microsoft.xboxgamecallableui", "microsoft.xboxidentityprovider",
        "microsoft.gamingservices", "windows.immersivecontrolpanel",
        "microsoft.sechealthui", "microsoft.windowsappruntime"
    };

    /// <summary>Marca o app como protegido ou sugerido. Roda uma vez por app na varredura.</summary>
    public static void Avaliar(InstalledApp app)
    {
        var nome = app.Nome.ToLowerInvariant();
        var publisher = (app.Publisher ?? string.Empty).ToLowerInvariant();
        var pacote = (app.PacoteDaStore ?? string.Empty).ToLowerInvariant();

        foreach (var (trecho, motivo) in Intocaveis)
        {
            if (nome.Contains(trecho, StringComparison.Ordinal))
            {
                app.Protegido = true;
                app.MotivoDaProtecao = motivo;
                return;
            }
        }

        foreach (var av in Seguranca)
        {
            if (nome.Contains(av, StringComparison.Ordinal) || publisher.Contains(av, StringComparison.Ordinal))
            {
                app.Protegido = true;
                app.MotivoDaProtecao = "É o seu antivírus. Remover daqui deixaria a máquina "
                                     + "desprotegida; use a ferramenta do próprio fabricante.";
                return;
            }
        }

        if (app.Origem == AppOrigem.Store)
        {
            foreach (var sistema in PacotesDeSistema)
            {
                if (pacote.Contains(sistema, StringComparison.Ordinal))
                {
                    app.Protegido = true;
                    app.MotivoDaProtecao = "Componente do Windows. Remover quebra partes da interface.";
                    return;
                }
            }
        }

        // Sugerido não é o mesmo que marcado: o app entra na lista com a
        // sugestão visível e a caixa vazia (regra 3).
        app.Sugerido = Bloatware.Any(b => nome.Contains(b, StringComparison.Ordinal));
    }

    /// <summary>Para a documentação e os testes saberem o que está coberto.</summary>
    public static IReadOnlyList<string> TrechosIntocaveis
        => Intocaveis.Select(i => i.Trecho).ToList();

    public static IReadOnlyList<string> TrechosDeBloatware => Bloatware;
}
