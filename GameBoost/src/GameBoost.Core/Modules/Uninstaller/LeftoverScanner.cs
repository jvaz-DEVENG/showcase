using GameBoost.Core.Logging;
using GameBoost.Core.Native;

namespace GameBoost.Core.Modules.Uninstaller;

public sealed record Resto(
    string Nome,
    string Caminho,
    long Bytes,
    DateTime Modificado,
    string Local)
{
    public string Idade
    {
        get
        {
            var dias = (DateTime.Now - Modificado).TotalDays;

            return dias switch
            {
                < 31 => "mexido este mês",
                < 180 => $"parado há {(int)(dias / 30)} meses",
                < 365 => "parado há mais de 6 meses",
                _ => $"parado há {(int)(dias / 365)} ano(s)"
            };
        }
    }
}

/// <summary>
/// Aba "Restos de apps antigos" (pedido de 13/09/2026, registrado na seção 5.3).
///
/// Varre %APPDATA%, %LOCALAPPDATA% e %PROGRAMDATA% procurando pastas cujo nome
/// não corresponde a nenhum app instalado. É a sobra que fica depois de
/// desinstalar, e que nenhum desinstalador limpa.
///
/// **Nada aqui é apagado automaticamente.** O casamento de nomes é uma
/// heurística: uma pasta pode pertencer a um app que o registro não declara.
/// Por isso tudo entra desmarcado e a remoção vai para a Lixeira.
/// </summary>
public sealed class LeftoverScanner
{
    private readonly IGameBoostLogger _log;

    public LeftoverScanner(IGameBoostLogger log)
    {
        _log = log;
    }

    /// <summary>
    /// Fabricantes cujas pastas nunca entram na lista: são de sistema, de
    /// driver ou compartilhadas por vários programas. Documentado em
    /// docs/PROTECAO.md.
    /// </summary>
    private static readonly IReadOnlySet<string> Ignoradas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Microsoft e Windows
        "microsoft", "microsoftedge", "microsoft corporation", "windows", "windowsapps",
        "microsoft onedrive", "onedrive", "packages", "temp", "tmp", "cache",
        "comms", "connecteddevicesplatform", "d3dscache", "elevatedDiagnostics",
        "publisher cache", "virtualstore", "webcachelock", "iconcache",
        "package cache", "installer", "assembly", "diagnostics", "history",
        "application data", "appdata", "programdata", "usoshared", "usoprivate",
        "windowsholographicdevices", "desktop", "start menu", "ssh",
        // Pastas-guarda-chuva: contem varios apps ativos dentro, nao sao resto
        // de nada. "Programs" apareceu com 9 GB no primeiro teste real.
        "programs", "apps", "local", "roaming", "lowlevel", "virtual machines",

        // Fabricantes de hardware e driver
        "nvidia", "nvidia corporation", "amd", "ati", "intel", "intelgraphicsprofiles",
        "realtek", "logishrd", "logitech", "razer", "corsair", "steelseries",
        "synaptics", "elan", "qualcomm", "broadcom",

        // Compartilhadas por muitos programas
        "adobe", "common files", "oracle", "java", "sun", "mozilla", "google",
        "chromium", "chrome", "crashdumps", "crashpad", "crashreports",
        "sentry", "squirreltemp", "node", "npm", "npm-cache", "yarn", "pip",
        "nuget", ".nuget", "dotnet", "jetbrains", "visualstudio", "vscode",
        "code", "github", "git", "docker", "kaspersky lab", "eset", "avast software"
    };

    public IReadOnlyList<Resto> Varrer(
        IReadOnlyList<InstalledApp> instalados,
        IProgress<string>? progresso,
        CancellationToken ct)
    {
        // O conjunto de nomes conhecidos vem dos apps instalados e dos seus
        // fabricantes: "Discord" e "Discord Inc." devem casar com a mesma pasta.
        var conhecidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in instalados)
        {
            AdicionarTermos(conhecidos, app.Nome);

            if (app.Publisher is not null)
                AdicionarTermos(conhecidos, app.Publisher);

            if (app.PastaDeInstalacao is not null)
                conhecidos.Add(Path.GetFileName(app.PastaDeInstalacao.TrimEnd('\\')));
        }

        var restos = new List<Resto>();

        foreach (var (rotulo, raiz) in Locais())
        {
            ct.ThrowIfCancellationRequested();

            if (raiz is null || !Directory.Exists(raiz))
                continue;

            progresso?.Report($"Procurando restos em {rotulo}");

            foreach (var entrada in FastFind.Listar(raiz))
            {
                ct.ThrowIfCancellationRequested();

                if (!entrada.EhPasta)
                    continue;

                var nome = entrada.Nome;

                if (Ignoradas.Contains(nome) || nome.StartsWith('.'))
                    continue;

                if (EhConhecida(nome, conhecidos))
                    continue;

                var caminho = Path.Combine(raiz, nome);
                var tamanho = TamanhoDaPasta(caminho, ct);

                // Pasta minuscula nao vale o ruido na lista.
                if (tamanho < 1024 * 1024)
                    continue;

                restos.Add(new Resto(nome, caminho, tamanho, UltimaModificacao(caminho), rotulo));
            }
        }

        _log.Info("Uninstaller", "Restos", null,
            $"{restos.Count} pastas sem app correspondente, "
          + $"{GameMode.GameModeModule.Formatar(restos.Sum(r => r.Bytes))}");

        return restos.OrderByDescending(r => r.Bytes).ToList();
    }

    /// <summary>
    /// Decide se a pasta pertence a algum app instalado.
    ///
    /// Casamento exato sozinho é rígido demais: no primeiro teste real ele
    /// acusou "BraveSoftware" e "EpicGamesLauncher" como restos, sendo que os
    /// dois estão instalados — só escrevem a pasta sem os espaços do nome. Por
    /// isso a comparação normaliza espaços e pontuação, e aceita prefixo a
    /// partir de 5 caracteres.
    ///
    /// Falso positivo aqui é pior que falso negativo: deixar de listar um resto
    /// custa espaço, apontar um app ativo como resto custa os dados do usuário.
    /// </summary>
    private static bool EhConhecida(string pasta, HashSet<string> conhecidos)
    {
        if (conhecidos.Contains(pasta))
            return true;

        var alvo = Normalizar(pasta);

        if (alvo.Length < 3)
            return true;

        foreach (var conhecido in conhecidos)
        {
            var termo = Normalizar(conhecido);

            if (termo.Length < 4)
                continue;

            if (termo == alvo)
                return true;

            // "brave" cobre "bravesoftware"; "epicgameslauncher" cobre a pasta
            // de mesmo nome sem espacos.
            if (termo.Length >= 5 && alvo.StartsWith(termo, StringComparison.Ordinal))
                return true;

            if (alvo.Length >= 5 && termo.StartsWith(alvo, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string Normalizar(string texto)
    {
        Span<char> destino = stackalloc char[texto.Length];
        var tamanho = 0;

        foreach (var c in texto)
        {
            if (char.IsLetterOrDigit(c))
                destino[tamanho++] = char.ToLowerInvariant(c);
        }

        return new string(destino[..tamanho]);
    }

    /// <summary>
    /// Quebra "Discord Inc." em termos aproveitáveis e descarta sufixos de
    /// razão social, que nunca viram nome de pasta.
    /// </summary>
    private static void AdicionarTermos(HashSet<string> destino, string texto)
    {
        destino.Add(texto);

        var limpo = texto;

        foreach (var sufixo in new[] { ", Inc.", " Inc.", " Inc", " LLC", " Ltd.", " Ltd",
                                        " GmbH", " S.A.", " Corporation", " Corp.", " Corp",
                                        " Software", " Technologies", " Studios", " Entertainment" })
        {
            if (limpo.EndsWith(sufixo, StringComparison.OrdinalIgnoreCase))
                limpo = limpo[..^sufixo.Length].Trim();
        }

        if (limpo.Length > 2)
            destino.Add(limpo);

        // A primeira palavra cobre "Epic Games Launcher" contra a pasta "Epic".
        var primeira = limpo.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (primeira is { Length: > 3 })
            destino.Add(primeira);
    }

    private static IEnumerable<(string Rotulo, string? Raiz)> Locais()
    {
        yield return ("AppData\\Roaming", Environment.GetEnvironmentVariable("APPDATA"));
        yield return ("AppData\\Local", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        yield return ("ProgramData", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
    }

    private static long TamanhoDaPasta(string pasta, CancellationToken ct)
    {
        long total = 0;
        var pilha = new Stack<string>();
        pilha.Push(pasta);

        while (pilha.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var atual = pilha.Pop();

            foreach (var entrada in FastFind.Listar(atual))
            {
                if (entrada.EhPasta)
                    pilha.Push(Path.Combine(atual, entrada.Nome));
                else
                    total += entrada.Tamanho;
            }
        }

        return total;
    }

    private static DateTime UltimaModificacao(string pasta)
    {
        try
        {
            return Directory.GetLastWriteTime(pasta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }
}
