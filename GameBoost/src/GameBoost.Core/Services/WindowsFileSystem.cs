using GameBoost.Core.Abstractions;

namespace GameBoost.Core.Services;

public sealed class WindowsFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    /// <summary>
    /// Escrita atomica: grava .tmp e troca. O antivirus desta maquina costuma
    /// segurar o .tmp por alguns milissegundos, entao a troca tem retry.
    /// </summary>
    public void WriteAllText(string path, string content)
    {
        var diretorio = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(diretorio))
            Directory.CreateDirectory(diretorio);

        var temporario = path + ".tmp";
        File.WriteAllText(temporario, content);

        for (var tentativa = 0; ; tentativa++)
        {
            try
            {
                File.Move(temporario, path, overwrite: true);
                return;
            }
            catch (IOException) when (tentativa < 4)
            {
                Thread.Sleep(50 * (tentativa + 1));
            }
            catch (UnauthorizedAccessException) when (tentativa < 4)
            {
                Thread.Sleep(50 * (tentativa + 1));
            }
        }
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteFile(string path) => File.Delete(path);

    public void MoveFile(string source, string destination, bool overwrite)
        => File.Move(source, destination, overwrite);

    public long GetFileSize(string path) => new FileInfo(path).Length;

    public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option)
        => Directory.EnumerateFiles(path, pattern, option);

    public IEnumerable<string> EnumerateDirectories(string path)
        => Directory.EnumerateDirectories(path);
}
