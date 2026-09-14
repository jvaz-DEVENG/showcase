namespace GameBoost.Core.Native;

/// <summary>
/// Ponte publica para os poucos P/Invoke que a composicao precisa enxergar.
/// NativeMethods segue interno para o resto do mundo.
/// </summary>
public static class NativeMethodsBridge
{
    public static bool TryGetSessionId(int processId, out int sessionId)
        => NativeMethods.ProcessIdToSessionId(processId, out sessionId);

    /// <summary>Resolucao do timer em unidades de 100 ns. 5000 = 0,5 ms.</summary>
    public static bool SetTimerResolution(uint cemNanosegundos, out uint atual)
        => NativeMethods.NtSetTimerResolution(cemNanosegundos, true, out atual) == 0;

    public static bool RestoreTimerResolution(uint cemNanosegundos, out uint atual)
        => NativeMethods.NtSetTimerResolution(cemNanosegundos, false, out atual) == 0;

    public static bool TryQueryTimerResolution(out uint minimo, out uint maximo, out uint atual)
        => NativeMethods.NtQueryTimerResolution(out minimo, out maximo, out atual) == 0;
}
