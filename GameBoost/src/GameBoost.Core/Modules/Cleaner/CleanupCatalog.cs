namespace GameBoost.Core.Modules.Cleaner;

/// <summary>
/// A whitelist de limpeza da seção 5.2, e só ela.
///
/// O Cleaner não monta caminho por conta própria em lugar nenhum: tudo que ele
/// pode tocar está escrito aqui, e ainda assim cada arquivo passa pelo
/// SafetyGuard antes de ser removido. Duas camadas, de propósito.
/// </summary>
public static class CleanupCatalog
{
    private static string? Env(string variavel)
    {
        var valor = Environment.GetEnvironmentVariable(variavel);
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
    }

    private static string? Pasta(Environment.SpecialFolder pasta)
    {
        var valor = Environment.GetFolderPath(pasta);
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
    }

    private static string? Combinar(string? raiz, params string[] partes)
        => raiz is null ? null : Path.Combine(new[] { raiz }.Concat(partes).ToArray());

    public static IReadOnlyList<CleanupTarget> Todos() => new[]
    {
        // ---------------- Sistema ----------------

        new CleanupTarget
        {
            Id = "temp-usuario",
            Categoria = "Sistema",
            Titulo = "Arquivos temporários do usuário",
            Descricao = "Sobras que programas deixam em %TEMP% e nunca apagam.",
            Raiz = () => Path.GetTempPath(),
            IdadeMinima = TimeSpan.FromHours(24),
            PreMarcadoPadrao = true,
            ComoDesfazer = "Não há: são arquivos temporários, recriados sob demanda. "
                         + "Por isso só entram os que estão parados há mais de 24 horas."
        },

        new CleanupTarget
        {
            Id = "temp-sistema",
            Categoria = "Sistema",
            Titulo = "Arquivos temporários do Windows",
            Descricao = "O mesmo, na pasta temporária do sistema.",
            Raiz = () => Combinar(Pasta(Environment.SpecialFolder.Windows), "Temp"),
            IdadeMinima = TimeSpan.FromHours(24),
            PreMarcadoPadrao = true,
            ComoDesfazer = "Não há: são temporários, recriados sob demanda."
        },

        new CleanupTarget
        {
            Id = "windows-update-cache",
            Categoria = "Sistema",
            Titulo = "Cache do Windows Update",
            Descricao = "Instaladores de atualizações já aplicadas.",
            Raiz = () => Combinar(Pasta(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download"),
            PararServico = "wuauserv",
            PreMarcadoPadrao = true,
            ComoDesfazer = "O Windows baixa de novo se precisar. O serviço de atualização é "
                         + "parado durante a limpeza e religado logo depois."
        },

        new CleanupTarget
        {
            Id = "delivery-optimization",
            Categoria = "Sistema",
            Titulo = "Cache de Otimização de Entrega",
            Descricao = "Pedaços de atualização que o Windows guarda para compartilhar na rede.",
            Raiz = () => Combinar(Pasta(Environment.SpecialFolder.Windows),
                "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows",
                "DeliveryOptimization", "Cache"),
            PreMarcadoPadrao = true,
            ComoDesfazer = "Recriado sozinho na próxima atualização."
        },

        new CleanupTarget
        {
            Id = "relatorios-de-erro",
            Categoria = "Sistema",
            Titulo = "Relatórios de erro do Windows",
            Descricao = "Relatórios de travamentos que ficaram na fila de envio.",
            Raiz = () => Combinar(Pasta(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows", "WER", "ReportQueue"),
            PreMarcadoPadrao = true,
            ComoDesfazer = "Não há. São relatórios de erros passados, sem uso para você."
        },

        new CleanupTarget
        {
            Id = "dumps-de-memoria",
            Categoria = "Sistema",
            Titulo = "Despejos de memória de telas azuis",
            Descricao = "Arquivos grandes gravados quando o Windows trava.",
            Raiz = () => Combinar(Pasta(Environment.SpecialFolder.Windows), "Minidump"),
            PreMarcadoPadrao = true,
            ComoDesfazer = "Não há.",
            Advertencia = "Só apague depois de investigar a tela azul: é o que um técnico leria."
        },

        new CleanupTarget
        {
            Id = "prefetch",
            Categoria = "Sistema",
            Titulo = "Prefetch antigo",
            Descricao = "Dados que o Windows usa para abrir programas mais rápido.",
            Raiz = () => Combinar(Pasta(Environment.SpecialFolder.Windows), "Prefetch"),
            Padrao = "*.pf",
            Recursivo = false,
            IdadeMinima = TimeSpan.FromDays(30),
            Risco = RiskLevel.Medio,
            PreMarcadoPadrao = false,
            ComoDesfazer = "O Windows recria em alguns dias de uso.",
            Advertencia = "Os programas abrem um pouco mais devagar até o Windows reaprender. "
                        + "Ganha pouco espaço: raramente compensa."
        },

        new CleanupTarget
        {
            Id = "miniaturas",
            Categoria = "Sistema",
            Titulo = "Cache de miniaturas e ícones",
            Descricao = "Miniaturas de fotos e vídeos que o Explorer guarda.",
            Raiz = () => Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Explorer"),
            Padrao = "*cache_*.db",
            Recursivo = false,
            Risco = RiskLevel.Medio,
            PreMarcadoPadrao = false,
            ComoDesfazer = "Recriado ao abrir as pastas de novo.",
            Advertencia = "As pastas com muitas fotos ficam lentas na primeira vez que abrirem."
        },

        // ---------------- Navegadores ----------------

        new CleanupTarget
        {
            Id = "cache-chrome",
            Categoria = "Navegadores",
            Titulo = "Cache do Chrome",
            Descricao = "Imagens e scripts de sites já visitados. Não inclui senhas, cookies nem histórico.",
            Raiz = () => Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "User Data", "Default", "Cache"),
            ExigeFechado = new[] { "chrome" },
            PreMarcadoPadrao = true,
            ComoDesfazer = "Os sites recarregam os arquivos na próxima visita. "
                         + "Você continua logado: senhas, cookies e histórico não são tocados."
        },

        new CleanupTarget
        {
            Id = "cache-edge",
            Categoria = "Navegadores",
            Titulo = "Cache do Edge",
            Descricao = "Imagens e scripts de sites já visitados. Não inclui senhas, cookies nem histórico.",
            Raiz = () => Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "User Data", "Default", "Cache"),
            ExigeFechado = new[] { "msedge" },
            PreMarcadoPadrao = true,
            ComoDesfazer = "Os sites recarregam os arquivos na próxima visita."
        },

        new CleanupTarget
        {
            Id = "cache-brave",
            Categoria = "Navegadores",
            Titulo = "Cache do Brave",
            Descricao = "Imagens e scripts de sites já visitados.",
            Raiz = () => Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData),
                "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache"),
            ExigeFechado = new[] { "brave" },
            PreMarcadoPadrao = true,
            ComoDesfazer = "Os sites recarregam os arquivos na próxima visita."
        },

        new CleanupTarget
        {
            Id = "cache-opera",
            Categoria = "Navegadores",
            Titulo = "Cache do Opera",
            Descricao = "Imagens e scripts de sites já visitados.",
            Raiz = () => Combinar(Env("APPDATA"), "Opera Software", "Opera Stable", "Cache"),
            ExigeFechado = new[] { "opera" },
            PreMarcadoPadrao = true,
            ComoDesfazer = "Os sites recarregam os arquivos na próxima visita."
        },

        new CleanupTarget
        {
            Id = "cache-firefox",
            Categoria = "Navegadores",
            Titulo = "Cache do Firefox",
            Descricao = "Imagens e scripts de sites já visitados.",
            Raiz = () => Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData),
                "Mozilla", "Firefox", "Profiles"),
            Padrao = "*",
            ExigeFechado = new[] { "firefox" },
            PreMarcadoPadrao = false,
            ComoDesfazer = "Os sites recarregam os arquivos na próxima visita.",
            Advertencia = "O Firefox guarda o cache junto do perfil; por isso este item entra desmarcado."
        },

        // ---------------- Aplicativos ----------------

        new CleanupTarget
        {
            Id = "cache-discord",
            Categoria = "Aplicativos",
            Titulo = "Cache do Discord",
            Descricao = "Imagens e anexos de conversas já vistos.",
            Raiz = () => Combinar(Env("APPDATA"), "discord", "Cache"),
            ExigeFechado = new[] { "discord" },
            PreMarcadoPadrao = true,
            ComoDesfazer = "O Discord baixa de novo o que precisar. Não desloga nem apaga conversa."
        },

        new CleanupTarget
        {
            Id = "cache-spotify",
            Categoria = "Aplicativos",
            Titulo = "Cache do Spotify",
            Descricao = "Músicas guardadas para tocar sem baixar de novo.",
            Raiz = () => Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData), "Spotify", "Storage"),
            ExigeFechado = new[] { "spotify" },
            Risco = RiskLevel.Medio,
            PreMarcadoPadrao = false,
            ComoDesfazer = "O Spotify baixa de novo ao tocar.",
            Advertencia = "Músicas salvas para ouvir offline precisam ser baixadas outra vez."
        },

        // ---------------- Jogos ----------------

        new CleanupTarget
        {
            Id = "steam-logs",
            Categoria = "Jogos",
            Titulo = "Logs e despejos da Steam",
            Descricao = "Registros de diagnóstico que a Steam acumula.",
            Raiz = () => PastaDaSteam("logs"),
            PreMarcadoPadrao = true,
            ComoDesfazer = "Não há. São registros de diagnóstico, recriados sozinhos."
        },

        new CleanupTarget
        {
            Id = "steam-httpcache",
            Categoria = "Jogos",
            Titulo = "Cache da loja da Steam",
            Descricao = "Imagens e páginas da loja.",
            Raiz = () => PastaDaSteam("appcache", "httpcache"),
            PreMarcadoPadrao = true,
            ComoDesfazer = "A loja recarrega as imagens sozinha."
        },

        new CleanupTarget
        {
            Id = "shader-nvidia",
            Categoria = "Jogos",
            Titulo = "Cache de shaders da NVIDIA",
            Descricao = "Efeitos gráficos que os jogos pré-compilaram para não recompilar depois.",
            Raiz = () => Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData), "NVIDIA", "DXCache"),
            Risco = RiskLevel.Medio,
            PreMarcadoPadrao = false,
            ComoDesfazer = "Os jogos recriam ao rodar.",
            Advertencia = "Os jogos vão engasgar nas primeiras horas, recompilando os efeitos. "
                        + "Só vale a pena quando falta espaço de verdade."
        },

        new CleanupTarget
        {
            Id = "shader-directx",
            Categoria = "Jogos",
            Titulo = "Cache de shaders do DirectX",
            Descricao = "O mesmo, do lado do Windows.",
            Raiz = () => Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData), "D3DSCache"),
            Risco = RiskLevel.Medio,
            PreMarcadoPadrao = false,
            ComoDesfazer = "Os jogos recriam ao rodar.",
            Advertencia = "Os jogos vão engasgar nas primeiras horas, recompilando os efeitos."
        },

        // ---------------- Só sugestão, nunca acionável ----------------

        new CleanupTarget
        {
            Id = "instaladores-esquecidos",
            Categoria = "Sugestões",
            Titulo = "Instaladores antigos em Downloads",
            Descricao = "Arquivos de instalação parados há mais de 30 dias.",
            Raiz = () => Combinar(Env("USERPROFILE"), "Downloads"),
            Padrao = "*.exe",
            Recursivo = false,
            IdadeMinima = TimeSpan.FromDays(30),
            Risco = RiskLevel.Alto,
            Modo = RemocaoModo.Lixeira,
            PreMarcadoPadrao = false,
            ApenasSugestao = true,
            ComoDesfazer = "O GameBoost não apaga nada aqui: só mostra o que encontrou, "
                         + "para você decidir e apagar à mão.",
            Advertencia = "Downloads é pasta sua. O GameBoost nunca remove arquivo daqui."
        }
    };

    /// <summary>
    /// Descobre a pasta da Steam pelo registro. Sem isso seria preciso chutar
    /// o disco de instalação, e chutar caminho é exatamente o que a regra 2 proíbe.
    /// </summary>
    private static string? PastaDaSteam(params string[] subpastas)
    {
        try
        {
            using var chave = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var caminho = chave?.GetValue("SteamPath") as string;

            if (string.IsNullOrWhiteSpace(caminho))
                return null;

            return Path.Combine(new[] { caminho.Replace('/', '\\') }.Concat(subpastas).ToArray());
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Raízes onde a remoção é permitida. É a whitelist que o SafetyGuard
    /// recebe na hora de apagar: nada fora daqui sai do disco, mesmo que um
    /// alvo do catálogo aponte para lá.
    ///
    /// Downloads NÃO está aqui de propósito. É pasta do usuário: o GameBoost
    /// pode olhar (ver <see cref="RaizesParaMedir"/>) e listar o que achou, mas
    /// nunca remover.
    /// </summary>
    public static IReadOnlyList<string> RaizesPermitidas()
    {
        var raizes = new List<string?>
        {
            Path.GetTempPath(),
            Combinar(Pasta(Environment.SpecialFolder.Windows), "Temp"),
            Combinar(Pasta(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download"),
            Combinar(Pasta(Environment.SpecialFolder.Windows), "ServiceProfiles"),
            Combinar(Pasta(Environment.SpecialFolder.Windows), "Minidump"),
            Combinar(Pasta(Environment.SpecialFolder.Windows), "Prefetch"),
            Combinar(Pasta(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER"),
            Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer"),
            Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data"),
            Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "User Data"),
            Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData), "BraveSoftware"),
            Combinar(Env("APPDATA"), "Opera Software"),
            Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData), "Mozilla", "Firefox", "Profiles"),
            Combinar(Env("APPDATA"), "discord"),
            Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData), "Spotify"),
            Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData), "NVIDIA"),
            Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData), "AMD"),
            Combinar(Pasta(Environment.SpecialFolder.LocalApplicationData), "D3DSCache"),
            PastaDaSteam()
        };

        return raizes.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!).ToList();
    }

    /// <summary>
    /// Raízes que podem ser LIDAS durante a varredura. Inclui as de remoção mais
    /// as pastas que entram só como sugestão.
    ///
    /// Separar leitura de remoção é o que permite mostrar "você tem 3 GB de
    /// instaladores velhos em Downloads" sem nunca ganhar permissão para apagar
    /// nada de lá: os alvos marcados como sugestão jamais chegam ao caminho de
    /// remoção, e mesmo que chegassem, a whitelist de lá não os cobre.
    /// </summary>
    public static IReadOnlyList<string> RaizesParaMedir()
    {
        var raizes = RaizesPermitidas().ToList();

        var downloads = Combinar(Env("USERPROFILE"), "Downloads");
        if (!string.IsNullOrWhiteSpace(downloads))
            raizes.Add(downloads);

        return raizes;
    }
}
