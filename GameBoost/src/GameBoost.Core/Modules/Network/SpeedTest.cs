using System.Diagnostics;
using GameBoost.Core.Logging;
using GameBoost.Core.Settings;

namespace GameBoost.Core.Modules.Network;

public sealed record ResultadoDeVelocidade(
    double DownloadMbps,
    double UploadMbps,
    long BytesBaixados,
    long BytesEnviados,
    TimeSpan Duracao,
    string? Erro)
{
    public bool Funcionou => Erro is null && DownloadMbps > 0;
}

/// <summary>
/// Teste de velocidade (seção 5.9).
///
/// Três coisas que este teste **não** é, e que a tela precisa dizer:
///
/// - Não substitui um teste de banda dedicado. Ele usa um servidor só, não
///   escolhe o mais próximo e não abre dezenas de conexões paralelas. O número
///   tende a sair abaixo do que o Speedtest mostra.
/// - Não mede a sua internet quando alguém está baixando algo em outra máquina.
/// - Não tem relação com ping. Banda larga e latência são coisas diferentes, e
///   é a latência que decide se o jogo está bom.
///
/// O que ele serve para responder é uma pergunta específica e útil: "a minha
/// conexão está entregando algo próximo do que eu contratei, ou está muito
/// abaixo?".
///
/// **Só roda quando o usuário permite acesso à rede** (regra 8: sem telemetria,
/// e nenhuma chamada externa sem consentimento).
/// </summary>
public sealed class SpeedTest
{
    /// <summary>
    /// Endpoint público da Cloudflare, o mesmo que o speed.cloudflare.com usa.
    /// Foi escolhido por não exigir chave, não registrar o teste numa conta e
    /// ter presença no Brasil.
    /// </summary>
    private const string UrlDownload = "https://speed.cloudflare.com/__down?bytes=";
    private const string UrlUpload = "https://speed.cloudflare.com/__up";

    /// <summary>
    /// Três amostras, mediana no fim (seção 5.9.1). A mediana existe para
    /// descartar a amostra estragada: basta o Windows Update acordar no meio de
    /// uma delas para a média despencar e o número mentir. Com três, a do meio
    /// sobrevive a uma interferência.
    /// </summary>
    private const int Amostras = 3;

    /// <summary>
    /// 25 MB por amostra. O spec pede 10 segundos de transferência; medir por
    /// tamanho fixo em vez de por tempo dá o mesmo resultado numa conexão
    /// doméstica e evita o caso ruim: numa conexão de 1 Gb/s, 10 segundos
    /// baixariam mais de 1 GB da franquia de alguém.
    /// </summary>
    private const int BytesDeDownload = 25 * 1024 * 1024;

    private const int BytesDeUpload = 8 * 1024 * 1024;

    private readonly ISettingsStore _configuracoes;
    private readonly IGameBoostLogger _log;

    public SpeedTest(ISettingsStore configuracoes, IGameBoostLogger log)
    {
        _configuracoes = configuracoes;
        _log = log;
    }

    public bool Permitido => _configuracoes.Load().PermitirAcessoARede;

    public async Task<ResultadoDeVelocidade> MedirAsync(IProgress<string>? progresso, CancellationToken ct)
    {
        if (!Permitido)
        {
            return new ResultadoDeVelocidade(0, 0, 0, 0, TimeSpan.Zero,
                "O teste de velocidade precisa de acesso à internet, e essa permissão está "
              + "desligada. Ligue em Configurações, se quiser.");
        }

        var relogio = Stopwatch.StartNew();

        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            http.DefaultRequestHeaders.UserAgent.ParseAdd("GameBoost/2.0");

            var downloads = new List<double>(Amostras);
            long bytesBaixados = 0;

            for (var i = 1; i <= Amostras; i++)
            {
                progresso?.Report($"Medindo download ({i} de {Amostras})");
                var (mbps, bytes) = await MedirDownloadAsync(http, ct);

                if (mbps > 0)
                {
                    downloads.Add(mbps);
                    bytesBaixados += bytes;
                }
            }

            var uploads = new List<double>(Amostras);
            long bytesEnviados = 0;

            for (var i = 1; i <= Amostras; i++)
            {
                progresso?.Report($"Medindo upload ({i} de {Amostras})");
                var (mbps, bytes) = await MedirUploadAsync(http, ct);

                if (mbps > 0)
                {
                    uploads.Add(mbps);
                    bytesEnviados += bytes;
                }
            }

            var download = Mediana(downloads);
            var upload = Mediana(uploads);

            relogio.Stop();

            _log.Info("network", "Velocidade", null,
                $"download {download:0.0} Mbps, upload {upload:0.0} Mbps");

            return new ResultadoDeVelocidade(
                Math.Round(download, 1), Math.Round(upload, 1),
                bytesBaixados, bytesEnviados, relogio.Elapsed, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            relogio.Stop();
            _log.Warn("network", "Velocidade", null, ex.Message);

            return new ResultadoDeVelocidade(0, 0, 0, 0, relogio.Elapsed,
                $"Não foi possível completar o teste: {ex.Message}");
        }
    }

    private static async Task<(double Mbps, long Bytes)> MedirDownloadAsync(HttpClient http, CancellationToken ct)
    {
        using var resposta = await http.GetAsync(
            UrlDownload + BytesDeDownload, HttpCompletionOption.ResponseHeadersRead, ct);

        resposta.EnsureSuccessStatusCode();

        await using var fluxo = await resposta.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[81920];
        long total = 0;

        // O cronômetro começa depois dos cabeçalhos: incluir o tempo de
        // handshake TLS na conta faria a velocidade parecer menor do que é.
        var relogio = Stopwatch.StartNew();

        int lidos;
        while ((lidos = await fluxo.ReadAsync(buffer, ct)) > 0)
            total += lidos;

        relogio.Stop();

        return (Mbps(total, relogio.Elapsed), total);
    }

    private static async Task<(double Mbps, long Bytes)> MedirUploadAsync(HttpClient http, CancellationToken ct)
    {
        var dados = new byte[BytesDeUpload];
        Random.Shared.NextBytes(dados);

        using var conteudo = new ByteArrayContent(dados);
        var relogio = Stopwatch.StartNew();

        using var resposta = await http.PostAsync(UrlUpload, conteudo, ct);
        relogio.Stop();

        if (!resposta.IsSuccessStatusCode)
            return (0, 0);

        return (Mbps(dados.Length, relogio.Elapsed), dados.Length);
    }

    private static double Mediana(List<double> valores)
    {
        if (valores.Count == 0)
            return 0;

        var ordenados = valores.OrderBy(v => v).ToList();
        var meio = ordenados.Count / 2;

        return ordenados.Count % 2 == 1
            ? ordenados[meio]
            : (ordenados[meio - 1] + ordenados[meio]) / 2;
    }

    private static double Mbps(long bytes, TimeSpan tempo)
        => tempo.TotalSeconds <= 0 ? 0 : bytes * 8 / tempo.TotalSeconds / 1_000_000;
}
