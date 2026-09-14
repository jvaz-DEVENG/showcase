using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Settings;

namespace GameBoost.Core.Modules.Uninstaller;

/// <summary>Um app com versão nova disponível no winget.</summary>
public sealed record AtualizacaoDisponivel
{
    public required string Nome { get; init; }
    public required string Id { get; init; }
    public required string VersaoAtual { get; init; }
    public required string VersaoNova { get; init; }
    public required string Fonte { get; init; }

    /// <summary>
    /// O winget não conseguiu ler a versão instalada. Aparece como "Unknown" ou
    /// com "&lt;" antes do número: nesses casos ele **não sabe** se há mesmo
    /// atualização, só que a versão do repositório é diferente.
    /// </summary>
    public bool VersaoIncerta { get; init; }

    public bool DaStore => Fonte.Equals("msstore", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Uma linha do updates-history.json.</summary>
public sealed class RegistroDeAtualizacao
{
    public string Id { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string De { get; set; } = string.Empty;
    public string Para { get; set; } = string.Empty;
    public DateTimeOffset Quando { get; set; }
    public bool Sucesso { get; set; }
    public int CodigoDeSaida { get; set; }
    public string? Erro { get; set; }
}

/// <summary>
/// Aba "Atualizações" da seção 5.3, em cima do winget.
///
/// O winget é o gerenciador de pacotes que a própria Microsoft entrega com o
/// Windows. Usar ele, em vez de baixar instalador por conta própria, é o que
/// mantém o GameBoost longe de ser mais um programa que puxa executável da
/// internet — quem baixa e verifica assinatura é a Microsoft.
///
/// **A saída do winget é uma tabela de texto, e isso é o problema desta
/// classe.** Não há formato estruturado para `winget upgrade`: nem JSON, nem
/// XML. O que existe é uma tabela de largura fixa cujos cabeçalhos são
/// traduzidos para o idioma do Windows. Procurar a palavra "Available" quebraria
/// em português, onde ela é "Disponível".
///
/// A saída é lida por **posição de coluna**, calculada a partir da linha de
/// cabeçalho: onde cada coluna começa não depende de idioma nenhum.
/// </summary>
public sealed class WingetService
{
    /// <summary>
    /// Uma listagem pode demorar: a primeira execução do dia baixa o índice das
    /// fontes. Dois minutos é o suficiente e evita travar a tela para sempre.
    /// </summary>
    private static readonly TimeSpan LimiteDeListagem = TimeSpan.FromMinutes(2);

    /// <summary>Uma instalação grande (SDK, runtime) passa fácil de 5 minutos.</summary>
    private static readonly TimeSpan LimiteDeAtualizacao = TimeSpan.FromMinutes(15);

    private readonly AppPaths _paths;
    private readonly IFileSystem _fs;
    private readonly IGameBoostLogger _log;
    private readonly IClock _relogio;

    public WingetService(AppPaths paths, IFileSystem fs, IGameBoostLogger log, IClock relogio)
    {
        _paths = paths;
        _fs = fs;
        _log = log;
        _relogio = relogio;
    }

    public string HistoricoFile => Path.Combine(_paths.RootDirectory, "updates-history.json");

    /// <summary>Versão do winget, ou null quando ele não está instalado.</summary>
    public string? Versao { get; private set; }

    public bool Disponivel => Versao is not null;

    /// <summary>
    /// Link que abre a Microsoft Store na página do App Installer, que é o
    /// pacote que traz o winget.
    /// </summary>
    public const string LinkDaStore = "ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1";

    // ==================================================================
    // Disponibilidade
    // ==================================================================

    public async Task<string?> DetectarAsync(CancellationToken ct)
    {
        var resultado = await RodarAsync("--version", TimeSpan.FromSeconds(20), ct);

        if (resultado.CodigoDeSaida != 0 || resultado.Saida.Length == 0)
        {
            Versao = null;
            _log.Info("winget", "Detectar", null, "winget não encontrado nesta máquina");
            return null;
        }

        Versao = resultado.Saida.Trim().TrimStart('v');
        _log.Info("winget", "Detectar", null, $"winget {Versao}");
        return Versao;
    }

    // ==================================================================
    // Listagem
    // ==================================================================

    public async Task<IReadOnlyList<AtualizacaoDisponivel>> ListarAsync(CancellationToken ct)
    {
        if (!Disponivel && await DetectarAsync(ct) is null)
            return Array.Empty<AtualizacaoDisponivel>();

        var resultado = await RodarAsync(
            "upgrade --include-unknown --accept-source-agreements --disable-interactivity",
            LimiteDeListagem, ct);

        // Código diferente de zero aqui é comum e não significa falha: o winget
        // devolve -1978335212 quando simplesmente não há nada a atualizar.
        if (resultado.Saida.Length == 0)
        {
            _log.Warn("winget", "Listar", null,
                $"saída vazia, código {resultado.CodigoDeSaida}: {resultado.Erro}");
            return Array.Empty<AtualizacaoDisponivel>();
        }

        var lista = Interpretar(resultado.Saida);

        _log.Info("winget", "Listar", null, $"{lista.Count} atualizações disponíveis");
        return lista;
    }

    /// <summary>
    /// Lê a tabela do winget por posição de coluna.
    ///
    /// O cabeçalho é a linha imediatamente anterior à régua de tracinhos. As
    /// colunas começam onde há um caractere visível logo depois de um espaço —
    /// e é só isso que precisa ser verdade, em qualquer idioma.
    /// </summary>
    internal static IReadOnlyList<AtualizacaoDisponivel> Interpretar(string saida)
    {
        var linhas = saida.Replace("\r", string.Empty).Split('\n');

        var regua = -1;

        for (var i = 1; i < linhas.Length; i++)
        {
            var limpa = Limpar(linhas[i]);

            // A régua tem só tracinhos, e é longa: uma linha com três traços no
            // meio de um nome de pacote não conta.
            if (limpa.Length >= 10 && limpa.All(c => c == '-'))
            {
                regua = i;
                break;
            }
        }

        if (regua <= 0)
            return Array.Empty<AtualizacaoDisponivel>();

        var colunas = Colunas(Limpar(linhas[regua - 1]));

        // Cinco colunas: nome, id, versão, disponível, fonte. Menos que isso
        // significa que a tabela não é a que esperamos.
        if (colunas.Count < 5)
            return Array.Empty<AtualizacaoDisponivel>();

        var itens = new List<AtualizacaoDisponivel>();

        for (var i = regua + 1; i < linhas.Length; i++)
        {
            var linha = Limpar(linhas[i]);

            if (linha.Trim().Length == 0)
                continue;

            // O rodapé ("35 upgrades available.") não é uma linha de tabela: ele
            // não alcança a coluna do id.
            if (linha.Length < colunas[1] + 1)
                continue;

            var nome = Fatiar(linha, colunas[0], colunas[1]);
            var id = Fatiar(linha, colunas[1], colunas[2]);
            var atual = Fatiar(linha, colunas[2], colunas[3]);
            var nova = Fatiar(linha, colunas[3], colunas[4]);
            var fonte = Fatiar(linha, colunas[4], linha.Length);

            if (nome.Length == 0 || id.Length == 0 || nova.Length == 0)
                continue;

            // Id com espaço no meio é sinal de que a fatia caiu no lugar errado.
            if (id.Contains(' '))
                continue;

            itens.Add(new AtualizacaoDisponivel
            {
                Nome = nome,
                Id = id,
                VersaoAtual = atual,
                VersaoNova = nova,
                Fonte = fonte.Length == 0 ? "winget" : fonte,
                VersaoIncerta = atual.StartsWith('<')
                                || atual.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                                || atual.Equals("Desconhecido", StringComparison.OrdinalIgnoreCase)
            });
        }

        return itens;
    }

    /// <summary>Índices onde cada coluna começa.</summary>
    private static List<int> Colunas(string cabecalho)
    {
        var inicios = new List<int>();

        for (var i = 0; i < cabecalho.Length; i++)
        {
            if (cabecalho[i] == ' ')
                continue;

            if (i == 0 || cabecalho[i - 1] == ' ')
                inicios.Add(i);
        }

        return inicios;
    }

    private static string Fatiar(string linha, int inicio, int fim)
    {
        if (inicio >= linha.Length)
            return string.Empty;

        var real = Math.Min(fim, linha.Length);
        return linha[inicio..real].Trim();
    }

    /// <summary>
    /// Tira o lixo de terminal da linha. O winget desenha barra de progresso com
    /// backspace e sequências ANSI mesmo com a saída redirecionada, e esses
    /// bytes deslocariam todas as colunas.
    /// </summary>
    private static string Limpar(string linha)
    {
        var sb = new StringBuilder(linha.Length);

        for (var i = 0; i < linha.Length; i++)
        {
            var c = linha[i];

            if (c == '')
            {
                // Sequência ANSI: pula até a letra que a encerra.
                while (i < linha.Length && !char.IsLetter(linha[i]))
                    i++;

                continue;
            }

            if (c is '\b' or ' ')
                continue;

            sb.Append(c);
        }

        return sb.ToString();
    }

    // ==================================================================
    // Atualização
    // ==================================================================

    public async Task<RegistroDeAtualizacao> AtualizarAsync(
        AtualizacaoDisponivel alvo, bool dryRun, CancellationToken ct)
    {
        var registro = new RegistroDeAtualizacao
        {
            Id = alvo.Id,
            Nome = alvo.Nome,
            De = alvo.VersaoAtual,
            Para = alvo.VersaoNova,
            Quando = _relogio.Now
        };

        if (dryRun)
        {
            registro.Sucesso = true;
            registro.Erro = "Simulação: nada foi instalado.";
            return registro;
        }

        // `--id` com `--exact` evita o winget escolher outro pacote de nome
        // parecido. Sem isso, um id curto pode casar com mais de um e ele
        // instala o errado ou pede escolha, que numa sessão sem terminal
        // significa travar.
        var argumentos =
            $"upgrade --id {alvo.Id} --exact --silent "
          + "--accept-package-agreements --accept-source-agreements --disable-interactivity";

        if (alvo.VersaoIncerta)
            argumentos += " --include-unknown";

        var resultado = await RodarAsync(argumentos, LimiteDeAtualizacao, ct);

        registro.CodigoDeSaida = resultado.CodigoDeSaida;
        registro.Sucesso = resultado.CodigoDeSaida == 0;

        if (!registro.Sucesso)
            registro.Erro = Explicar(resultado.CodigoDeSaida, resultado.Erro, resultado.Saida);

        _log.Log(registro.Sucesso ? LogLevel.Info : LogLevel.Warn,
            "winget", "Atualizar", alvo.Id,
            registro.Sucesso ? $"{alvo.VersaoAtual} -> {alvo.VersaoNova}" : registro.Erro ?? "falhou");

        Registrar(registro);
        return registro;
    }

    /// <summary>
    /// Traduz o código de saída do winget. Os números dele são inteiros
    /// negativos enormes e não dizem nada a ninguém; o que o usuário precisa
    /// saber é se pode tentar de novo.
    /// </summary>
    internal static string Explicar(int codigo, string erro, string saida)
    {
        var texto = codigo switch
        {
            unchecked((int)0x8A150011) => "O pacote já está na versão mais nova.",
            unchecked((int)0x8A150014) => "Nenhum pacote com esse id foi encontrado na fonte.",
            unchecked((int)0x8A15002B) => "Este pacote não tem instalador compatível com esta máquina.",
            unchecked((int)0x8A150045) => "O winget não conseguiu baixar o instalador. Pode ser a rede.",
            unchecked((int)0x8A15010D) => "A atualização precisa de privilégios de administrador.",
            unchecked((int)0x8A150056) => "O aplicativo está aberto. Feche e tente de novo.",
            1602 => "A instalação foi cancelada.",
            1603 => "O instalador do próprio app falhou.",
            1618 => "Outra instalação está em andamento. Espere terminar.",
            _ => $"O winget terminou com o código {codigo}."
        };

        var detalhe = erro.Trim();

        if (detalhe.Length == 0)
            detalhe = saida.Trim().Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? string.Empty;

        return detalhe.Length > 0 && detalhe.Length < 300
            ? $"{texto} ({detalhe})"
            : texto;
    }

    /// <summary>
    /// Pasta onde o winget grava o log detalhado de cada instalação. É para
    /// onde apontar quando um update falha por motivo que o código de saída não
    /// explica.
    /// </summary>
    public static string PastaDeLogs()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pacotes = Path.Combine(local, "Packages");

        if (!Directory.Exists(pacotes))
            return pacotes;

        try
        {
            var pasta = Directory.EnumerateDirectories(pacotes, "Microsoft.DesktopAppInstaller_*")
                .FirstOrDefault();

            return pasta is null
                ? pacotes
                : Path.Combine(pasta, "LocalState", "DiagOutputDir");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return pacotes;
        }
    }

    // ==================================================================
    // Histórico
    // ==================================================================

    public IReadOnlyList<RegistroDeAtualizacao> Historico()
    {
        try
        {
            if (!_fs.FileExists(HistoricoFile))
                return Array.Empty<RegistroDeAtualizacao>();

            var json = _fs.ReadAllText(HistoricoFile);

            return JsonSerializer.Deserialize<List<RegistroDeAtualizacao>>(json)
                   ?? new List<RegistroDeAtualizacao>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _log.Warn("winget", "Historico", null, ex.Message);
            return Array.Empty<RegistroDeAtualizacao>();
        }
    }

    private void Registrar(RegistroDeAtualizacao registro)
    {
        try
        {
            var todos = Historico().ToList();
            todos.Add(registro);

            // Duzentas linhas dão vários meses de histórico e mantêm o arquivo
            // pequeno o bastante para ser lido de uma vez.
            if (todos.Count > 200)
                todos = todos.Skip(todos.Count - 200).ToList();

            _fs.WriteAllText(HistoricoFile,
                JsonSerializer.Serialize(todos, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn("winget", "Registrar", registro.Id, ex.Message);
        }
    }

    // ==================================================================
    // Execução
    // ==================================================================

    private sealed record ResultadoDoProcesso(int CodigoDeSaida, string Saida, string Erro);

    private async Task<ResultadoDoProcesso> RodarAsync(string argumentos, TimeSpan limite, CancellationToken ct)
    {
        try
        {
            using var processo = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "winget.exe",
                    Arguments = argumentos,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,

                    // O winget escreve em UTF-8. Sem isto, nome de pacote com
                    // acento chega quebrado e, pior, com número de bytes
                    // diferente do de caracteres — o que desalinha as colunas.
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            if (!processo.Start())
                return new ResultadoDoProcesso(-1, string.Empty, "Não foi possível iniciar o winget.");

            // As duas saídas precisam ser lidas em paralelo: se só uma for lida,
            // o buffer da outra enche e o processo trava esperando alguém ler.
            // Foi exatamente esse erro que fez o Get-AppxPackage devolver zero
            // na Fase 4.
            var lendoSaida = processo.StandardOutput.ReadToEndAsync(ct);
            var lendoErro = processo.StandardError.ReadToEndAsync(ct);

            using var limiteCt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limiteCt.CancelAfter(limite);

            try
            {
                await processo.WaitForExitAsync(limiteCt.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { processo.Kill(entireProcessTree: true); } catch { /* já morreu */ }

                return new ResultadoDoProcesso(-1, string.Empty,
                    $"O winget passou de {limite.TotalMinutes:0} minutos e foi encerrado.");
            }

            return new ResultadoDoProcesso(processo.ExitCode, await lendoSaida, await lendoErro);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Win32Exception aqui quase sempre é "arquivo não encontrado": o
            // winget não está instalado.
            return new ResultadoDoProcesso(-1, string.Empty, ex.Message);
        }
    }
}
