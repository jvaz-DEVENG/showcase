using System.Text.Json;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Settings;

namespace GameBoost.Core.State;

public interface IStateBackup
{
    IReadOnlyList<ChangeRecord> Pendentes { get; }
    IReadOnlyList<ChangeRecord> Todos { get; }
    bool TemPendencias { get; }

    ChangeRecord Registrar(ChangeRecord record);
    void MarcarRevertido(string changeId, DateTimeOffset quando);
    void Recarregar();
    void Persistir();
}

/// <summary>
/// Pilha de ChangeRecord em state-backup.json. Grava a cada alteracao: se o app
/// travar ou o PC reiniciar com o Modo Game ativo, a proxima abertura le este
/// arquivo e oferece a restauracao.
/// </summary>
public sealed class StateBackup : IStateBackup
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly IFileSystem _fs;
    private readonly IGameBoostLogger _log;
    private readonly IClock _clock;
    private readonly object _gate = new();
    private List<ChangeRecord> _records = new();

    public StateBackup(AppPaths paths, IFileSystem fs, IGameBoostLogger log, IClock clock)
    {
        _paths = paths;
        _fs = fs;
        _log = log;
        _clock = clock;
        Recarregar();
    }

    public IReadOnlyList<ChangeRecord> Pendentes
    {
        get { lock (_gate) return _records.Where(r => r.Pendente).ToList(); }
    }

    public IReadOnlyList<ChangeRecord> Todos
    {
        get { lock (_gate) return _records.ToList(); }
    }

    public bool TemPendencias
    {
        get { lock (_gate) return _records.Any(r => r.Pendente); }
    }

    public ChangeRecord Registrar(ChangeRecord record)
    {
        lock (_gate)
        {
            if (record.Data == default)
                record.Data = _clock.Now;

            _records.Add(record);
            PersistirInterno();
        }

        _log.Info(record.Modulo, "Snapshot", record.Alvo,
            $"anterior={record.ValorAnterior ?? "(inexistente)"} novo={record.ValorNovo ?? "-"}");
        return record;
    }

    public void MarcarRevertido(string changeId, DateTimeOffset quando)
    {
        lock (_gate)
        {
            var alvo = _records.FirstOrDefault(r => r.Id == changeId);
            if (alvo is null)
                return;

            alvo.Revertido = true;
            alvo.DataReversao = quando;
            PersistirInterno();
        }
    }

    public void Recarregar()
    {
        lock (_gate)
        {
            if (!_fs.FileExists(_paths.StateBackupFile))
            {
                _records = new List<ChangeRecord>();
                return;
            }

            try
            {
                var texto = _fs.ReadAllText(_paths.StateBackupFile);
                _records = JsonSerializer.Deserialize<List<ChangeRecord>>(texto, Json) ?? new List<ChangeRecord>();
            }
            catch (JsonException ex)
            {
                // Perder o backup e pior que manter um arquivo ilegivel: preserva copia.
                _log.Error("State", "Recarregar", _paths.StateBackupFile,
                    "state-backup.json ilegivel, preservado como .corrompido", ex);
                try
                {
                    _fs.WriteAllText(_paths.StateBackupFile + ".corrompido", _fs.ReadAllText(_paths.StateBackupFile));
                }
                catch (IOException)
                {
                }

                _records = new List<ChangeRecord>();
            }
        }
    }

    public void Persistir()
    {
        lock (_gate) PersistirInterno();
    }

    private void PersistirInterno()
    {
        _fs.CreateDirectory(_paths.RootDirectory);
        _fs.WriteAllText(_paths.StateBackupFile, JsonSerializer.Serialize(_records, Json));
    }
}
