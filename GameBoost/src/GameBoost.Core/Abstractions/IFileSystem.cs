namespace GameBoost.Core.Abstractions;

/// <summary>
/// Toda escrita em disco passa por aqui. Os testes usam um fake que registra
/// cada caminho tocado, garantindo a regra 2 (nunca deletar fora da whitelist).
/// </summary>
public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string ReadAllText(string path);
    void WriteAllText(string path, string content);
    void CreateDirectory(string path);
    void DeleteFile(string path);
    void MoveFile(string source, string destination, bool overwrite);
    long GetFileSize(string path);
    IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option);
    IEnumerable<string> EnumerateDirectories(string path);
}
