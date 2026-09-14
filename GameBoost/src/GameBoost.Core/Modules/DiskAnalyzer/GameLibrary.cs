using System.Text.RegularExpressions;
using GameBoost.Core.Logging;
using Microsoft.Win32;

namespace GameBoost.Core.Modules.DiskAnalyzer;

public sealed record JogoInstalado(
    string Nome,
    string Launcher,
    string Pasta,
    long Bytes,
    DateTimeOffset? UltimaVez)
{
    public string QuandoJogou => UltimaVez is null
        ? "sem registro"
        : (DateTimeOffset.Now - UltimaVez.Value).TotalDays switch
        {
            < 7 => "esta semana",
            < 31 => "este mês",
            < 90 => "nos últimos 3 meses",
            < 365 => $"há {(int)((DateTimeOffset.Now - UltimaVez.Value).TotalDays / 30)} meses",
            _ => "há mais de um ano"
        };

    /// <summary>Jogo grande e parado há muito é onde estão os GB de verdade (seção 5.11).</summary>
    public bool CandidatoADesinstalar
        => Bytes > 20L * 1024 * 1024 * 1024
           && UltimaVez is not null
           && (DateTimeOffset.Now - UltimaVez.Value).TotalDays > 180;
}

/// <summary>
/// Lê a biblioteca instalada de cada launcher (seção 5.11).
///
/// Serve para o analisador de disco responder o que o usuário realmente quer
/// saber: qual jogo de 90 GB está parado desde o ano passado. Só lê; nunca
/// desinstala nada.
/// </summary>
public sealed class GameLibrary
{
    private readonly IGameBoostLogger _log;

    public GameLibrary(IGameBoostLogger log)
    {
        _log = log;
    }

    public IReadOnlyList<JogoInstalado> Listar()
    {
        var jogos = new List<JogoInstalado>();

        Seguro(() => jogos.AddRange(DaSteam()), "Steam");
        Seguro(() => jogos.AddRange(DaEpic()), "Epic");
        Seguro(() => jogos.AddRange(DaGog()), "GOG");
        Seguro(() => jogos.AddRange(DoXbox()), "Xbox");

        return jogos
            .GroupBy(j => j.Pasta, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(j => j.Bytes)
            .ToList();
    }

    private void Seguro(Action acao, string origem)
    {
        try
        {
            acao();
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or System.Security.SecurityException
                                      or FormatException)
        {
            _log.Warn("DiskAnalyzer", "Biblioteca", origem, ex.Message);
        }
    }

    // ---------------- Steam ----------------

    /// <summary>
    /// A Steam guarda as bibliotecas em libraryfolders.vdf e cada jogo num
    /// appmanifest_*.acf. O .acf traz tamanho e data do último jogo, que é
    /// exatamente o que interessa aqui.
    /// </summary>
    private IEnumerable<JogoInstalado> DaSteam()
    {
        var raiz = CaminhoDaSteam();
        if (raiz is null)
            yield break;

        foreach (var biblioteca in BibliotecasDaSteam(raiz))
        {
            var steamapps = Path.Combine(biblioteca, "steamapps");
            if (!Directory.Exists(steamapps))
                continue;

            foreach (var manifesto in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                var texto = LerTexto(manifesto);
                if (texto is null)
                    continue;

                var nome = Campo(texto, "name");
                var pasta = Campo(texto, "installdir");
                var tamanho = CampoLongo(texto, "SizeOnDisk");
                var ultimo = CampoLongo(texto, "LastPlayed");

                if (nome is null || pasta is null)
                    continue;

                yield return new JogoInstalado(
                    nome,
                    "Steam",
                    Path.Combine(steamapps, "common", pasta),
                    tamanho,
                    ultimo > 0 ? DateTimeOffset.FromUnixTimeSeconds(ultimo) : null);
            }
        }
    }

    private static string? CaminhoDaSteam()
    {
        using var chave = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var caminho = chave?.GetValue("SteamPath") as string;

        return string.IsNullOrWhiteSpace(caminho) ? null : caminho.Replace('/', '\\');
    }

    private static IEnumerable<string> BibliotecasDaSteam(string raiz)
    {
        yield return raiz;

        var vdf = Path.Combine(raiz, "steamapps", "libraryfolders.vdf");
        var texto = LerTexto(vdf);

        if (texto is null)
            yield break;

        // O formato mudou de versão para versão; casar "path" cobre as duas.
        foreach (Match m in Regex.Matches(texto, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
        {
            var caminho = m.Groups[1].Value.Replace(@"\\", @"\");

            if (!caminho.Equals(raiz, StringComparison.OrdinalIgnoreCase) && Directory.Exists(caminho))
                yield return caminho;
        }
    }

    private static string? Campo(string vdf, string chave)
    {
        var m = Regex.Match(vdf, $"\"{chave}\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static long CampoLongo(string vdf, string chave)
        => long.TryParse(Campo(vdf, chave), out var valor) ? valor : 0;

    // ---------------- Epic ----------------

    private IEnumerable<JogoInstalado> DaEpic()
    {
        var pasta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(pasta))
            yield break;

        foreach (var arquivo in Directory.EnumerateFiles(pasta, "*.item"))
        {
            var texto = LerTexto(arquivo);
            if (texto is null)
                continue;

            var nome = Json(texto, "DisplayName");
            var local = Json(texto, "InstallLocation");
            var tamanho = long.TryParse(Json(texto, "InstallSize"), out var v) ? v : 0;

            if (nome is null || local is null)
                continue;

            yield return new JogoInstalado(nome, "Epic", local, tamanho, UltimoUso(local));
        }
    }

    private static string? Json(string texto, string chave)
    {
        var m = Regex.Match(texto, $"\"{chave}\"\\s*:\\s*\"?([^\",}}]*)\"?", RegexOptions.IgnoreCase);
        return m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value) ? m.Groups[1].Value : null;
    }

    // ---------------- GOG ----------------

    private IEnumerable<JogoInstalado> DaGog()
    {
        foreach (var raiz in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var caminho in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games", @"SOFTWARE\GOG.com\Games" })
            {
                using var chave = raiz.OpenSubKey(caminho);
                if (chave is null)
                    continue;

                foreach (var id in chave.GetSubKeyNames())
                {
                    using var jogo = chave.OpenSubKey(id);
                    var nome = jogo?.GetValue("gameName") as string;
                    var pasta = jogo?.GetValue("path") as string;

                    if (nome is null || pasta is null || !Directory.Exists(pasta))
                        continue;

                    yield return new JogoInstalado(nome, "GOG", pasta, TamanhoDaPasta(pasta), UltimoUso(pasta));
                }
            }
        }
    }

    // ---------------- Xbox ----------------

    private IEnumerable<JogoInstalado> DoXbox()
    {
        foreach (var unidade in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            // .GamingRoot aponta a pasta de jogos do Xbox naquele volume.
            var marcador = Path.Combine(unidade.RootDirectory.FullName, ".GamingRoot");
            if (!File.Exists(marcador))
                continue;

            var pastaDeJogos = Path.Combine(unidade.RootDirectory.FullName, "XboxGames");
            if (!Directory.Exists(pastaDeJogos))
                continue;

            foreach (var jogo in Directory.EnumerateDirectories(pastaDeJogos))
            {
                yield return new JogoInstalado(
                    Path.GetFileName(jogo), "Xbox", jogo, TamanhoDaPasta(jogo), UltimoUso(jogo));
            }
        }
    }

    // ---------------- Apoio ----------------

    private static string? LerTexto(string caminho)
    {
        try
        {
            return File.Exists(caminho) ? File.ReadAllText(caminho) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sem data de "último jogo" no launcher, a data de modificação da pasta é
    /// a melhor aproximação: jogo salva progresso e configuração ali.
    /// </summary>
    private static DateTimeOffset? UltimoUso(string pasta)
    {
        try
        {
            return Directory.Exists(pasta) ? new DateTimeOffset(Directory.GetLastWriteTimeUtc(pasta)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long TamanhoDaPasta(string pasta)
    {
        long total = 0;
        var pilha = new Stack<string>();
        pilha.Push(pasta);

        while (pilha.Count > 0)
        {
            var atual = pilha.Pop();

            foreach (var entrada in Native.FastFind.Listar(atual))
            {
                if (entrada.EhPasta)
                    pilha.Push(Path.Combine(atual, entrada.Nome));
                else
                    total += entrada.Tamanho;
            }
        }

        return total;
    }
}
