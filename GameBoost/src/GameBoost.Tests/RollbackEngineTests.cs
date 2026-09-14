using GameBoost.Core.Abstractions;
using GameBoost.Core.Settings;
using GameBoost.Core.State;
using Xunit;

namespace GameBoost.Tests;

/// <summary>
/// Prova da regra 1: aplicar -> reverter -> o sistema volta ao estado original.
/// </summary>
public sealed class RollbackEngineTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeLogger _log = new();
    private readonly FakeClock _clock = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeServices _services = new();
    private readonly FakePower _power = new();
    private readonly AppPaths _paths = new(@"C:\dados\GameBoost", portatil: false);

    private (StateBackup Backup, RollbackEngine Engine) Montar()
    {
        var backup = new StateBackup(_paths, _fs, _log, _clock);
        var engine = new RollbackEngine(backup, _registry, _services, _power, _log, _clock);
        return (backup, engine);
    }

    // ---------------- Registro ----------------

    [Fact]
    public void Valor_de_registro_existente_volta_ao_original()
    {
        _registry.Semear(RegistryRoot.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 1, RegistryValueKindLite.DWord);
        var (backup, engine) = Montar();

        var record = backup.Registrar(new ChangeRecord
        {
            Modulo = "gamemode",
            Tipo = ChangeType.Registry,
            Alvo = @"System\GameConfigStore",
            SubAlvo = "GameDVR_Enabled",
            ValorAnterior = "1",
            ValorNovo = "0",
            ValorAnteriorExistia = true,
            Extras = { ["root"] = "CurrentUser", ["kind"] = "DWord" }
        });

        _registry.SetValue(RegistryRoot.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0, RegistryValueKindLite.DWord);
        Assert.Equal(0, _registry.GetValue(RegistryRoot.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled"));

        var resultados = engine.Reverter(new[] { record.Id }, dryRun: false);

        Assert.True(resultados.Single().Sucesso);
        Assert.Equal(1, _registry.GetValue(RegistryRoot.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled"));
    }

    [Fact]
    public void Valor_que_nao_existia_antes_e_apagado_e_nao_zerado()
    {
        var (backup, engine) = Montar();

        var record = backup.Registrar(new ChangeRecord
        {
            Modulo = "gamemode",
            Tipo = ChangeType.Registry,
            Alvo = @"Software\Microsoft\GameBar",
            SubAlvo = "ShowStartupPanel",
            ValorAnterior = null,
            ValorNovo = "0",
            ValorAnteriorExistia = false,
            Extras = { ["root"] = "CurrentUser", ["kind"] = "DWord" }
        });

        _registry.SetValue(RegistryRoot.CurrentUser, @"Software\Microsoft\GameBar", "ShowStartupPanel", 0, RegistryValueKindLite.DWord);

        engine.Reverter(new[] { record.Id }, dryRun: false);

        Assert.Null(_registry.GetValue(RegistryRoot.CurrentUser, @"Software\Microsoft\GameBar", "ShowStartupPanel"));
        Assert.Contains(@"CurrentUser\Software\Microsoft\GameBar\ShowStartupPanel", _registry.Remocoes);
    }

    [Fact]
    public void Tipos_de_valor_sobrevivem_a_ida_e_volta()
    {
        var (backup, engine) = Montar();

        var binario = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var record = backup.Registrar(new ChangeRecord
        {
            Modulo = "teste",
            Tipo = ChangeType.Registry,
            Alvo = @"Software\Teste",
            SubAlvo = "Blob",
            ValorAnterior = Convert.ToHexString(binario),
            ValorNovo = "00",
            ValorAnteriorExistia = true,
            Extras = { ["root"] = "CurrentUser", ["kind"] = "Binary" }
        });

        engine.Reverter(new[] { record.Id }, dryRun: false);

        var restaurado = Assert.IsType<byte[]>(_registry.GetValue(RegistryRoot.CurrentUser, @"Software\Teste", "Blob"));
        Assert.Equal(binario, restaurado);
    }

    // ---------------- Servicos ----------------

    [Fact]
    public void Servico_parado_volta_a_rodar()
    {
        _services.Semear(new ServiceInfo("wuauserv", "Windows Update", IsRunning: true, ServiceStartMode.Manual));
        var (backup, engine) = Montar();

        var record = backup.Registrar(new ChangeRecord
        {
            Modulo = "gamemode",
            Tipo = ChangeType.Service,
            Alvo = "wuauserv",
            ValorAnterior = "Manual",
            ValorNovo = "Manual",
            Extras = { ["estavaRodando"] = "true" }
        });

        _services.Stop("wuauserv", TimeSpan.FromSeconds(1));
        Assert.False(_services.GetService("wuauserv")!.IsRunning);

        var resultado = engine.Reverter(new[] { record.Id }, dryRun: false).Single();

        Assert.True(resultado.Sucesso);
        Assert.True(_services.GetService("wuauserv")!.IsRunning);
    }

    [Fact]
    public void Start_mode_de_servico_volta_ao_original()
    {
        _services.Semear(new ServiceInfo("DiagTrack", "Telemetria", IsRunning: false, ServiceStartMode.Automatic));
        var (backup, engine) = Montar();

        var record = backup.Registrar(new ChangeRecord
        {
            Modulo = "services",
            Tipo = ChangeType.Service,
            Alvo = "DiagTrack",
            ValorAnterior = "Automatic",
            ValorNovo = "Disabled",
            Extras = { ["estavaRodando"] = "false" }
        });

        _services.SetStartMode("DiagTrack", ServiceStartMode.Disabled);

        engine.Reverter(new[] { record.Id }, dryRun: false);

        Assert.Equal(ServiceStartMode.Automatic, _services.GetService("DiagTrack")!.StartMode);
        Assert.False(_services.GetService("DiagTrack")!.IsRunning);
    }

    // ---------------- Energia ----------------

    [Fact]
    public void Plano_de_energia_volta_ao_anterior()
    {
        var balanceado = _power.Ativo;
        var (backup, engine) = Montar();

        var record = backup.Registrar(new ChangeRecord
        {
            Modulo = "gamemode",
            Tipo = ChangeType.Power,
            Alvo = "plano-de-energia",
            ValorAnterior = balanceado.ToString(),
            ValorNovo = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"
        });

        _power.SetActivePlan(new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"));

        engine.Reverter(new[] { record.Id }, dryRun: false);

        Assert.Equal(balanceado, _power.Ativo);
    }

    // ---------------- Ordem, dry-run e idempotencia ----------------

    [Fact]
    public void Reversao_acontece_na_ordem_inversa_da_aplicacao()
    {
        var (backup, engine) = Montar();

        foreach (var nome in new[] { "primeiro", "segundo", "terceiro" })
        {
            backup.Registrar(new ChangeRecord
            {
                Modulo = "teste",
                Tipo = ChangeType.Registry,
                Alvo = @"Software\Teste",
                SubAlvo = nome,
                ValorAnterior = "1",
                ValorNovo = "0",
                ValorAnteriorExistia = true,
                Extras = { ["root"] = "CurrentUser", ["kind"] = "DWord" }
            });
        }

        engine.ReverterTudo(dryRun: false);

        Assert.Equal(
            new[] { "terceiro", "segundo", "primeiro" },
            _registry.Escritas.Select(e => e[(e.LastIndexOf('\\') + 1)..]).ToArray());
    }

    [Fact]
    public void Dry_run_nao_toca_em_nada()
    {
        _registry.Semear(RegistryRoot.CurrentUser, @"Software\Teste", "Valor", 1, RegistryValueKindLite.DWord);
        _services.Semear(new ServiceInfo("wuauserv", "Windows Update", IsRunning: false, ServiceStartMode.Manual));
        var (backup, engine) = Montar();

        backup.Registrar(new ChangeRecord
        {
            Modulo = "teste", Tipo = ChangeType.Registry,
            Alvo = @"Software\Teste", SubAlvo = "Valor",
            ValorAnterior = "9", ValorNovo = "0", ValorAnteriorExistia = true,
            Extras = { ["root"] = "CurrentUser", ["kind"] = "DWord" }
        });

        backup.Registrar(new ChangeRecord
        {
            Modulo = "teste", Tipo = ChangeType.Service,
            Alvo = "wuauserv", ValorAnterior = "Manual",
            Extras = { ["estavaRodando"] = "true" }
        });

        backup.Registrar(new ChangeRecord
        {
            Modulo = "teste", Tipo = ChangeType.Power,
            Alvo = "plano-de-energia",
            ValorAnterior = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"
        });

        var resultados = engine.ReverterTudo(dryRun: true);

        Assert.All(resultados, r => Assert.True(r.Sucesso));
        Assert.Empty(_registry.Escritas);
        Assert.Empty(_registry.Remocoes);
        Assert.Empty(_services.Inicios);
        Assert.Empty(_services.Modos);
        Assert.Empty(_power.Trocas);

        // Dry-run nao pode marcar nada como revertido: as pendencias continuam.
        Assert.Equal(3, backup.Pendentes.Count);
    }

    [Fact]
    public void Reverter_duas_vezes_nao_reaplica_nada()
    {
        _registry.Semear(RegistryRoot.CurrentUser, @"Software\Teste", "Valor", 0, RegistryValueKindLite.DWord);
        var (backup, engine) = Montar();

        backup.Registrar(new ChangeRecord
        {
            Modulo = "teste", Tipo = ChangeType.Registry,
            Alvo = @"Software\Teste", SubAlvo = "Valor",
            ValorAnterior = "1", ValorNovo = "0", ValorAnteriorExistia = true,
            Extras = { ["root"] = "CurrentUser", ["kind"] = "DWord" }
        });

        Assert.Single(engine.ReverterTudo(dryRun: false));
        Assert.Empty(engine.ReverterTudo(dryRun: false));
        Assert.False(backup.TemPendencias);
        Assert.Single(_registry.Escritas);
    }

    [Fact]
    public void Falha_em_um_item_nao_interrompe_os_demais()
    {
        var (backup, engine) = Montar();

        // Sem a chave "root", este registro nao tem como ser revertido.
        backup.Registrar(new ChangeRecord
        {
            Modulo = "teste", Tipo = ChangeType.Registry,
            Alvo = @"Software\Teste", SubAlvo = "Quebrado",
            ValorAnterior = "1", ValorAnteriorExistia = true
        });

        backup.Registrar(new ChangeRecord
        {
            Modulo = "teste", Tipo = ChangeType.Registry,
            Alvo = @"Software\Teste", SubAlvo = "Bom",
            ValorAnterior = "7", ValorNovo = "0", ValorAnteriorExistia = true,
            Extras = { ["root"] = "CurrentUser", ["kind"] = "DWord" }
        });

        var resultados = engine.ReverterTudo(dryRun: false);

        Assert.Equal(2, resultados.Count);
        Assert.Single(resultados, r => !r.Sucesso);
        Assert.Equal(7, _registry.GetValue(RegistryRoot.CurrentUser, @"Software\Teste", "Bom"));

        // O que falhou continua pendente para nova tentativa.
        Assert.Single(backup.Pendentes);
    }

    [Fact]
    public void Estado_sobrevive_a_um_travamento_e_e_relido_do_disco()
    {
        var (backup, _) = Montar();

        backup.Registrar(new ChangeRecord
        {
            Modulo = "gamemode", Tipo = ChangeType.Power,
            Alvo = "plano-de-energia",
            ValorAnterior = "381b4222-f694-41f0-9685-ff5bb260df2e"
        });

        // Simula reabrir o app: nova instancia lendo o mesmo arquivo.
        var depoisDoCrash = new StateBackup(_paths, _fs, _log, _clock);

        Assert.True(depoisDoCrash.TemPendencias);
        Assert.Equal("plano-de-energia", depoisDoCrash.Pendentes.Single().Alvo);
    }

    [Fact]
    public void Reverter_com_lista_vazia_pega_tudo_que_esta_pendente()
    {
        var (backup, engine) = Montar();

        for (var i = 0; i < 3; i++)
        {
            backup.Registrar(new ChangeRecord
            {
                Modulo = "teste", Tipo = ChangeType.Registry,
                Alvo = @"Software\Teste", SubAlvo = $"V{i}",
                ValorAnterior = "1", ValorAnteriorExistia = true,
                Extras = { ["root"] = "CurrentUser", ["kind"] = "DWord" }
            });
        }

        Assert.Equal(3, engine.Reverter(Array.Empty<string>(), dryRun: false).Count);
    }
}
