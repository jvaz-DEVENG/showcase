using System.Runtime.InteropServices;

namespace GameBoost.Core.Native;

/// <summary>
/// P/Invoke das ferramentas rápidas. Regra 9: onde há API, nada de chamar
/// executável. `ipconfig /flushdns` vira DnsFlushResolverCache, que é o que o
/// próprio ipconfig faz por dentro.
/// </summary>
public static class ToolsNative
{
    // ---------------- DNS ----------------

    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DnsFlushResolverCache();

    /// <summary>Limpa o cache de DNS do sistema. É o que `ipconfig /flushdns` faz.</summary>
    public static bool LimparCacheDeDns()
    {
        try
        {
            return DnsFlushResolverCache();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    // ---------------- Reiniciar o driver de vídeo ----------------

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL, wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInput);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_B = 0x42;

    /// <summary>
    /// Envia Win+Ctrl+Shift+B, o atalho que o Windows reserva para recarregar o
    /// driver de vídeo. Não existe API pública para isso: simular o atalho é o
    /// caminho oficial.
    /// </summary>
    public static bool ReiniciarDriverDeVideo()
    {
        var teclas = new ushort[] { VK_LWIN, VK_CONTROL, VK_SHIFT, VK_B };
        var entradas = new List<INPUT>();

        foreach (var tecla in teclas)
            entradas.Add(Tecla(tecla, pressionar: true));

        // Solta na ordem inversa, como um teclado de verdade.
        for (var i = teclas.Length - 1; i >= 0; i--)
            entradas.Add(Tecla(teclas[i], pressionar: false));

        var vetor = entradas.ToArray();
        var enviadas = SendInput((uint)vetor.Length, vetor, Marshal.SizeOf<INPUT>());

        return enviadas == vetor.Length;
    }

    private static INPUT Tecla(ushort codigo, bool pressionar) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = codigo,
                dwFlags = pressionar ? 0 : KEYEVENTF_KEYUP,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };
}
