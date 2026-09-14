using System.Net;
using System.Net.Sockets;

namespace GameBoost.Core.Modules.Network;

/// <summary>O que um servidor STUN respondeu: como o mundo vê esta máquina.</summary>
public sealed record RespostaStun(IPAddress EnderecoPublico, int PortaPublica, IPEndPoint Local);

/// <summary>
/// Cliente STUN mínimo (RFC 5389), só o Binding Request.
///
/// É o que permite responder à pergunta que importa para jogo online: **qual é
/// o meu endereço visto de fora, e a porta que eu abri sai com o mesmo número?**
/// Sem isso não dá para distinguir um NAT que deixa o jogo receber conexão de
/// um que não deixa — e é essa diferença que decide se o jogador consegue
/// hospedar partida, entrar em lobby e falar por voz.
///
/// A implementação é propositalmente curta: só o suficiente para mandar um
/// Binding Request e ler o XOR-MAPPED-ADDRESS da resposta. Uma biblioteca de
/// STUN completa traria ICE, TURN e autenticação, nada disso usado aqui.
/// </summary>
public static class StunClient
{
    private const int TamanhoCabecalho = 20;
    private const uint MagicCookie = 0x2112A442;

    private const ushort BindingRequest = 0x0001;
    private const ushort BindingResponse = 0x0101;

    private const ushort AtributoMappedAddress = 0x0001;
    private const ushort AtributoXorMappedAddress = 0x0020;

    /// <summary>
    /// Servidores usados na detecção. São dois de propósito: comparar a porta
    /// devolvida por servidores diferentes é o que separa NAT de cone
    /// (mesma porta para todos) de NAT simétrico (porta nova a cada destino),
    /// que é o tipo que quebra jogo peer-to-peer.
    /// </summary>
    public static readonly (string Host, int Porta)[] Servidores =
    {
        ("stun.l.google.com", 19302),
        ("stun1.l.google.com", 19302),
        ("stun.cloudflare.com", 3478)
    };

    /// <summary>
    /// Pergunta a um servidor STUN como esta máquina aparece de fora.
    /// <paramref name="portaLocal"/> zero deixa o sistema escolher.
    /// </summary>
    public static async Task<RespostaStun?> ConsultarAsync(
        string host, int porta, int portaLocal, CancellationToken ct)
    {
        using var socket = new UdpClient(portaLocal);

        try
        {
            var enderecos = await Dns.GetHostAddressesAsync(host, ct);
            var destino = enderecos.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

            if (destino is null)
                return null;

            var transacao = new byte[12];
            Random.Shared.NextBytes(transacao);

            var pedido = MontarPedido(transacao);
            await socket.SendAsync(pedido, pedido.Length, new IPEndPoint(destino, porta));

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(3));

            var resposta = await socket.ReceiveAsync(limite.Token);
            var local = (IPEndPoint)socket.Client.LocalEndPoint!;

            return Interpretar(resposta.Buffer, transacao, local);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        {
            return null;
        }
    }

    private static byte[] MontarPedido(byte[] transacao)
    {
        var pacote = new byte[TamanhoCabecalho];

        pacote[0] = (byte)(BindingRequest >> 8);
        pacote[1] = (byte)(BindingRequest & 0xFF);

        // Comprimento zero: o Binding Request não leva atributo nenhum.
        pacote[2] = 0;
        pacote[3] = 0;

        pacote[4] = 0x21;
        pacote[5] = 0x12;
        pacote[6] = 0xA4;
        pacote[7] = 0x42;

        transacao.CopyTo(pacote, 8);
        return pacote;
    }

    private static RespostaStun? Interpretar(byte[] buffer, byte[] transacao, IPEndPoint local)
    {
        if (buffer.Length < TamanhoCabecalho)
            return null;

        var tipo = (ushort)((buffer[0] << 8) | buffer[1]);

        if (tipo != BindingResponse)
            return null;

        // A resposta tem que carregar o mesmo id de transação, senão é resposta
        // de outra pergunta — ou de quem quis se passar pelo servidor.
        for (var i = 0; i < 12; i++)
        {
            if (buffer[8 + i] != transacao[i])
                return null;
        }

        var comprimento = (ushort)((buffer[2] << 8) | buffer[3]);
        var fim = Math.Min(TamanhoCabecalho + comprimento, buffer.Length);
        var i2 = TamanhoCabecalho;

        while (i2 + 4 <= fim)
        {
            var atributo = (ushort)((buffer[i2] << 8) | buffer[i2 + 1]);
            var tamanho = (ushort)((buffer[i2 + 2] << 8) | buffer[i2 + 3]);
            var dados = i2 + 4;

            if (dados + tamanho > buffer.Length)
                break;

            if (atributo is AtributoXorMappedAddress or AtributoMappedAddress && tamanho >= 8)
            {
                var familia = buffer[dados + 1];

                // Só IPv4: o que importa aqui é o NAT, e NAT é coisa de IPv4.
                if (familia == 0x01)
                {
                    var xor = atributo == AtributoXorMappedAddress;

                    var porta = (buffer[dados + 2] << 8) | buffer[dados + 3];
                    var endereco = new byte[4];
                    Array.Copy(buffer, dados + 4, endereco, 0, 4);

                    if (xor)
                    {
                        // O XOR com o magic cookie existe para o pacote não
                        // trombar com middleboxes que reescrevem endereços que
                        // reconhecem no corpo da mensagem.
                        porta ^= (int)(MagicCookie >> 16);

                        endereco[0] ^= 0x21;
                        endereco[1] ^= 0x12;
                        endereco[2] ^= 0xA4;
                        endereco[3] ^= 0x42;
                    }

                    return new RespostaStun(new IPAddress(endereco), porta & 0xFFFF, local);
                }
            }

            // Atributos são alinhados em 4 bytes.
            i2 = dados + ((tamanho + 3) & ~3);
        }

        return null;
    }
}
