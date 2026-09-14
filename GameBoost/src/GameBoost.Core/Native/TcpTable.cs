using System.Net;
using System.Runtime.InteropServices;

namespace GameBoost.Core.Native;

public sealed record ConexaoTcp(int ProcessId, IPAddress Local, int PortaLocal, IPAddress Remoto, int PortaRemota, string Estado);

/// <summary>
/// Lê a tabela de conexões TCP com o PID de cada dono, via
/// <c>GetExtendedTcpTable</c>.
///
/// Uma ressalva que a UI precisa repetir: isto mostra **conexões**, não banda.
/// Medir quantos bytes por segundo cada processo usa exigiria ETW, que é um
/// consumidor de eventos rodando o tempo todo, e a regra 9 do spec põe teto de
/// 1,5% de CPU na coleta. Contar conexão custa uma chamada e responde à
/// pergunta que o usuário realmente faz: "quem está falando com a internet
/// agora?".
/// </summary>
public static class TcpTable
{
    private const int AF_INET = 2;

    /// <summary>TCP_TABLE_OWNER_PID_ALL.</summary>
    private const int TableClass = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public uint localPort;
        public uint remoteAddr;
        public uint remotePort;
        public uint owningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int TableClass, int Reserved);

    public static IReadOnlyList<ConexaoTcp> Listar()
    {
        var tamanho = 0;
        var buffer = IntPtr.Zero;

        try
        {
            // Primeira chamada só para descobrir o tamanho. Entre ela e a
            // segunda a tabela pode crescer, e é por isso que o laço existe.
            for (var tentativa = 0; tentativa < 5; tentativa++)
            {
                var resultado = GetExtendedTcpTable(buffer, ref tamanho, false, AF_INET, TableClass, 0);

                if (resultado == 0)
                    break;

                // ERROR_INSUFFICIENT_BUFFER: aloca e tenta de novo.
                if (resultado != 122)
                    return Array.Empty<ConexaoTcp>();

                if (buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(buffer);

                buffer = Marshal.AllocHGlobal(tamanho);
            }

            if (buffer == IntPtr.Zero)
                return Array.Empty<ConexaoTcp>();

            var quantidade = Marshal.ReadInt32(buffer);
            var linhas = new List<ConexaoTcp>(quantidade);
            var tamanhoLinha = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (var i = 0; i < quantidade; i++)
            {
                var endereco = IntPtr.Add(buffer, 4 + i * tamanhoLinha);
                var linha = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(endereco);

                linhas.Add(new ConexaoTcp(
                    (int)linha.owningPid,
                    new IPAddress(linha.localAddr),
                    Porta(linha.localPort),
                    new IPAddress(linha.remoteAddr),
                    Porta(linha.remotePort),
                    Estado(linha.state)));
            }

            return linhas;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>A porta vem em ordem de rede nos dois bytes baixos.</summary>
    private static int Porta(uint valor) => ((int)(valor & 0xFF) << 8) | (int)((valor >> 8) & 0xFF);

    private static string Estado(uint estado) => estado switch
    {
        1 => "fechada",
        2 => "escutando",
        3 => "SYN enviado",
        4 => "SYN recebido",
        5 => "conectada",
        6 => "encerrando",
        7 => "aguardando",
        8 => "fechando",
        9 => "fechada (espera)",
        10 => "último ACK",
        11 => "espera final",
        12 => "excluída",
        _ => "desconhecida"
    };
}
