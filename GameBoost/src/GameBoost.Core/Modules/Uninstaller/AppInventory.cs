using System.Diagnostics;
using System.Text;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Native;
using Microsoft.Win32;

namespace GameBoost.Core.Modules.Uninstaller;

/// <summary>
/// Monta a lista de aplicativos instalados (seção 5.3): registro, Store e
/// último uso pelo UserAssist.
/// </summary>
public sealed class AppInventory
{
    private readonly IRegistryService _registro;
    private readonly IGameBoostLogger _log;

    public AppInventory(IRegistryService registro, IGameBoostLogger log)
    {
        _registro = registro;
        _log = log;
    }

    private static readonly (RegistryRoot Raiz, string Caminho)[] ChavesDeDesinstalacao =
    {
        (RegistryRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryRoot.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryRoot.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
    };

    public IReadOnlyList<InstalledApp> Listar(bool medirTamanho, CancellationToken ct)
    {
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in DoRegistro(ct))
            apps.TryAdd(app.Id, app);

        foreach (var app in DaStore(ct))
            apps.TryAdd(app.Id, app);

        var usos = LerUltimoUso();

        foreach (var app in apps.Values)
        {
            ct.ThrowIfCancellationRequested();

            UninstallerProtection.Avaliar(app);

            if (app.PastaDeInstalacao is not null)
            {
                var executavel = Path.GetFileName(app.PastaDeInstalacao);

                if (usos.TryGetValue(executavel, out var quando))
                    app.UltimoUso = quando;
                else if (Directory.Exists(app.PastaDeInstalacao))
                    app.UltimoUso = UltimoAcessoDaPasta(app.PastaDeInstalacao);

                if (medirTamanho && app.TamanhoMedido == 0)
                    app.TamanhoMedido = TamanhoDaPasta(app.PastaDeInstalacao, ct);
            }
        }

        return apps.Values.OrderByDescending(a => a.Tamanho).ToList();
    }

    // ---------------- Registro ----------------

    private IEnumerable<InstalledApp> DoRegistro(CancellationToken ct)
    {
        foreach (var (raiz, caminho) in ChavesDeDesinstalacao)
        {
            IReadOnlyList<string> chaves;

            try
            {
                chaves = _registro.GetSubKeyNames(raiz, caminho);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                _log.Warn("Uninstaller", "Registro", caminho, ex.Message);
                continue;
            }

            foreach (var chave in chaves)
            {
                ct.ThrowIfCancellationRequested();

                var app = Montar(raiz, $@"{caminho}\{chave}", chave);
                if (app is not null)
                    yield return app;
            }
        }
    }

    private InstalledApp? Montar(RegistryRoot raiz, string caminho, string chave)
    {
        var nome = Texto(raiz, caminho, "DisplayName");

        if (string.IsNullOrWhiteSpace(nome))
            return null;

        // Atualizações e componentes de sistema não são "aplicativos": listar
        // encheria a tela com centenas de entradas que ninguém desinstala.
        if (Numero(raiz, caminho, "SystemComponent") == 1)
            return null;

        if (!string.IsNullOrWhiteSpace(Texto(raiz, caminho, "ParentKeyName")))
            return null;

        if (Texto(raiz, caminho, "ReleaseType") is "Security Update" or "Update" or "Hotfix")
            return null;

        var comando = Texto(raiz, caminho, "UninstallString");
        var silencioso = Texto(raiz, caminho, "QuietUninstallString");
        var pasta = Texto(raiz, caminho, "InstallLocation");

        // Chave no formato {GUID} com WindowsInstaller=1 é MSI: dá para
        // desinstalar em silêncio mesmo sem QuietUninstallString.
        var ehMsi = Numero(raiz, caminho, "WindowsInstaller") == 1
                 && chave.StartsWith('{') && chave.EndsWith('}');

        return new InstalledApp
        {
            Id = chave,
            Nome = nome!.Trim(),
            Origem = AppOrigem.Win32,
            Versao = Texto(raiz, caminho, "DisplayVersion"),
            Publisher = Texto(raiz, caminho, "Publisher"),
            PastaDeInstalacao = string.IsNullOrWhiteSpace(pasta) ? null : pasta.Trim('"'),
            TamanhoEstimado = Numero(raiz, caminho, "EstimatedSize") * 1024L,
            Instalado = DataDeInstalacao(Texto(raiz, caminho, "InstallDate")),
            Comando = comando,
            ComandoSilencioso = silencioso,
            CodigoMsi = ehMsi ? chave : null
        };
    }

    private string? Texto(RegistryRoot raiz, string caminho, string valor)
    {
        try
        {
            return _registro.GetValue(raiz, caminho, valor) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private int Numero(RegistryRoot raiz, string caminho, string valor)
    {
        try
        {
            return _registro.GetValue(raiz, caminho, valor) is int i ? i : 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static DateTimeOffset? DataDeInstalacao(string? bruto)
        => !string.IsNullOrWhiteSpace(bruto)
           && DateTime.TryParseExact(bruto, "yyyyMMdd", null,
               System.Globalization.DateTimeStyles.None, out var data)
            ? new DateTimeOffset(data)
            : null;

    // ---------------- Store ----------------

    /// <summary>
    /// Aplicativos da Store. É o único ponto do GameBoost que usa PowerShell,
    /// porque Get-AppxPackage não tem equivalente acessível de um processo
    /// comum (regra 9 prevê exatamente esta exceção).
    /// </summary>
    private IEnumerable<InstalledApp> DaStore(CancellationToken ct)
    {
        var saida = RodarPowerShell(
            // Sem isto o PowerShell escreve barras de progresso em CLIXML no
            // stderr, que e justamente o que enchia o buffer e travava a leitura.
            "$ProgressPreference = 'SilentlyContinue'; " +
            "Get-AppxPackage | Where-Object { -not $_.IsFramework } | " +
            "ForEach-Object { \"$($_.Name)|$($_.PackageFullName)|$($_.InstallLocation)|$($_.Version)|$($_.Publisher)|$($_.NonRemovable)\" }",
            ct);

        if (saida is null)
            yield break;

        foreach (var linha in saida.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var partes = linha.Trim().Split('|');
            if (partes.Length < 6)
                continue;

            var naoRemovivel = partes[5].Equals("True", StringComparison.OrdinalIgnoreCase);

            yield return new InstalledApp
            {
                Id = partes[1],
                Nome = Legivel(partes[0]),
                Origem = AppOrigem.Store,
                PacoteDaStore = partes[1],
                PastaDeInstalacao = string.IsNullOrWhiteSpace(partes[2]) ? null : partes[2],
                Versao = partes[3],
                Publisher = LimparPublisher(partes[4]),
                Protegido = naoRemovivel,
                MotivoDaProtecao = naoRemovivel
                    ? "O Windows marca este pacote como não removível."
                    : null
            };
        }
    }

    /// <summary>"Microsoft.WindowsCalculator" fica ilegível; vira "Windows Calculator".</summary>
    private static string Legivel(string nomeDoPacote)
    {
        var nome = nomeDoPacote;

        var ponto = nome.LastIndexOf('.');
        if (ponto > 0 && ponto < nome.Length - 1)
            nome = nome[(ponto + 1)..];

        var sb = new StringBuilder();

        for (var i = 0; i < nome.Length; i++)
        {
            if (i > 0 && char.IsUpper(nome[i]) && !char.IsUpper(nome[i - 1]))
                sb.Append(' ');

            sb.Append(nome[i]);
        }

        return sb.ToString();
    }

    private static string? LimparPublisher(string publisher)
    {
        // Vem como "CN=Microsoft Corporation, O=..., L=...".
        var cn = publisher.Split(',')
            .FirstOrDefault(p => p.TrimStart().StartsWith("CN=", StringComparison.OrdinalIgnoreCase));

        return cn is null ? null : cn.TrimStart()[3..].Trim();
    }

    private string? RodarPowerShell(string comando, CancellationToken ct)
    {
        try
        {
            // -EncodedCommand em vez de -Command: o script tem pipes, cifroes e
            // aspas, e passar isso pela linha de comando faz o shell reinterpretar
            // os simbolos. Foi o que aconteceu na primeira versao, que devolvia
            // zero pacotes numa maquina com 142 instalados.
            var codificado = Convert.ToBase64String(Encoding.Unicode.GetBytes(comando));

            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                // Regra 9: quando PowerShell é inevitável, sempre com estes flags.
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {codificado}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var processo = Process.Start(info);
            if (processo is null)
                return null;

            // Os dois fluxos precisam ser lidos ao mesmo tempo. Ler so o stdout
            // e deixar o stderr redirecionado sem leitor trava o processo assim
            // que o buffer do stderr enche: foi o que fez esta consulta devolver
            // zero pacotes numa maquina com 142 instalados.
            var lendoSaida = processo.StandardOutput.ReadToEndAsync(ct);
            var lendoErro = processo.StandardError.ReadToEndAsync(ct);

            if (!processo.WaitForExit(30_000))
            {
                processo.Kill(entireProcessTree: true);
                _log.Warn("Uninstaller", "Store", null, "Get-AppxPackage demorou demais e foi encerrado");
                return null;
            }

            var erro = lendoErro.GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(erro))
                _log.Warn("Uninstaller", "Store", null, erro.Split(Environment.NewLine)[0]);

            return lendoSaida.GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _log.Warn("Uninstaller", "Store", null, $"nao foi possivel listar apps da Store: {ex.Message}");
            return null;
        }
    }

    // ---------------- Último uso ----------------

    /// <summary>
    /// Lê o UserAssist, onde o Explorer registra o que o usuário abriu. Os
    /// nomes vêm em ROT13 — não é criptografia, é ofuscação simples da Microsoft.
    /// </summary>
    private Dictionary<string, DateTimeOffset> LerUltimoUso()
    {
        var resultado = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        const string raiz = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist";

        try
        {
            foreach (var guid in _registro.GetSubKeyNames(RegistryRoot.CurrentUser, raiz))
            {
                var contagem = $@"{raiz}\{guid}\Count";

                foreach (var valor in _registro.GetValueNames(RegistryRoot.CurrentUser, contagem))
                {
                    var nome = Rot13(valor);
                    var executavel = Path.GetFileName(nome);

                    if (string.IsNullOrWhiteSpace(executavel))
                        continue;

                    if (_registro.GetValue(RegistryRoot.CurrentUser, contagem, valor) is not byte[] dados
                        || dados.Length < 68)
                    {
                        continue;
                    }

                    // Deslocamento 60: FILETIME do último uso.
                    var ticks = BitConverter.ToInt64(dados, 60);
                    if (ticks <= 0)
                        continue;

                    try
                    {
                        var quando = DateTimeOffset.FromFileTime(ticks);

                        if (!resultado.TryGetValue(executavel, out var atual) || quando > atual)
                            resultado[executavel] = quando;
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            _log.Warn("Uninstaller", "UserAssist", null, ex.Message);
        }

        return resultado;
    }

    internal static string Rot13(string texto)
    {
        var saida = texto.ToCharArray();

        for (var i = 0; i < saida.Length; i++)
        {
            var c = saida[i];

            if (c is >= 'a' and <= 'z')
                saida[i] = (char)('a' + (c - 'a' + 13) % 26);
            else if (c is >= 'A' and <= 'Z')
                saida[i] = (char)('A' + (c - 'A' + 13) % 26);
        }

        return new string(saida);
    }

    private static DateTimeOffset? UltimoAcessoDaPasta(string pasta)
    {
        try
        {
            return new DateTimeOffset(Directory.GetLastWriteTimeUtc(pasta), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long TamanhoDaPasta(string pasta, CancellationToken ct)
    {
        if (!Directory.Exists(pasta))
            return 0;

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
}
