using System.Diagnostics;
using System.Net;
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
/// Teste de velocidade (seção 5.9.1).
///
/// Três coisas que ele **não** é, e que a tela precisa dizer:
///
/// - Não substitui um teste de banda dedicado. Ele usa um servidor só e uma
///   conexão só, enquanto o Speedtest escolhe o servidor mais próximo e abre
///   várias em paralelo. O número sai por volta de 10% a 15% abaixo.
///
///   Vale lembrar que o teste dedicado também erra quando o servidor escolhido
///   está congestionado: na mesma máquina e no mesmo minuto, um servidor deu
///   73 Mbps de upload e outro deu 264.
/// - Não mede a sua internet quando alguém está baixando algo em outra máquina.
/// - Não tem relação com ping. Banda e latência são coisas diferentes, e é a
///   latência que decide se o jogo está bom.
///
/// O que ele responde é uma pergunta específica: "a minha conexão está
/// entregando algo próximo do que eu contratei, ou está muito abaixo?".
///
/// **Só roda quando o usuário permite acesso à rede** (regra 8).
/// </summary>
public sealed class SpeedTest
{
    private const string UrlDownload = "https://speed.cloudflare.com/__down?bytes=";
    private const string UrlUpload = "https://speed.cloudflare.com/__up";

    /// <summary>Três amostras, mediana no fim (seção 5.9.1).</summary>
    private const int Amostras = 3;

    /// <summary>
    /// Duração de cada amostra.
    ///
    /// **Medir por tempo, não por tamanho.** A primeira versão transferia 25 MB
    /// e cronometrava, o que parecia equivalente e era mais previsível para a
    /// franquia de quem tem limite. Não é equivalente: numa fibra doméstica a
    /// operadora deixa passar bem acima do contratado por um ou dois segundos,
    /// e uma transferência que termina em 0,6 s mede **só essa rajada**.
    ///
    /// Na máquina de teste isso deu 380 Mbps de upload numa linha que entrega
    /// 73. Não era ruído: com 8, 25 e 50 MB o resultado saiu 131, 354 e 275
    /// Mbps — todos errados, e nenhum parecido com o outro.
    /// </summary>
    private static readonly TimeSpan DuracaoDaAmostra = TimeSpan.FromSeconds(8);

    /// <summary>
    /// O começo da transferência é descartado.
    ///
    /// É onde moram a rajada da operadora e o slow start do TCP, que ainda está
    /// descobrindo quanto a linha aguenta. O que interessa é a taxa sustentada,
    /// e ela só aparece depois.
    /// </summary>
    private static readonly TimeSpan Aquecimento = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Teto por amostra, para não comer a franquia de quem tem limite. Numa
    /// conexão de 500 Mbps, 8 segundos passariam de 500 MB.
    /// </summary>
    private const long TetoDeBytes = 120L * 1024 * 1024;

    /// <summary>Pedaço pedido ao servidor a cada rodada do laço de download.</summary>
    private const int BlocoDeDownload = 25 * 1024 * 1024;

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
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GameBoost/2.0");

            var downloads = new List<double>(Amostras);
            var uploads = new List<double>(Amostras);
            long bytesBaixados = 0;
            long bytesEnviados = 0;

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

            relogio.Stop();

            var download = Mediana(downloads);
            var upload = Mediana(uploads);

            _log.Info("network", "Velocidade", null,
                $"download {download:0.0} Mbps, upload {upload:0.0} Mbps, "
              + $"{bytesBaixados / 1024 / 1024} MB baixados, {bytesEnviados / 1024 / 1024} MB enviados");

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

    /// <summary>
    /// Baixa por <see cref="DuracaoDaAmostra"/> e conta só o que passou depois
    /// do aquecimento.
    /// </summary>
    private static async Task<(double Mbps, long Bytes)> MedirDownloadAsync(HttpClient http, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long totalTransferido = 0;
        long contados = 0;

        var relogio = Stopwatch.StartNew();
        TimeSpan? inicioDaContagem = null;

        while (relogio.Elapsed < DuracaoDaAmostra && totalTransferido < TetoDeBytes)
        {
            ct.ThrowIfCancellationRequested();

            using var resposta = await http.GetAsync(
                UrlDownload + BlocoDeDownload, HttpCompletionOption.ResponseHeadersRead, ct);

            resposta.EnsureSuccessStatusCode();

            await using var fluxo = await resposta.Content.ReadAsStreamAsync(ct);

            int lidos;
            while ((lidos = await fluxo.ReadAsync(buffer, ct)) > 0)
            {
                totalTransferido += lidos;

                if (relogio.Elapsed >= Aquecimento)
                {
                    inicioDaContagem ??= relogio.Elapsed;
                    contados += lidos;
                }

                if (relogio.Elapsed >= DuracaoDaAmostra || totalTransferido >= TetoDeBytes)
                    break;
            }
        }

        relogio.Stop();

        if (inicioDaContagem is null || contados == 0)
            return (0, totalTransferido);

        var janela = relogio.Elapsed - inicioDaContagem.Value;

        return janela.TotalSeconds <= 0
            ? (0, totalTransferido)
            : (contados * 8.0 / janela.TotalSeconds / 1_000_000, totalTransferido);
    }

    /// <summary>
    /// Envia por <see cref="DuracaoDaAmostra"/>, contando só depois do
    /// aquecimento.
    ///
    /// O corpo é escrito por um fluxo, não por um array pronto: assim dá para
    /// cronometrar **enquanto** os bytes saem. Com `ByteArrayContent` só dava
    /// para medir o POST inteiro, e era isso que fazia o resultado depender do
    /// tamanho escolhido.
    /// </summary>
    private static async Task<(double Mbps, long Bytes)> MedirUploadAsync(HttpClient http, CancellationToken ct)
    {
        var bloco = new byte[81920];
        Random.Shared.NextBytes(bloco);

        long totalEnviado = 0;
        long contados = 0;
        var relogio = Stopwatch.StartNew();
        TimeSpan? inicioDaContagem = null;

        using var conteudo = new ConteudoDeFluxo(async destino =>
        {
            while (relogio.Elapsed < DuracaoDaAmostra && totalEnviado < TetoDeBytes)
            {
                ct.ThrowIfCancellationRequested();

                await destino.WriteAsync(bloco, ct);
                await destino.FlushAsync(ct);

                totalEnviado += bloco.Length;

                if (relogio.Elapsed >= Aquecimento)
                {
                    inicioDaContagem ??= relogio.Elapsed;
                    contados += bloco.Length;
                }
            }
        });

        using var resposta = await http.PostAsync(UrlUpload, conteudo, ct);
        relogio.Stop();

        if (!resposta.IsSuccessStatusCode || inicioDaContagem is null || contados == 0)
            return (0, totalEnviado);

        var janela = relogio.Elapsed - inicioDaContagem.Value;

        return janela.TotalSeconds <= 0
            ? (0, totalEnviado)
            : (contados * 8.0 / janela.TotalSeconds / 1_000_000, totalEnviado);
    }

    /// <summary>
    /// Conteúdo HTTP que escreve direto no fluxo de saída, sem montar o corpo
    /// inteiro na memória antes. É o que permite cronometrar o envio enquanto
    /// ele acontece.
    /// </summary>
    private sealed class ConteudoDeFluxo : HttpContent
    {
        private readonly Func<Stream, Task> _escrever;

        public ConteudoDeFluxo(Func<Stream, Task> escrever)
        {
            _escrever = escrever;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => _escrever(stream);

        /// <summary>
        /// Comprimento desconhecido de propósito: o envio termina por tempo, não
        /// por tamanho. Sem Content-Length o .NET usa transferência em pedaços,
        /// que é exatamente o que se quer.
        /// </summary>
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    internal static double Mediana(List<double> valores)
    {
        if (valores.Count == 0)
            return 0;

        var ordenados = valores.OrderBy(v => v).ToList();
        var meio = ordenados.Count / 2;

        return ordenados.Count % 2 == 1
            ? ordenados[meio]
            : (ordenados[meio - 1] + ordenados[meio]) / 2;
    }
}
