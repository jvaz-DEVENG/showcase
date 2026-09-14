using System.Text.Json;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;

namespace GameBoost.Core.Settings;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}

/// <summary>
/// Le e grava settings.json. Na leitura roda os migradores em sequencia se o
/// schemaVersion estiver atrasado; o arquivo antigo vira .bak, nunca e apagado (secao 7).
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly IFileSystem _fs;
    private readonly IGameBoostLogger _log;

    public SettingsStore(AppPaths paths, IFileSystem fs, IGameBoostLogger log)
    {
        _paths = paths;
        _fs = fs;
        _log = log;
    }

    public AppSettings Load()
    {
        if (!_fs.FileExists(_paths.SettingsFile))
        {
            var novo = new AppSettings();
            Save(novo);
            return novo;
        }

        try
        {
            var texto = _fs.ReadAllText(_paths.SettingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(texto, Json) ?? new AppSettings();

            if (settings.SchemaVersion < AppSettings.VersaoAtual)
            {
                _fs.WriteAllText(_paths.SettingsFile + ".bak", texto);
                settings = SettingsMigrator.Migrar(settings, _log);
                Save(settings);
            }

            return settings;
        }
        catch (JsonException ex)
        {
            _log.Error("Settings", "Load", _paths.SettingsFile, "JSON invalido, recriando com padroes", ex);
            var texto = _fs.ReadAllText(_paths.SettingsFile);
            _fs.WriteAllText(_paths.SettingsFile + ".bak", texto);
            var novo = new AppSettings();
            Save(novo);
            return novo;
        }
    }

    public void Save(AppSettings settings)
    {
        settings.SchemaVersion = AppSettings.VersaoAtual;
        _fs.CreateDirectory(_paths.RootDirectory);
        _fs.WriteAllText(_paths.SettingsFile, JsonSerializer.Serialize(settings, Json));
    }
}
