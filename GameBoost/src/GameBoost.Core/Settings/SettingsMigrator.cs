using GameBoost.Core.Logging;

namespace GameBoost.Core.Settings;

/// <summary>
/// Migradores em sequencia, um por versao. A v1 do GameBoost nao gravava
/// schemaVersion: um settings.json sem o campo desserializa como 0 e cai no
/// migrador 0 -> 1.
/// </summary>
public static class SettingsMigrator
{
    public static AppSettings Migrar(AppSettings settings, IGameBoostLogger log)
    {
        while (settings.SchemaVersion < AppSettings.VersaoAtual)
        {
            var de = settings.SchemaVersion;
            settings = de switch
            {
                0 => De0Para1(settings),
                1 => De1Para2(settings),
                _ => Encerrar(settings)
            };
            log.Info("Settings", "Migrar", $"v{de}", $"migrado para v{settings.SchemaVersion}");
        }

        return settings;
    }

    /// <summary>v1 do GameBoost: arquivo sem schemaVersion. So carimba a versao.</summary>
    private static AppSettings De0Para1(AppSettings s)
    {
        s.SchemaVersion = 1;
        return s;
    }

    /// <summary>v2: campos novos do modo "so o essencial" e de atualizacao.</summary>
    private static AppSettings De1Para2(AppSettings s)
    {
        if (s.LimiteCpuPercent <= 0) s.LimiteCpuPercent = 2.0;
        if (s.LimiteRamMegabytes <= 0) s.LimiteRamMegabytes = 300;
        s.SchemaVersion = 2;
        return s;
    }

    private static AppSettings Encerrar(AppSettings s)
    {
        s.SchemaVersion = AppSettings.VersaoAtual;
        return s;
    }
}
