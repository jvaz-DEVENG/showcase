using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;

namespace GameBoost.Core.Modules.Network;

/// <summary>
/// Tipo de NAT no vocabulário que o jogador já conhece.
///
/// Xbox, Call of Duty e Discord dizem Aberto, Moderado e Estrito, e é isso que o
/// usuário vê na tela do jogo quando vem procurar ajuda. Inventar nomes próprios
/// ("cone restrito", "simétrico") forçaria ele a traduzir sozinho a informação
/// que veio buscar aqui.
///
/// CGNAT e NAT duplo não são tipos de NAT: são **causas** que produzem um NAT
/// estrito. Estão no mesmo enum porque a tela precisa de uma resposta só, e
/// porque a correção de cada um é completamente diferente — dizer "estrito"
/// para quem está atrás de CGNAT o faria passar a tarde mexendo no roteador à
/// toa.
/// </summary>
public enum TipoDeNat
{
    NaoDetectado,

    /// <summary>Endereço público direto na máquina. O melhor caso possível.</summary>
    SemNat,

    /// <summary>Mapeamento estável e recebe de qualquer origem. "Open" nos jogos.</summary>
    Aberto,

    /// <summary>Mapeamento estável, mas só recebe de quem já falou. "Moderate".</summary>
    Moderado,

    /// <summary>O mapeamento muda por destino. "Strict": é o que trava lobby e voz.</summary>
    Estrito,

    /// <summary>O provedor compartilha um IP público entre vários assinantes.</summary>
    Cgnat,

    /// <summary>Dois roteadores em série fazendo NAT.</summary>
    Duplo
}

public enum EstadoTeredo
{
    Desconhecido,
    Desativado,
    Qualificado,
    NaoQualificado
}

/// <summary>Resultado completo do diagnóstico de conectividade.</summary>
public sealed record DiagnosticoDeNat
{
    public required TipoDeNat Tipo { get; init; }
    public IPAddress? EnderecoPublico { get; init; }
    public IPAddress? EnderecoLocal { get; init; }

    /// <summary>Quantos saltos privados existem antes do primeiro público.</summary>
    public int SaltosPrivados { get; init; }

    public bool PortaEstavel { get; init; }
    public EstadoTeredo Teredo { get; init; }
    public bool UpnpDisponivel { get; init; }
    public string? ModeloDoRoteador { get; init; }
    public IReadOnlyList<string> PerfisDeFirewallAtivos { get; init; } = Array.Empty<string>();

    /// <summary>O que isso significa para quem joga, em português.</summary>
    public string Explicacao => Tipo switch
    {
        TipoDeNat.SemNat =>
            "Sua máquina tem endereço público direto. É o melhor caso: qualquer jogo consegue "
          + "hospedar partida e receber conexão.",

        TipoDeNat.Aberto =>
            "NAT aberto. A porta que o jogo abre sai com o mesmo número para todos os destinos, "
          + "que é o que os jogos chamam de NAT tipo 1 ou 2. Chat de voz e partida hospedada "
          + "funcionam.",

        TipoDeNat.Moderado =>
            "NAT moderado. Seu roteador mantém a mesma porta para todo mundo, mas só aceita "
          + "resposta de quem você contatou primeiro. Dá para jogar e entrar em partida; o que "
          + "costuma falhar é hospedar e entrar em festa de quem também está em NAT moderado ou "
          + "estrito. Ligar UPnP no roteador resolve a maior parte dos casos.",

        TipoDeNat.Estrito =>
            "NAT estrito. Seu roteador dá uma porta diferente para cada destino, e por isso "
          + "outro jogador não consegue te alcançar de volta. É o tipo 3 dos jogos: lobby demora, "
          + "chat de voz falha e partida hospedada não abre. Duas pessoas em NAT estrito não "
          + "conseguem se conectar de jeito nenhum. Costuma ser resolvido ligando UPnP no "
          + "roteador ou abrindo as portas do jogo à mão.",

        TipoDeNat.Cgnat =>
            "CGNAT: sua operadora divide um mesmo endereço público entre vários assinantes. "
          + "Nenhum ajuste no seu roteador resolve isso, porque o NAT que atrapalha está na "
          + "operadora, não na sua casa. As saídas são pedir IP público à operadora (às vezes "
          + "gratuito, às vezes pago) ou usar IPv6, quando o jogo suporta.",

        TipoDeNat.Duplo =>
            "NAT duplo: há dois roteadores em série fazendo tradução, normalmente o modem da "
          + "operadora mais um roteador seu. Abrir porta só no roteador de dentro não resolve, "
          + "porque o de fora continua barrando. A correção é pôr o modem da operadora em modo "
          + "bridge, ou apontar a DMZ dele para o seu roteador.",

        _ => "Não foi possível determinar o tipo de NAT. Pode ser firewall bloqueando UDP de saída."
    };

    /// <summary>
    /// Cor do semáforo grande da tela: verde joga, amarelo joga com atrito,
    /// vermelho não entra em partida.
    /// </summary>
    public string Semaforo => Tipo switch
    {
        TipoDeNat.SemNat or TipoDeNat.Aberto => "verde",
        TipoDeNat.Moderado => "amarelo",
        TipoDeNat.Estrito or TipoDeNat.Cgnat or TipoDeNat.Duplo => "vermelho",
        _ => "cinza"
    };

    /// <summary>Nome curto, do jeito que console e jogo usam.</summary>
    public string NomeCurto => Tipo switch
    {
        TipoDeNat.SemNat => "aberto (IP público direto)",
        TipoDeNat.Aberto => "aberto",
        TipoDeNat.Moderado => "moderado",
        TipoDeNat.Estrito => "estrito",
        TipoDeNat.Cgnat => "CGNAT da operadora",
        TipoDeNat.Duplo => "NAT duplo",
        _ => "não detectado"
    };
}

/// <summary>
/// Detecção de NAT e conectividade (seção 5.9).
///
/// Esta é a parte da tela de Rede que resolve uma reclamação concreta: "meu
/// jogo diz NAT estrito e eu não sei o que fazer". O valor não está em mudar
/// nada — está em dizer, em dez segundos, **onde** está o problema, porque a
/// correção é diferente para cada causa e o usuário normalmente mexe no lugar
/// errado.
///
/// Nada aqui altera configuração. As correções que existem estão no
/// <see cref="NetworkModule"/> e passam por ChangeRecord.
/// </summary>
public sealed class NatDiagnostics
{
    /// <summary>Faixa 100.64.0.0/10, reservada justamente para CGNAT (RFC 6598).</summary>
    private static bool EhCgnat(IPAddress endereco)
    {
        var b = endereco.GetAddressBytes();
        return b.Length == 4 && b[0] == 100 && b[1] >= 64 && b[1] <= 127;
    }

    private static bool EhPrivado(IPAddress endereco)
    {
        var b = endereco.GetAddressBytes();

        if (b.Length != 4)
            return false;

        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || EhCgnat(endereco);
    }

    private readonly IRegistryService _registro;
    private readonly IGameBoostLogger _log;

    public NatDiagnostics(IRegistryService registro, IGameBoostLogger log)
    {
        _registro = registro;
        _log = log;
    }

    public async Task<DiagnosticoDeNat> DiagnosticarAsync(CancellationToken ct)
    {
        // Quatro consultas: dois servidores por duas portas locais.
        //
        // As duas primeiras, da MESMA porta local para servidores diferentes,
        // respondem se o mapeamento é estável — se a porta pública muda com o
        // destino, o NAT é estrito e acabou a conversa.
        //
        // A terceira e a quarta, de OUTRA porta local, respondem se o roteador
        // preserva o número da porta. Um NAT que devolve a mesma porta de origem
        // costuma ser o de cone cheio; um que renumera, mas mantém o mapeamento
        // por destino, é o moderado.
        var portaA = PortaLivre();
        var portaB = PortaLivre();

        var a1 = await StunClient.ConsultarAsync(
            StunClient.Servidores[0].Host, StunClient.Servidores[0].Porta, portaA, ct);

        RespostaStun? a2 = null;
        RespostaStun? b1 = null;

        if (a1 is not null)
        {
            a2 = await StunClient.ConsultarAsync(
                StunClient.Servidores[1].Host, StunClient.Servidores[1].Porta, portaA, ct);

            b1 = await StunClient.ConsultarAsync(
                StunClient.Servidores[0].Host, StunClient.Servidores[0].Porta, portaB, ct);
        }

        var saltos = await ContarSaltosPrivadosAsync(ct);
        var local = EnderecoLocal();

        var estavel = a1 is not null && a2 is not null && a1.PortaPublica == a2.PortaPublica;
        var preservaPorta = a1 is not null && b1 is not null
                            && a1.PortaPublica == portaA && b1.PortaPublica == portaB;

        var tipo = Classificar(a1, estavel, preservaPorta, saltos, local);
        var primeira = a1;

        return new DiagnosticoDeNat
        {
            Tipo = tipo,
            EnderecoPublico = primeira?.EnderecoPublico,
            EnderecoLocal = local,
            SaltosPrivados = saltos,
            PortaEstavel = estavel,
            Teredo = LerTeredo(),
            UpnpDisponivel = await UpnpDisponivelAsync(ct),
            PerfisDeFirewallAtivos = LerFirewall()
        };
    }

    /// <summary>
    /// A classificação completa da RFC 5780 exigiria o atributo CHANGE-REQUEST,
    /// que faz o servidor responder de outro IP e de outra porta. Quase nenhum
    /// servidor STUN público ainda atende a isso — os da Google e da Cloudflare
    /// não atendem. Sem ele não dá para separar cone cheio de cone restrito com
    /// certeza.
    ///
    /// O que dá para afirmar com duas portas locais e dois servidores é o que
    /// importa na prática, e é o que está aqui: se o mapeamento muda por
    /// destino (estrito) e se o roteador preserva o número da porta de origem
    /// (sinal forte de cone cheio, o "aberto" dos jogos). O caso do meio vira
    /// moderado, que é a resposta correta na dúvida — errar para moderado faz o
    /// usuário ligar UPnP à toa; errar para aberto o faria parar de procurar o
    /// problema que ele tem.
    /// </summary>
    private static TipoDeNat Classificar(
        RespostaStun? primeira, bool mapeamentoEstavel, bool preservaPorta,
        int saltosPrivados, IPAddress? local)
    {
        if (primeira is null)
            return TipoDeNat.NaoDetectado;

        // CGNAT ganha de tudo: se o endereço "público" está na faixa reservada
        // para operadora, nada que o usuário faça em casa muda o resultado, e
        // dizer "abra a porta no roteador" seria mandá-lo perder a tarde.
        if (EhCgnat(primeira.EnderecoPublico))
            return TipoDeNat.Cgnat;

        if (local is not null && primeira.EnderecoPublico.Equals(local))
            return TipoDeNat.SemNat;

        // Dois ou mais saltos privados antes do primeiro público: há um
        // roteador atrás do outro.
        if (saltosPrivados >= 2)
            return TipoDeNat.Duplo;

        if (!mapeamentoEstavel)
            return TipoDeNat.Estrito;

        return preservaPorta ? TipoDeNat.Aberto : TipoDeNat.Moderado;
    }

    private static int PortaLivre()
    {
        using var socket = new UdpClient(0);
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    private static IPAddress? EnderecoLocal()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                     && n.GetIPProperties().GatewayAddresses.Count > 0)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(u => u.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

    /// <summary>
    /// Conta quantos saltos com endereço privado existem antes do primeiro
    /// público, usando ping com TTL crescente.
    ///
    /// Para no quinto salto: NAT duplo, quando existe, aparece nos dois
    /// primeiros. Ir mais longe só gastaria segundos que o usuário está
    /// esperando na tela.
    /// </summary>
    private async Task<int> ContarSaltosPrivadosAsync(CancellationToken ct)
    {
        var privados = 0;

        try
        {
            using var ping = new Ping();

            for (var ttl = 1; ttl <= 5; ttl++)
            {
                ct.ThrowIfCancellationRequested();

                var opcoes = new PingOptions(ttl, true);
                var resposta = await ping.SendPingAsync("1.1.1.1", 1200, new byte[32], opcoes);

                if (resposta.Address is null || resposta.Address.Equals(IPAddress.Any))
                    continue;

                if (EhPrivado(resposta.Address))
                {
                    privados++;
                    continue;
                }

                // Primeiro salto público: daqui para frente é a internet.
                break;
            }
        }
        catch (Exception ex) when (ex is PingException or SocketException or NotSupportedException)
        {
            _log.Warn("network", "Saltos", null, ex.Message);
        }

        return privados;
    }

    /// <summary>
    /// Estado do Teredo, lido do registro.
    ///
    /// Teredo é o túnel de IPv6 sobre IPv4 que o multiplayer de Xbox e Game
    /// Pass no PC usa para achar outros jogadores. Metade dos guias de
    /// "otimização de rede" da internet manda desativar, e é justamente isso
    /// que quebra o multiplayer de quem joga Game Pass.
    /// </summary>
    internal EstadoTeredo LerTeredo()
    {
        const string chave = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters";

        // DisabledComponents com o bit 0x01 ligado desativa todos os túneis,
        // inclusive o Teredo.
        if (_registro.GetValue(RegistryRoot.LocalMachine, chave, "DisabledComponents") is int componentes
            && (componentes & 0x01) != 0)
        {
            return EstadoTeredo.Desativado;
        }

        var tipo = _registro.GetValue(RegistryRoot.LocalMachine,
            @"SYSTEM\CurrentControlSet\Services\iphlpsvc\Teredo", "Type");

        // 0 e 1 são "disabled"; ausente significa o padrão do Windows, que é
        // cliente quando há necessidade.
        return tipo switch
        {
            int t and (0 or 1) => EstadoTeredo.Desativado,
            null => EstadoTeredo.Qualificado,
            _ => EstadoTeredo.Qualificado
        };
    }

    /// <summary>
    /// Procura um roteador que fale UPnP, por SSDP multicast.
    ///
    /// Só descoberta: o GameBoost não cria mapeamento de porta. Abrir porta no
    /// roteador por conta própria seria mexer em equipamento que não é dele,
    /// sem ter como desfazer se a máquina sumir da rede.
    /// </summary>
    internal async Task<bool> UpnpDisponivelAsync(CancellationToken ct)
    {
        const string busca =
            "M-SEARCH * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "MAN: \"ssdp:discover\"\r\n" +
            "MX: 2\r\n" +
            "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";

        try
        {
            using var socket = new UdpClient(0);
            var dados = System.Text.Encoding.ASCII.GetBytes(busca);
            var destino = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);

            await socket.SendAsync(dados, dados.Length, destino);

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(3));

            var resposta = await socket.ReceiveAsync(limite.Token);
            var texto = System.Text.Encoding.ASCII.GetString(resposta.Buffer);

            return texto.Contains("InternetGatewayDevice", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Perfis de firewall ligados, lidos do registro.
    ///
    /// Isto é informativo: o firewall ligado é o certo, e um bloqueio de jogo
    /// específico é regra, não perfil. O GameBoost não desliga firewall — essa
    /// é a "otimização" que mais aparece em tutorial de YouTube e a que mais
    /// custa caro.
    /// </summary>
    internal IReadOnlyList<string> LerFirewall()
    {
        const string raiz = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";

        var perfis = new (string Chave, string Nome)[]
        {
            ("DomainProfile", "Domínio"),
            ("StandardProfile", "Rede privada"),
            ("PublicProfile", "Rede pública")
        };

        var ativos = new List<string>();

        foreach (var (chave, nome) in perfis)
        {
            var valor = _registro.GetValue(RegistryRoot.LocalMachine, $@"{raiz}\{chave}", "EnableFirewall");

            // Ausente significa ligado: é o padrão do Windows.
            if (valor is null or int and not 0)
                ativos.Add(nome);
        }

        return ativos;
    }
}
