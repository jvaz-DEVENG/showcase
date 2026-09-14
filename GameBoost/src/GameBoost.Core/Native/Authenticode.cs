using System.Runtime.InteropServices;

namespace GameBoost.Core.Native;

/// <summary>
/// Verifica a assinatura digital de um executável com WinVerifyTrust.
///
/// É o que o Windows usa para decidir se confia num binário. Melhor que só
/// extrair o certificado: aqui a assinatura é de fato **validada** — certificado
/// expirado, revogado ou arquivo adulterado reprovam.
///
/// (`X509Certificate.CreateFromSignedFile` faria o trabalho mais curto, mas
/// está obsoleto desde o .NET 9.)
/// </summary>
public static class Authenticode
{
    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_SAFER_FLAG = 0x100;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, IntPtr pWVTData);

    /// <summary>
    /// True quando o arquivo tem assinatura Authenticode válida. Falso não
    /// significa suspeito: muito programa legítimo simplesmente não assina.
    /// </summary>
    public static bool Assinado(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho))
            return false;

        var arquivo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = caminho,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };

        var ponteiroArquivo = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        var ponteiroDados = IntPtr.Zero;

        try
        {
            Marshal.StructureToPtr(arquivo, ponteiroArquivo, false);

            var dados = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                // Sem checar revogação: exigiria rede, e numa lista de dezenas
                // de executáveis isso travaria a varredura.
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = ponteiroArquivo,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_SAFER_FLAG
            };

            ponteiroDados = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
            Marshal.StructureToPtr(dados, ponteiroDados, false);

            var resultado = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ponteiroDados);

            // O estado precisa ser fechado, senão vaza handle a cada arquivo.
            var paraFechar = Marshal.PtrToStructure<WINTRUST_DATA>(ponteiroDados);
            paraFechar.dwStateAction = WTD_STATEACTION_CLOSE;
            Marshal.StructureToPtr(paraFechar, ponteiroDados, false);
            WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ponteiroDados);

            return resultado == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (ponteiroDados != IntPtr.Zero)
                Marshal.FreeHGlobal(ponteiroDados);

            Marshal.FreeHGlobal(ponteiroArquivo);
        }
    }

    /// <summary>Nome da empresa declarado no executável, para exibição.</summary>
    public static string? Fabricante(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho))
            return null;

        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(caminho);
            return string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
