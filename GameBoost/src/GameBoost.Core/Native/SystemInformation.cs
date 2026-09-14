using System.Runtime.InteropServices;

namespace GameBoost.Core.Native;

/// <summary>
/// NtQuerySystemInformation e IP Helper.
///
/// Por que nao PerformanceCounter: os nomes de contador do Windows sao
/// traduzidos. Num Windows pt-BR, "\Processor Information(_Total)" simplesmente
/// nao existe -- e "\Informacoes do Processador(_Total)". Estas APIs devolvem
/// numeros, nao texto, entao funcionam em qualquer idioma.
/// </summary>
internal static class SystemInformation
{
    private const int SystemProcessorPerformanceInformation = 8;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
    {
        public long IdleTime;
        public long KernelTime;   // inclui IdleTime
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public uint InterruptCount;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

    /// <summary>Tempos acumulados por nucleo logico. Retorna vazio se a chamada falhar.</summary>
    internal static SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] LerTemposDeProcessador(int nucleos)
    {
        var tamanho = Marshal.SizeOf<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
        var buffer = Marshal.AllocHGlobal(tamanho * nucleos);

        try
        {
            var status = NtQuerySystemInformation(
                SystemProcessorPerformanceInformation, buffer, tamanho * nucleos, out _);

            if (status != 0)
                return Array.Empty<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();

            var resultado = new SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[nucleos];
            for (var i = 0; i < nucleos; i++)
            {
                resultado[i] = Marshal.PtrToStructure<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(
                    buffer + i * tamanho);
            }

            return resultado;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ---------------- Memoria de sistema: Standby List ----------------

    private const int SystemMemoryListInformation = 80;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_MEMORY_LIST_INFORMATION
    {
        public UIntPtr ZeroPageCount;
        public UIntPtr FreePageCount;
        public UIntPtr ModifiedPageCount;
        public UIntPtr ModifiedNoWritePageCount;
        public UIntPtr BadPageCount;
        // Seguem 8 contadores de prioridade da Standby List.
    }

    /// <summary>
    /// Tamanho da Standby List em bytes. Le os 8 contadores por prioridade que
    /// vem logo depois da estrutura fixa. Retorna null se nao conseguir.
    /// </summary>
    internal static long? LerStandbyList()
    {
        const int prioridades = 8;
        var fixo = Marshal.SizeOf<SYSTEM_MEMORY_LIST_INFORMATION>();
        var tamanho = fixo + prioridades * IntPtr.Size;
        var buffer = Marshal.AllocHGlobal(tamanho);

        try
        {
            if (NtQuerySystemInformation(SystemMemoryListInformation, buffer, tamanho, out _) != 0)
                return null;

            long paginas = 0;
            for (var i = 0; i < prioridades; i++)
            {
                var valor = IntPtr.Size == 8
                    ? Marshal.ReadInt64(buffer, fixo + i * 8)
                    : Marshal.ReadInt32(buffer, fixo + i * 4);

                if (valor > 0)
                    paginas += valor;
            }

            return paginas * Environment.SystemPageSize;
        }
        catch (AccessViolationException)
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
