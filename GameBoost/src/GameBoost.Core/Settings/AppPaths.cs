using System.Reflection;

namespace GameBoost.Core.Settings;

/// <summary>
/// Resolve onde os dados vivem. Se existir portable.txt ao lado do exe, usa .\data\
/// em vez de %LOCALAPPDATA%\GameBoost (secao 7 do spec).
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string rootDirectory, bool portatil)
    {
        RootDirectory = rootDirectory;
        Portatil = portatil;
    }

    public string RootDirectory { get; }
    public bool Portatil { get; }

    public string SettingsFile => Path.Combine(RootDirectory, "settings.json");
    public string SessionFile => Path.Combine(RootDirectory, "session.json");
    public string StateBackupFile => Path.Combine(RootDirectory, "state-backup.json");
    public string LogFile => Path.Combine(RootDirectory, "gameboost.log");
    public string CleanupHistoryFile => Path.Combine(RootDirectory, "cleanup-history.json");
    public string ProfilesDirectory => Path.Combine(RootDirectory, "profiles");
    public string HistoryDirectory => Path.Combine(RootDirectory, "history");
    public string CacheDirectory => Path.Combine(RootDirectory, "cache");
    public string BackupsDirectory => Path.Combine(RootDirectory, "backups");

    public static AppPaths Resolve()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath
            ?? Assembly.GetEntryAssembly()?.Location
            ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;

        var marcador = Path.Combine(exeDir, "portable.txt");
        if (File.Exists(marcador))
            return new AppPaths(Path.Combine(exeDir, "data"), portatil: true);

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppPaths(Path.Combine(local, "GameBoost"), portatil: false);
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ProfilesDirectory);
        Directory.CreateDirectory(HistoryDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }
}
