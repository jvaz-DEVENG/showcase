using System.Runtime.InteropServices;

namespace GameBoost.Core.Native;

/// <summary>
/// Descobre se um processo tem janela ocupando o monitor inteiro.
///
/// É o terceiro critério de detecção de jogo da seção 5.1. Não distingue tela
/// cheia exclusiva de borderless, e nem precisa: as duas dizem a mesma coisa
/// para o que interessa aqui — alguém está usando o monitor todo para uma coisa
/// só, e essa coisa merece a máquina.
/// </summary>
public static class WindowInfo
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// True quando o processo tem alguma janela visível do tamanho do monitor
    /// em que ela está.
    /// </summary>
    public static bool EstaEmTelaCheia(int pid)
    {
        var achou = false;

        try
        {
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true;

                GetWindowThreadProcessId(hWnd, out var dono);

                if (dono != (uint)pid)
                    return true;

                if (!GetWindowRect(hWnd, out var janela))
                    return true;

                var monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);

                if (monitor == IntPtr.Zero)
                    return true;

                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };

                if (!GetMonitorInfo(monitor, ref info))
                    return true;

                // Margem de um pixel: borderless às vezes fica um pixel maior
                // que o monitor, e tela cheia exclusiva às vezes um menor.
                var larguraJanela = janela.Right - janela.Left;
                var alturaJanela = janela.Bottom - janela.Top;
                var larguraMonitor = info.rcMonitor.Right - info.rcMonitor.Left;
                var alturaMonitor = info.rcMonitor.Bottom - info.rcMonitor.Top;

                if (Math.Abs(larguraJanela - larguraMonitor) <= 1
                    && Math.Abs(alturaJanela - alturaMonitor) <= 1)
                {
                    achou = true;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }

        return achou;
    }

    /// <summary>Pid do processo dono da janela em primeiro plano, ou zero.</summary>
    public static int PidEmPrimeiroPlano()
    {
        try
        {
            var janela = GetForegroundWindow();

            if (janela == IntPtr.Zero)
                return 0;

            GetWindowThreadProcessId(janela, out var pid);
            return (int)pid;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
    }
}
