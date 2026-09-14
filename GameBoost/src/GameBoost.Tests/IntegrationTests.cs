using GameBoost.Cli;
using GameBoost.Core;
using GameBoost.Core.Modules;
using GameBoost.Core.Modules.GameMode;
using GameBoost.Core.Safety;
using GameBoost.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBoost.Tests;

/// <summary>
/// Roda contra o Windows real, so leitura: nenhum processo e encerrado e nada
/// de sistema e alterado. Escreve apenas numa pasta temporaria descartavel.
/// </summary>
[Trait("Category", "Windows")]
public sealed class IntegrationTests : IDisposable
{
    private readonly string _pasta;
    private readonly ServiceProvider _provider;

    public IntegrationTests()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "GameBoostTests", Guid.NewGuid().ToString("N"));

        var colecao = new ServiceCollection();
        colecao.AddGameBoostCore(new AppPaths(_pasta, portatil: true));
        _provider = colecao.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();

        try
        {
            if (Directory.Exists(_pasta))
                Directory.Delete(_pasta, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Varredura_real_nunca_pre_marca_processo_protegido()
    {
        var modulo = _provider.GetRequiredService<GameModeModule>();

        var resultado = await modulo.ScanAsync(null, CancellationToken.None);

        Assert.NotEmpty(resultado.Itens);

        foreach (var item in resultado.Itens.Where(i => i.PreMarcado))
        {
            Assert.False(item.Bloqueado, $"{item.Titulo} veio pre-marcado mesmo estando bloqueado.");

            var nome = ProtectedProcesses.Normalizar(item.Titulo);
            Assert.DoesNotContain(nome, ProtectedProcesses.Criticos);
            Assert.DoesNotContain(nome, ProtectedProcesses.AntiCheats);
            Assert.DoesNotContain(nome, ProtectedProcesses.Seguranca);
            Assert.DoesNotContain(nome, ProtectedProcesses.NuncaPreMarcados);
        }
    }

    [Fact]
    public async Task Varredura_real_nao_lista_o_proprio_processo_de_teste()
    {
        var modulo = _provider.GetRequiredService<GameModeModule>();
        var resultado = await modulo.ScanAsync(null, CancellationToken.None);

        // O SafetyGuard usa o pid do processo atual, que aqui e o runner de testes.
        var eu = System.Diagnostics.Process.GetCurrentProcess().ProcessName;

        Assert.DoesNotContain(resultado.Itens, i =>
            i.Titulo.Equals(eu, StringComparison.OrdinalIgnoreCase) && i.PreMarcado);
    }

    [Fact]
    public async Task Scan_pela_cli_gera_relatorio_e_nao_altera_nada()
    {
        var saida = new StringWriter();
        var runner = new CommandRunner(_provider, saida);

        var codigo = await runner.ExecutarAsync(CommandLineOptions.Parse(new[] { "--scan" }));

        Assert.Equal(0, codigo);

        var texto = saida.ToString();
        Assert.Contains("relatorio de varredura", texto);
        Assert.Contains("Nenhum processo foi encerrado", texto);
    }

    [Fact]
    public async Task Scan_em_json_e_json_valido()
    {
        var saida = new StringWriter();
        var runner = new CommandRunner(_provider, saida);

        await runner.ExecutarAsync(CommandLineOptions.Parse(new[] { "--scan", "--json" }));

        using var doc = System.Text.Json.JsonDocument.Parse(saida.ToString());
        Assert.True(doc.RootElement.GetProperty("itens").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Scan_grava_arquivo_quando_recebe_caminho()
    {
        var alvo = Path.Combine(_pasta, "relatorio.txt");
        var runner = new CommandRunner(_provider, new StringWriter());

        await runner.ExecutarAsync(CommandLineOptions.Parse(new[] { "--scan", alvo }));

        Assert.True(File.Exists(alvo));
        Assert.Contains("GameBoost", File.ReadAllText(alvo));
    }

    [Fact]
    public async Task Dry_run_do_modo_game_nao_encerra_nada_nem_grava_pendencias()
    {
        var modulo = _provider.GetRequiredService<GameModeModule>();
        var backup = _provider.GetRequiredService<Core.State.IStateBackup>();

        var scan = await modulo.ScanAsync(null, CancellationToken.None);
        var selecionados = scan.Itens.Where(i => i.PreMarcado).Select(i => i.Id).ToList();

        var antes = System.Diagnostics.Process.GetProcesses().Length;
        var resultado = await modulo.ApplyAsync(selecionados, dryRun: true, CancellationToken.None);
        var depois = System.Diagnostics.Process.GetProcesses().Length;

        Assert.True(resultado.DryRun);
        Assert.False(backup.TemPendencias);

        // Margem para processos que nascem e morrem sozinhos durante o teste.
        Assert.True(Math.Abs(antes - depois) < 15, $"processos antes={antes} depois={depois}");
    }

    [Fact]
    public void Leitura_de_energia_e_memoria_funciona_na_maquina_real()
    {
        var power = _provider.GetRequiredService<Core.Abstractions.IPowerService>();
        var memory = _provider.GetRequiredService<Core.Abstractions.IMemoryService>();

        Assert.NotNull(power.GetActivePlan());
        Assert.NotEmpty(power.GetPlans());

        var ram = memory.GetSnapshot();
        Assert.True(ram.TotalBytes > 0);
        Assert.True(ram.AvailableBytes > 0);
        Assert.InRange(ram.UsedPercent, 0, 100);
    }

    [Fact]
    public async Task Revert_all_sem_pendencias_e_no_op()
    {
        var saida = new StringWriter();
        var runner = new CommandRunner(_provider, saida);

        var codigo = await runner.ExecutarAsync(CommandLineOptions.Parse(new[] { "--revert-all" }));

        Assert.Equal(0, codigo);
        Assert.Contains("Nada a reverter", saida.ToString());
    }

    [Fact]
    public async Task Ajuda_lista_todos_os_comandos_da_secao_8()
    {
        var saida = new StringWriter();
        var runner = new CommandRunner(_provider, saida);

        await runner.ExecutarAsync(CommandLineOptions.Parse(new[] { "--help" }));

        var texto = saida.ToString();
        foreach (var comando in new[] { "--scan", "--report", "--clean", "--revert-all", "--gamemode", "--dry-run", "--json" })
            Assert.Contains(comando, texto);
    }
}
