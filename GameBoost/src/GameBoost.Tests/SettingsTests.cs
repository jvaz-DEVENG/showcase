using System.Text.Json;
using GameBoost.Core.Modules.GameMode;
using GameBoost.Core.Settings;
using Xunit;

namespace GameBoost.Tests;

public sealed class SettingsTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeLogger _log = new();
    private readonly AppPaths _paths = new(@"C:\dados\GameBoost", portatil: false);

    [Fact]
    public void Primeira_execucao_cria_settings_com_padroes()
    {
        var settings = new SettingsStore(_paths, _fs, _log).Load();

        Assert.Equal(AppSettings.VersaoAtual, settings.SchemaVersion);
        Assert.True(_fs.FileExists(_paths.SettingsFile));
    }

    [Fact]
    public void Settings_da_v1_sem_schema_version_e_migrado_e_o_original_vira_bak()
    {
        // A v1 do GameBoost gravava o arquivo sem o campo schemaVersion.
        _fs.WriteAllText(_paths.SettingsFile, """
            { "tema": "Dark", "pausarWindowsUpdate": true }
            """);

        var settings = new SettingsStore(_paths, _fs, _log).Load();

        Assert.Equal(AppSettings.VersaoAtual, settings.SchemaVersion);
        Assert.True(_fs.FileExists(_paths.SettingsFile + ".bak"));
        Assert.True(settings.PausarWindowsUpdate);
    }

    [Fact]
    public void Json_corrompido_nao_derruba_o_app_e_preserva_o_original()
    {
        _fs.WriteAllText(_paths.SettingsFile, "{ isto nao e json");

        var settings = new SettingsStore(_paths, _fs, _log).Load();

        Assert.Equal(AppSettings.VersaoAtual, settings.SchemaVersion);
        Assert.True(_fs.FileExists(_paths.SettingsFile + ".bak"));
    }

    [Fact]
    public void Migracao_preenche_limites_zerados()
    {
        var antigo = new AppSettings { SchemaVersion = 1, LimiteCpuPercent = 0, LimiteRamMegabytes = 0 };

        var migrado = SettingsMigrator.Migrar(antigo, _log);

        Assert.Equal(AppSettings.VersaoAtual, migrado.SchemaVersion);
        Assert.True(migrado.LimiteCpuPercent > 0);
        Assert.True(migrado.LimiteRamMegabytes > 0);
    }

    [Fact]
    public void Ida_e_volta_pelo_json_preserva_os_essenciais()
    {
        var original = new AppSettings
        {
            Essenciais = { new EssentialApp { Nome = "Discord", ExecutablePath = @"C:\d\Discord.exe", Argumentos = "--start-minimized" } },
            NuncaEncerrar = { "obs64" }
        };

        var store = new SettingsStore(_paths, _fs, _log);
        store.Save(original);
        var lido = store.Load();

        Assert.Equal("Discord", lido.Essenciais.Single().Nome);
        Assert.Equal("--start-minimized", lido.Essenciais.Single().Argumentos);
        Assert.Contains("obs64", lido.NuncaEncerrar);
    }

    [Fact]
    public void Modo_portatil_usa_a_pasta_data_ao_lado_do_exe()
    {
        var portatil = new AppPaths(@"D:\GameBoost\data", portatil: true);

        Assert.True(portatil.Portatil);
        Assert.Equal(@"D:\GameBoost\data\settings.json", portatil.SettingsFile);
    }
}

public sealed class GameSessionTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeLogger _log = new();
    private readonly AppPaths _paths = new(@"C:\dados\GameBoost", portatil: false);

    [Fact]
    public void Sessao_sobrevive_a_um_travamento()
    {
        var store = new SessionStore(_paths, _fs, _log);

        store.Save(new GameSession
        {
            Ativo = true,
            JogoDetectado = "cs2",
            AppsEncerrados = { new ClosedApp { Nome = "chrome", ExecutablePath = @"C:\c\chrome.exe", Essencial = true } }
        });

        var relido = new SessionStore(_paths, _fs, _log).Load();

        Assert.True(relido.Ativo);
        Assert.Equal("cs2", relido.JogoDetectado);
        Assert.True(relido.AppsEncerrados.Single().Essencial);
    }

    [Fact]
    public void Session_json_ilegivel_vira_sessao_vazia_sem_excecao()
    {
        _fs.WriteAllText(_paths.SessionFile, "{{{");

        var sessao = new SessionStore(_paths, _fs, _log).Load();

        Assert.False(sessao.Ativo);
        Assert.Empty(sessao.AppsEncerrados);
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(2048L, "2 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    public void Formatacao_de_tamanho_e_legivel(long bytes, string esperado)
    {
        Assert.Equal(esperado, GameModeModule.Formatar(bytes));
    }

    [Fact]
    public void Formatacao_de_gigabytes_usa_uma_casa_decimal()
    {
        Assert.StartsWith("1", GameModeModule.Formatar(1024L * 1024 * 1024));
        Assert.EndsWith("GB", GameModeModule.Formatar(1024L * 1024 * 1024));
    }
}
