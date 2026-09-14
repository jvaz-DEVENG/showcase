using System.Runtime.InteropServices;

namespace GameBoost.Core.Native;

/// <summary>Dados brutos de um processo, como o kernel devolve.</summary>
internal readonly record struct RawProcess(
    int Pid,
    string Nome,
    long TempoDeCpu100ns,
    long WorkingSetBytes,
    int SessionId,
    int Threads);

/// <summary>
/// Lista todos os processos numa unica chamada
/// (NtQuerySystemInformation / SystemProcessInformation).
///
/// Existe por causa do orcamento de CPU da secao 5.5: Process.GetProcesses()
/// seguido de TotalProcessorTime e WorkingSet64 abre um handle por processo e
/// custou 6% de CPU medidos numa maquina com ~400 processos, contra o limite de
/// 1,5%. Esta chamada devolve tudo de uma vez, sem abrir handle nenhum.
///
/// O layout de SYSTEM_PROCESS_INFORMATION e estavel desde o Windows Vista.
/// Ainda assim, nada aqui confia cegamente no buffer: cada avanco e validado
/// antes de ler, e qualquer inconsistencia encerra a leitura em vez de andar
/// para fora da regiao alocada.
/// </summary>
internal static class ProcessInformation
{
    private const int SystemProcessInformation = 5;
    private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

    // Deslocamentos dentro de SYSTEM_PROCESS_INFORMATION em x64.
    private const int OffsetNextEntry = 0x00;   // ULONG
    private const int OffsetThreads = 0x04;     // ULONG
    private const int OffsetUserTime = 0x28;    // LARGE_INTEGER
    private const int OffsetKernelTime = 0x30;  // LARGE_INTEGER
    private const int OffsetImageName = 0x38;   // UNICODE_STRING
    private const int OffsetPid = 0x50;         // HANDLE
    private const int OffsetSessionId = 0x64;   // ULONG
    private const int OffsetWorkingSet = 0x90;  // SIZE_T
    private const int TamanhoMinimoDaEntrada = 0x98;

    internal static IReadOnlyList<RawProcess> Listar()
    {
        if (!Environment.Is64BitProcess)
            return Array.Empty<RawProcess>();

        var tamanho = 512 * 1024;

        for (var tentativa = 0; tentativa < 6; tentativa++)
        {
            var buffer = Marshal.AllocHGlobal(tamanho);

            try
            {
                var status = NtQuerySystemInformation(
                    SystemProcessInformation, buffer, tamanho, out var necessario);

                if (status == STATUS_INFO_LENGTH_MISMATCH)
                {
                    // A lista cresceu entre a consulta e a alocacao: refaz com folga.
                    tamanho = Math.Max(necessario, tamanho * 2) + 64 * 1024;
                    continue;
                }

                if (status != 0)
                    return Array.Empty<RawProcess>();

                return Percorrer(buffer, tamanho);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return Array.Empty<RawProcess>();
    }

    private static List<RawProcess> Percorrer(IntPtr buffer, int tamanhoDoBuffer)
    {
        var resultado = new List<RawProcess>(512);
        var deslocamento = 0;

        while (true)
        {
            // A entrada inteira precisa caber no que foi alocado.
            if (deslocamento < 0 || deslocamento + TamanhoMinimoDaEntrada > tamanhoDoBuffer)
                break;

            var entrada = buffer + deslocamento;

            var pid = (int)Marshal.ReadIntPtr(entrada, OffsetPid);
            var user = Marshal.ReadInt64(entrada, OffsetUserTime);
            var kernel = Marshal.ReadInt64(entrada, OffsetKernelTime);
            var workingSet = Marshal.ReadInt64(entrada, OffsetWorkingSet);
            var sessao = Marshal.ReadInt32(entrada, OffsetSessionId);
            var threads = Marshal.ReadInt32(entrada, OffsetThreads);

            var nome = LerNome(entrada, pid);

            if (pid >= 0)
            {
                resultado.Add(new RawProcess(
                    Pid: pid,
                    Nome: nome,
                    TempoDeCpu100ns: user + kernel,
                    WorkingSetBytes: workingSet,
                    SessionId: sessao,
                    Threads: threads));
            }

            var proximo = Marshal.ReadInt32(entrada, OffsetNextEntry);
            if (proximo <= 0)
                break;

            deslocamento += proximo;
        }

        return resultado;
    }

    /// <summary>ImageName e um UNICODE_STRING; o processo ocioso vem com Buffer nulo.</summary>
    private static string LerNome(IntPtr entrada, int pid)
    {
        var comprimentoEmBytes = (ushort)Marshal.ReadInt16(entrada, OffsetImageName);
        var ponteiro = Marshal.ReadIntPtr(entrada, OffsetImageName + 8);

        if (ponteiro == IntPtr.Zero || comprimentoEmBytes == 0)
            return pid == 0 ? "Idle" : "System";

        // Limite de sanidade: nome de imagem nao passa de MAX_PATH.
        if (comprimentoEmBytes > 520)
            return "?";

        var nome = Marshal.PtrToStringUni(ponteiro, comprimentoEmBytes / 2) ?? string.Empty;

        return nome.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? nome[..^4]
            : nome;
    }
}
