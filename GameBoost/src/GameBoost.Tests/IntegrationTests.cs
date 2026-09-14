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
    public void Coleta_nativa_de_processos_bate_com_a_api_gerenciada()
    {
        // Guarda dos deslocamentos de SYSTEM_PROCESS_INFORMATION: se algum
        // estiver errado, sai lixo silencioso em vez de erro.
        var coletor = _provider.GetRequiredService<Core.Modules.Bottleneck.IMetricsCollector>();

        coletor.Coletar();
        Thread.Sleep(600);
        var snapshot = coletor.Coletar();

        var pelaApi = System.Diagnostics.Process.GetProcesses()
            .Select(p => { var n = p.ProcessName; p.Dispose(); return n; })
            .Select(Core.Safety.ProtectedProcesses.Normalizar)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(snapshot.Processos);

        // Nomes conhecidos precisam aparecer nos dois lados.
        var nativos = snapshot.Processos.Select(p => p.Nome).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("explorer", nativos, StringComparer.OrdinalIgnoreCase);

        // A grande maioria dos nomes tem que coincidir: processos nascem e
        // morrem entre as duas leituras, entao nao da para exigir 100%.
        var coincidem = nativos.Count(n => pelaApi.Contains(n));
        Assert.True(coincidem >= nativos.Count * 0.85,
            $"so {coincidem} de {nativos.Count} nomes coincidiram: deslocamentos suspeitos");

        // Valores tem que ser plausiveis, nao lixo de memoria.
        Assert.All(snapshot.Processos, p =>
        {
            Assert.InRange(p.CpuPercent, 0, 100);
            Assert.InRange(p.WorkingSetBytes, 0, 512L * 1024 * 1024 * 1024);
            Assert.False(string.IsNullOrWhiteSpace(p.Nome));
        });

        // A soma do working set nao pode passar de varias vezes a RAM da maquina.
        var somaRam = snapshot.Processos.Sum(p => p.WorkingSetBytes);
        Assert.True(somaRam < snapshot.RamTotalBytes * 8,
            "soma de working set absurda: deslocamento de WorkingSetSize suspeito");
    }

    [Fact]
    public void Snapshot_real_tem_metricas_plausiveis()
    {
        var coletor = _provider.GetRequiredService<Core.Modules.Bottleneck.IMetricsCollector>();

        coletor.Coletar();
        Thread.Sleep(600);
        var s = coletor.Coletar();

        Assert.InRange(s.CpuPercent, 0, 100);
        Assert.True(s.RamTotalBytes > 0);
        Assert.InRange(s.RamUsadaPercent, 0, 100);
        Assert.InRange(s.DiscoSistemaUsadoPercent, 0, 100);
        Assert.True(s.NucleosLogicos > 0);

        // Indisponivel e uma resposta valida; zero disfarcado de medida nao e.
        if (s.GpuPercent.Disponivel)
            Assert.InRange(s.GpuPercent.Valor, 0, 100);
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
    public async Task Informacoes_do_sistema_leem_a_maquina_real_sem_alterar_nada()
    {
        var servico = _provider.GetRequiredService<Core.Modules.Tools.QuickToolsService>();

        var resultado = await servico.ExecutarAsync(
            Core.Modules.Tools.QuickToolsCatalog.InfoDoSistema, null, CancellationToken.None);

        Assert.True(resultado.Sucesso);
        Assert.NotNull(resultado.Detalhe);

        // Precisa conter dados de verdade, nao "desconhecido" em tudo.
        foreach (var secao in new[] { "=== Sistema ===", "=== Processador ===", "=== Memoria ===",
                                      "=== Video ===", "=== Discos ===" })
        {
            Assert.Contains(secao.Replace("Memoria", "Memória").Replace("Video", "Vídeo"),
                resultado.Detalhe!);
        }

        Assert.Contains("Windows", resultado.Detalhe!);
        Assert.DoesNotContain("Núcleos lógicos   : 0", resultado.Detalhe!);
    }

    [Fact]
    public async Task Teste_de_disco_mede_e_apaga_o_arquivo_temporario()
    {
        var servico = _provider.GetRequiredService<Core.Modules.Tools.QuickToolsService>();

        var antes = Directory.GetFiles(Path.GetTempPath(), "gameboost-disco-*.tmp").Length;

        var linhas = new List<string>();
        var resultado = await servico.ExecutarAsync(
            Core.Modules.Tools.QuickToolsCatalog.TesteDeDisco,
            new Progress<string>(linhas.Add),
            CancellationToken.None);

        var depois = Directory.GetFiles(Path.GetTempPath(), "gameboost-disco-*.tmp").Length;

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.Contains("MB/s", resultado.Mensagem);
        Assert.NotNull(resultado.Detalhe);

        // O arquivo de 1 GB nao pode ficar para tras.
        Assert.Equal(antes, depois);
    }

    [Fact]
    [Trait("Category", "Lento")]
    public void Varredura_de_disco_real_mede_o_volume_do_sistema()
    {
        var log = _provider.GetRequiredService<Core.Logging.IGameBoostLogger>();
        var scanner = new Core.Modules.DiskAnalyzer.DiskScanner(log);

        var raiz = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
        var resultado = scanner.Varrer(raiz, null, CancellationToken.None);

        Assert.True(resultado.TotalDeArquivos > 1000, "poucos arquivos: a varredura nao entrou no disco");
        Assert.True(resultado.Raiz.Tamanho > 0);

        // O que foi somado nao pode passar do que cabe no volume.
        Assert.True(resultado.Raiz.Tamanho <= resultado.EspacoTotal,
            $"somou {resultado.Raiz.Tamanho} num volume de {resultado.EspacoTotal}");

        // A soma tem que bater com o espaco ocupado, com folga para o que a
        // varredura nao alcanca: System Volume Information, quotas e ACLs.
        var ocupado = resultado.EspacoTotal - resultado.EspacoLivre;
        var proporcao = (double)resultado.Raiz.Tamanho / ocupado;
        Assert.InRange(proporcao, 0.55, 1.05);

        // Numeros reais, para a documentacao nao chutar desempenho.
        var linha = $"MEDIDO: {resultado.TotalDeArquivos} arquivos, "
                  + $"{resultado.Raiz.Tamanho / 1024.0 / 1024 / 1024:0.0} GB somados, "
                  + $"volume de {resultado.EspacoTotal / 1024.0 / 1024 / 1024:0} GB, "
                  + $"em {resultado.Duracao.TotalSeconds:0.0}s, "
                  + $"{resultado.PastasIgnoradas} pastas sem acesso";
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "gb-medida-disco.txt"), linha);

        // A arvore tem que estar consolidada de baixo para cima.
        var maiorPasta = resultado.Raiz.MaioresPastas(1).FirstOrDefault();
        Assert.NotNull(maiorPasta);
        Assert.True(maiorPasta!.Tamanho > 0);

        var maiorArquivo = resultado.Raiz.MaioresArquivos(1).FirstOrDefault();
        Assert.NotNull(maiorArquivo);
        Assert.False(maiorArquivo!.EhPasta);
    }

    [Fact]
    public void Biblioteca_de_jogos_le_os_launchers_instalados()
    {
        var log = _provider.GetRequiredService<Core.Logging.IGameBoostLogger>();
        var biblioteca = new Core.Modules.DiskAnalyzer.GameLibrary(log);

        var jogos = biblioteca.Listar();

        // Cada entrada precisa ser plausivel, venha de qual launcher vier.
        Assert.All(jogos, j =>
        {
            Assert.False(string.IsNullOrWhiteSpace(j.Nome));
            Assert.False(string.IsNullOrWhiteSpace(j.Launcher));
            Assert.False(string.IsNullOrWhiteSpace(j.Pasta));
            Assert.True(j.Bytes >= 0);
            Assert.InRange(j.Bytes, 0, 2L * 1024 * 1024 * 1024 * 1024);
        });

        var resumo = jogos.Count == 0
            ? "MEDIDO: nenhum jogo encontrado nos launchers"
            : "MEDIDO: " + jogos.Count + " jogos, "
              + (jogos.Sum(j => j.Bytes) / 1024.0 / 1024 / 1024).ToString("0.0") + " GB no total | "
              + string.Join(" | ", jogos.Take(6).Select(j =>
                    $"{j.Launcher}: {j.Nome} {(j.Bytes / 1024.0 / 1024 / 1024):0.0}GB, {j.QuandoJogou}"));

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "gb-medida-jogos.txt"), resumo);
    }

    [Fact]
    public void Arquivos_especiais_do_windows_sao_explicados()
    {
        var raiz = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
        var especiais = Core.Modules.DiskAnalyzer.SpecialFiles.Encontrar(raiz);

        // pagefile.sys existe em praticamente toda instalacao.
        Assert.All(especiais, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.OQueE));
            Assert.False(string.IsNullOrWhiteSpace(e.ComoRemover));
            Assert.True(e.Bytes > 0);
        });
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
