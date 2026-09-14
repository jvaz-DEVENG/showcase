using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using GameBoost.Core.Logging;

namespace GameBoost.Core.Modules.Network;

/// <summary>
/// Resultado de uma medição de latência. <see cref="Respondeu"/> falso não
/// significa rede ruim: muito host ignora ICMP de propósito.
/// </summary>
public sealed record MedicaoPing(
    string Rotulo,
    string Alvo,
    bool Respondeu,
    double MediaMs,
    double JitterMs,
    double PiorMs,
    int PerdaPercentual,
    string? Erro)
{
    /// <summary>
    /// Semáforo da seção 5.9.3: abaixo de 30 ms verde, até 80 amarelo, acima
    /// vermelho.
    /// </summary>
    public string Semaforo => !Respondeu
        ? "cinza"
        : MediaMs switch
        {
            < 30 => "verde",
            <= 80 => "amarelo",
            _ => "vermelho"
        };

    /// <summary>Texto do que o número significa para quem joga.</summary>
    public string Leitura => !Respondeu
        ? "sem resposta"
        : MediaMs switch
        {
            < 30 => "ótimo",
            <= 80 => "aceitável",
            _ => "alto para jogo competitivo"
        };

    /// <summary>Jitter acima de 15 ms ou perda acima de 1% viram Finding (5.9.1).</summary>
    public bool EhProblema => Respondeu && (JitterMs > 15 || PerdaPercentual > 1);
}

/// <summary>Tempo de resposta de um resolvedor de DNS.</summary>
public sealed record MedicaoDns(string Nome, string Endereco, bool Respondeu, double MediaMs, bool EmUso);

/// <summary>Estado do Wi-Fi, quando a conexão é sem fio.</summary>
public sealed record EstadoWifi(string Ssid, int SinalPercentual, string Banda, int Canal, string Padrao);

/// <summary>
/// Diagnóstico de rede da seção 5.9.
///
/// Tudo aqui é leitura: nada nesta classe altera configuração. As ações ficam
/// no <see cref="NetworkModule"/>, onde passam pelo ChangeRecord.
/// </summary>
public sealed class NetworkDiagnostics
{
    /// <summary>
    /// Quantos pacotes por alvo. Cinco é o suficiente para ter jitter com
    /// algum significado e ainda terminar rápido; um único ping não diz nada
    /// sobre estabilidade, que é o que importa em jogo.
    /// </summary>
    private const int Pacotes = 5;

    private const int TimeoutMs = 1500;

    /// <summary>Resolvedores públicos comparados com o que está em uso.</summary>
    public static readonly (string Nome, string Endereco)[] ResolvedoresPublicos =
    {
        ("Cloudflare", "1.1.1.1"),
        ("Google", "8.8.8.8"),
        ("Quad9", "9.9.9.9")
    };

    private readonly IGameBoostLogger _log;

    public NetworkDiagnostics(IGameBoostLogger log)
    {
        _log = log;
    }

    // ==================================================================
    // Latência
    // ==================================================================

    public async Task<IReadOnlyList<MedicaoPing>> MedirLatenciaAsync(CancellationToken ct)
    {
        var alvos = new List<(string Rotulo, string Alvo)>();

        var gateway = Gateway();
        if (gateway is not null)
            alvos.Add(("Seu roteador", gateway));

        alvos.Add(("Internet (Cloudflare)", "1.1.1.1"));
        alvos.Add(("Internet (Google)", "8.8.8.8"));

        var resultados = new List<MedicaoPing>(alvos.Count);

        foreach (var (rotulo, alvo) in alvos)
        {
            ct.ThrowIfCancellationRequested();
            resultados.Add(await MedirUmAsync(rotulo, alvo, ct));
        }

        return resultados;
    }

    private async Task<MedicaoPing> MedirUmAsync(string rotulo, string alvo, CancellationToken ct)
    {
        var tempos = new List<double>(Pacotes);
        var perdidos = 0;
        string? erro = null;

        using var ping = new Ping();

        for (var i = 0; i < Pacotes; i++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var resposta = await ping.SendPingAsync(alvo, TimeoutMs);

                if (resposta.Status == IPStatus.Success)
                    tempos.Add(resposta.RoundtripTime);
                else
                    perdidos++;
            }
            catch (Exception ex) when (ex is PingException or SocketException)
            {
                perdidos++;
                erro = ex.Message;
            }
        }

        if (tempos.Count == 0)
            return new MedicaoPing(rotulo, alvo, false, 0, 0, 0, 100, erro);

        var media = tempos.Average();

        // Jitter é a variação entre pacotes consecutivos, não o desvio padrão:
        // é a medida que corresponde ao que o jogador sente como "travadinha".
        var jitter = tempos.Count > 1
            ? Enumerable.Range(1, tempos.Count - 1)
                        .Select(i => Math.Abs(tempos[i] - tempos[i - 1]))
                        .Average()
            : 0;

        return new MedicaoPing(
            rotulo, alvo, true,
            Math.Round(media, 1),
            Math.Round(jitter, 1),
            tempos.Max(),
            perdidos * 100 / Pacotes,
            erro);
    }

    public static string? Gateway()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address)
            .FirstOrDefault(a => a is not null
                              && a.AddressFamily == AddressFamily.InterNetwork
                              && !a.Equals(IPAddress.Any))
            ?.ToString();

    // ==================================================================
    // DNS
    // ==================================================================

    /// <summary>
    /// Compara o tempo de resolução do DNS em uso com os três públicos.
    ///
    /// A comparação usa consulta UDP direta a cada servidor, montada à mão.
    /// `Dns.GetHostEntry` não serviria: ele pergunta ao resolvedor do sistema,
    /// então mediria sempre o mesmo servidor com três nomes diferentes.
    /// </summary>
    public async Task<IReadOnlyList<MedicaoDns>> CompararDnsAsync(CancellationToken ct)
    {
        var emUso = DnsEmUso();
        var resultados = new List<MedicaoDns>();

        foreach (var endereco in emUso.Take(1))
        {
            ct.ThrowIfCancellationRequested();
            var ms = await MedirDnsAsync(endereco, ct);
            resultados.Add(new MedicaoDns("Em uso agora", endereco, ms >= 0, ms, true));
        }

        foreach (var (nome, endereco) in ResolvedoresPublicos)
        {
            ct.ThrowIfCancellationRequested();

            // Não repetir na tabela o que já está em uso.
            if (emUso.Contains(endereco, StringComparer.Ordinal))
                continue;

            var ms = await MedirDnsAsync(endereco, ct);
            resultados.Add(new MedicaoDns(nome, endereco, ms >= 0, ms, false));
        }

        return resultados;
    }

    public static IReadOnlyList<string> DnsEmUso()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                     && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().DnsAddresses)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Média de três consultas. Devolve -1 quando o servidor não responde.</summary>
    private async Task<double> MedirDnsAsync(string servidor, CancellationToken ct)
    {
        // Nomes diferentes a cada rodada evitam medir o cache do servidor em
        // vez do servidor.
        string[] nomes = { "example.com", "wikipedia.org", "github.com" };
        var tempos = new List<double>(nomes.Length);

        foreach (var nome in nomes)
        {
            ct.ThrowIfCancellationRequested();

            var ms = await ConsultarAsync(servidor, nome, ct);
            if (ms >= 0)
                tempos.Add(ms);
        }

        return tempos.Count == 0 ? -1 : Math.Round(tempos.Average(), 1);
    }

    /// <summary>
    /// Uma consulta A por UDP, montada na mão. O pacote de pergunta do DNS é
    /// pequeno o bastante para não valer uma dependência.
    /// </summary>
    private async Task<double> ConsultarAsync(string servidor, string nome, CancellationToken ct)
    {
        try
        {
            using var cliente = new UdpClient();
            cliente.Client.ReceiveTimeout = TimeoutMs;

            var pergunta = MontarConsulta(nome);
            var destino = new IPEndPoint(IPAddress.Parse(servidor), 53);

            var relogio = Stopwatch.StartNew();

            await cliente.SendAsync(pergunta, pergunta.Length, destino);

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeoutMs);

            var resposta = await cliente.ReceiveAsync(limite.Token);
            relogio.Stop();

            // Confere se a resposta é da pergunta que fizemos: os dois primeiros
            // bytes são o id da transação.
            if (resposta.Buffer.Length < 2
                || resposta.Buffer[0] != pergunta[0]
                || resposta.Buffer[1] != pergunta[1])
            {
                return -1;
            }

            return relogio.Elapsed.TotalMilliseconds;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or FormatException)
        {
            return -1;
        }
    }

    private static byte[] MontarConsulta(string nome)
    {
        var partes = nome.Split('.');
        var tamanho = 12 + partes.Sum(p => p.Length + 1) + 1 + 4;
        var pacote = new byte[tamanho];

        // Id aleatório, para casar pergunta e resposta.
        var id = Random.Shared.Next(1, ushort.MaxValue);
        pacote[0] = (byte)(id >> 8);
        pacote[1] = (byte)(id & 0xFF);

        // Flags: consulta padrão, recursão desejada.
        pacote[2] = 0x01;

        // Uma pergunta.
        pacote[5] = 0x01;

        var i = 12;

        foreach (var parte in partes)
        {
            pacote[i++] = (byte)parte.Length;

            foreach (var c in parte)
                pacote[i++] = (byte)c;
        }

        pacote[i++] = 0;      // fim do nome
        pacote[i++] = 0;
        pacote[i++] = 1;      // tipo A
        pacote[i++] = 0;
        pacote[i] = 1;        // classe IN

        return pacote;
    }

    // ==================================================================
    // Wi-Fi
    // ==================================================================

    /// <summary>
    /// Dados do Wi-Fi, ou null em conexão por cabo.
    ///
    /// O que interessa aqui é dizer ao jogador se ele está em 2,4 GHz, que
    /// divide espaço com micro-ondas e com o Wi-Fi do vizinho, ou em 5/6 GHz.
    /// </summary>
    public EstadoWifi? LerWifi()
    {
        var adaptador = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                              && n.OperationalStatus == OperationalStatus.Up);

        if (adaptador is null)
            return null;

        try
        {
            return Native.WlanInfo.Ler(adaptador.Id);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            _log.Warn("network", "Wifi", adaptador.Name, $"não foi possível ler: {ex.Message}");
            return null;
        }
    }

    public static bool ConectadoPorCabo()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                   && n.OperationalStatus == OperationalStatus.Up
                   && n.GetIPProperties().GatewayAddresses.Count > 0);
}
