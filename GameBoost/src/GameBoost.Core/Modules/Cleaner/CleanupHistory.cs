using System.Text.Json;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Settings;

namespace GameBoost.Core.Modules.Cleaner;

public sealed class CleanupEntry
{
    public DateTimeOffset Data { get; set; }
    public List<string> Categorias { get; set; } = new();
    public long BytesLiberados { get; set; }
    public int ArquivosRemovidos { get; set; }
}

/// <summary>
/// Histórico de limpeza em cleanup-history.json (seção 5.2). Serve para o
/// usuário conferir o que o app já fez e quanto rendeu de verdade — e para
/// ele mesmo perceber quando uma limpeza rendeu quase nada.
/// </summary>
public sealed class CleanupHistory
{
    private const int MaximoDeEntradas = 200;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly IFileSystem _fs;
    private readonly IGameBoostLogger _log;
    private readonly object _gate = new();

    public CleanupHistory(AppPaths paths, IFileSystem fs, IGameBoostLogger log)
    {
        _paths = paths;
        _fs = fs;
        _log = log;
    }

    public IReadOnlyList<CleanupEntry> Ler()
    {
        lock (_gate)
        {
            if (!_fs.FileExists(_paths.CleanupHistoryFile))
                return Array.Empty<CleanupEntry>();

            try
            {
                return JsonSerializer.Deserialize<List<CleanupEntry>>(
                    _fs.ReadAllText(_paths.CleanupHistoryFile), Json) ?? new List<CleanupEntry>();
            }
            catch (JsonException ex)
            {
                _log.Error("Cleaner", "Historico", _paths.CleanupHistoryFile, "arquivo ilegivel", ex);
                return Array.Empty<CleanupEntry>();
            }
        }
    }

    public void Registrar(CleanupEntry entrada)
    {
        lock (_gate)
        {
            var lista = Ler().ToList();
            lista.Insert(0, entrada);

            // Histórico não pode crescer para sempre.
            if (lista.Count > MaximoDeEntradas)
                lista = lista.Take(MaximoDeEntradas).ToList();

            _fs.CreateDirectory(_paths.RootDirectory);
            _fs.WriteAllText(_paths.CleanupHistoryFile, JsonSerializer.Serialize(lista, Json));
        }
    }

    public long TotalLiberadoNoAno(int ano)
        => Ler().Where(e => e.Data.Year == ano).Sum(e => e.BytesLiberados);
}
