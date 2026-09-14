using System.Runtime.InteropServices;

namespace GameBoost.Core.Native;

/// <summary>Um item devolvido pela enumeração: nome, tamanho e se é pasta.</summary>
public readonly record struct FoundEntry(string Nome, long Tamanho, bool EhPasta, DateTime Modificado);

/// <summary>
/// Enumeração de diretório com FindFirstFileEx e FIND_FIRST_EX_LARGE_FETCH.
///
/// O flag de busca grande faz o Windows trazer vários registros por chamada ao
/// kernel em vez de um por vez, e o tamanho já vem no próprio resultado — sem
/// isso seria preciso um GetFileAttributes por arquivo, que é o que torna
/// DirectoryInfo lento em disco cheio.
/// </summary>
public static class FastFind
{
    private const int MAX_PATH = 260;
    private const int FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const int FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
    private const int FIND_FIRST_EX_LARGE_FETCH = 0x02;
    private const int FindExInfoBasic = 1;
    private const int FindExSearchNameMatch = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)] public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileExW(
        string fileName, int infoLevelId, out WIN32_FIND_DATAW findData,
        int searchOp, IntPtr searchFilter, int additionalFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextFileW(IntPtr findFile, out WIN32_FIND_DATAW findData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr findFile);

    private static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>
    /// Lista o conteúdo direto de uma pasta. Não lança: pasta sem permissão
    /// devolve lista vazia, porque numa varredura de disco inteiro sempre
    /// haverá pastas de sistema fechadas, e parar em cada uma inviabilizaria
    /// a leitura.
    /// </summary>
    public static List<FoundEntry> Listar(string pasta) => Listar(pasta, out _);

    /// <param name="acessoNegado">
    /// True so quando o Windows recusou a leitura. Pasta vazia devolve lista
    /// vazia com false: contar pasta vazia como "sem acesso" daria um numero
    /// inventado no relatorio (regra 4).
    /// </param>
    public static List<FoundEntry> Listar(string pasta, out bool acessoNegado)
    {
        acessoNegado = false;
        var resultado = new List<FoundEntry>();

        // O prefixo \\?\ tira o limite de 260 caracteres, que estoura em
        // node_modules e em pastas de build.
        var padrao = PrefixoLongo(pasta) + "\\*";

        var handle = FindFirstFileExW(
            padrao, FindExInfoBasic, out var dados,
            FindExSearchNameMatch, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);

        if (handle == InvalidHandle)
        {
            const int ERROR_FILE_NOT_FOUND = 2;
            const int ERROR_PATH_NOT_FOUND = 3;
            const int ERROR_NO_MORE_FILES = 18;

            var erro = Marshal.GetLastWin32Error();

            // Pasta que sumiu ou ficou vazia entre a descoberta e a leitura nao
            // e problema de permissao.
            acessoNegado = erro is not (ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND or ERROR_NO_MORE_FILES);
            return resultado;
        }

        try
        {
            do
            {
                var nome = dados.cFileName;

                if (nome is "." or "..")
                    continue;

                var ehPasta = (dados.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;

                // Junção e link simbólico apontam para outro lugar: seguir
                // contaria o mesmo espaço duas vezes e pode virar laço infinito.
                if ((dados.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
                    continue;

                var tamanho = ehPasta
                    ? 0
                    : ((long)dados.nFileSizeHigh << 32) | dados.nFileSizeLow;

                resultado.Add(new FoundEntry(nome, tamanho, ehPasta, ParaData(dados.ftLastWriteTime)));
            }
            while (FindNextFileW(handle, out dados));
        }
        finally
        {
            FindClose(handle);
        }

        return resultado;
    }

    private static string PrefixoLongo(string caminho)
    {
        if (caminho.StartsWith(@"\\?\", StringComparison.Ordinal))
            return caminho;

        // Caminho de rede usa outra forma do prefixo.
        return caminho.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC" + caminho[1..]
            : @"\\?\" + caminho;
    }

    private static DateTime ParaData(System.Runtime.InteropServices.ComTypes.FILETIME ft)
    {
        try
        {
            var valor = ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
            return valor <= 0 ? DateTime.MinValue : DateTime.FromFileTimeUtc(valor);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.MinValue;
        }
    }
}
