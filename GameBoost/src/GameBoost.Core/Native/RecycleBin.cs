using System.Runtime.InteropServices;

namespace GameBoost.Core.Native;

/// <summary>
/// Envia arquivos para a Lixeira (regra 2: conteúdo do usuário nunca é apagado
/// direto). Usa SHFileOperation com FOF_ALLOWUNDO, que é o que o Explorer faz.
/// </summary>
internal static class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>
    /// **Sem `Pack`.** O alinhamento tem que ser o natural da plataforma.
    ///
    /// Com `Pack = 1` o .NET tira o preenchimento entre os campos, e em x64 o
    /// `pFrom` sai no deslocamento 12 em vez de 16. O shell32 continua lendo do
    /// 16, pega metade de um ponteiro colada na metade de outro, e usa aquilo
    /// como endereço: violação de acesso (0xc0000005) e o processo morre sem
    /// exceção gerenciada, sem log e sem caixa de erro.
    ///
    /// O defeito derrubava o aplicativo em **todo** envio para a Lixeira —
    /// Limpeza, Espaço e Restos usam esta mesma função.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);

    /// <summary>
    /// Manda tudo de uma vez: uma chamada por arquivo seria lenta e encheria a
    /// Lixeira de operações separadas. A lista precisa terminar em dois nulos.
    /// </summary>
    internal static bool ParaLixeira(IReadOnlyList<string> caminhos)
    {
        if (caminhos.Count == 0)
            return true;

        var lista = string.Join('\0', caminhos) + "\0\0";

        var operacao = new SHFILEOPSTRUCT
        {
            hwnd = IntPtr.Zero,
            wFunc = FO_DELETE,
            pFrom = lista,
            pTo = null,
            // WANTNUKEWARNING garante que, se algo nao couber na Lixeira, o
            // Windows avise em vez de apagar de vez sem perguntar.
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING
        };

        var resultado = SHFileOperationW(ref operacao);
        return resultado == 0 && !operacao.fAnyOperationsAborted;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? rootPath, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string? rootPath, ref SHQUERYRBINFO info);

    internal static (long Bytes, long Itens) ConsultarLixeira()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };

        return SHQueryRecycleBinW(null, ref info) == 0
            ? (info.i64Size, info.i64NumItems)
            : (0, 0);
    }

    internal static bool EsvaziarLixeira()
    {
        const uint SHERB_NOCONFIRMATION = 0x01;
        const uint SHERB_NOPROGRESSUI = 0x02;
        const uint SHERB_NOSOUND = 0x04;

        return SHEmptyRecycleBinW(IntPtr.Zero, null,
            SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND) == 0;
    }
}

/// <summary>Ponte pública para a Lixeira.</summary>
public static class RecycleBinBridge
{
    public static bool ParaLixeira(IReadOnlyList<string> caminhos) => RecycleBin.ParaLixeira(caminhos);

    public static (long Bytes, long Itens) Consultar() => RecycleBin.ConsultarLixeira();

    public static bool Esvaziar() => RecycleBin.EsvaziarLixeira();
}
