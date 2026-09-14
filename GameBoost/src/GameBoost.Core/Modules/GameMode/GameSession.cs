using System.Text.Json;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Settings;

namespace GameBoost.Core.Modules.GameMode;

/// <summary>App encerrado pelo Modo Game, com o necessario para reabrir depois.</summary>
public sealed class ClosedApp
{
    public string Nome { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public string? Argumentos { get; set; }
    public string? WorkingDirectory { get; set; }

    /// <summary>Marcado com estrela: reabre sozinho ao desligar o Modo Game.</summary>
    public bool Essencial { get; set; }

    public bool Reaberto { get; set; }
}

/// <summary>
/// Conteudo de session.json. Sobrevive a um travamento: se o app morrer com o
/// Modo Game ativo, a proxima abertura le este arquivo e oferece a restauracao.
/// </summary>
public sealed class GameSession
{
    public bool Ativo { get; set; }
    public DateTimeOffset? AtivadoEm { get; set; }
    public string? JogoDetectado { get; set; }
    public int? PidDoJogo { get; set; }
    public List<ClosedApp> AppsEncerrados { get; set; } = new();

    public long RamLivreAntes { get; set; }
    public long RamLivreDepois { get; set; }
    public long RamTotal { get; set; }
}

public interface ISessionStore
{
    GameSession Load();
    void Save(GameSession session);
    void Clear();
}

public sealed class SessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly IFileSystem _fs;
    private readonly IGameBoostLogger _log;

    public SessionStore(AppPaths paths, IFileSystem fs, IGameBoostLogger log)
    {
        _paths = paths;
        _fs = fs;
        _log = log;
    }

    public GameSession Load()
    {
        if (!_fs.FileExists(_paths.SessionFile))
            return new GameSession();

        try
        {
            return JsonSerializer.Deserialize<GameSession>(_fs.ReadAllText(_paths.SessionFile), Json)
                ?? new GameSession();
        }
        catch (JsonException ex)
        {
            _log.Error("GameMode", "LoadSession", _paths.SessionFile, "session.json ilegivel", ex);
            return new GameSession();
        }
    }

    public void Save(GameSession session)
    {
        _fs.CreateDirectory(_paths.RootDirectory);
        _fs.WriteAllText(_paths.SessionFile, JsonSerializer.Serialize(session, Json));
    }

    public void Clear() => Save(new GameSession());
}
