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
    public void Inventario_de_apps_le_a_maquina_real_e_protege_o_que_deve()
    {
        var inventario = new Core.Modules.Uninstaller.AppInventory(
            _provider.GetRequiredService<Core.Abstractions.IRegistryService>(),
            _provider.GetRequiredService<Core.Logging.IGameBoostLogger>());

        // Sem medir tamanho: medir pasta a pasta levaria minutos.
        var apps = inventario.Listar(medirTamanho: false, CancellationToken.None);

        Assert.True(apps.Count > 10, $"so {apps.Count} apps: o inventario nao leu o registro");

        Assert.All(apps, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Nome));
            Assert.False(string.IsNullOrWhiteSpace(a.Id));
        });

        // Nada que seja runtime, driver ou antivirus pode ficar desprotegido.
        foreach (var app in apps)
        {
            var nome = app.Nome.ToLowerInvariant();

            if (nome.Contains("visual c++") || nome.Contains("webview2")
                || nome.Contains(".net runtime") || nome.Contains("kaspersky"))
            {
                Assert.True(app.Protegido, $"'{app.Nome}' deveria estar protegido");
                Assert.False(string.IsNullOrWhiteSpace(app.MotivoDaProtecao));
            }
        }

        // Bloatware pode ser sugerido, mas nunca protegido por engano.
        Assert.DoesNotContain(apps, a => a.Sugerido && a.Protegido);

        var daStore = apps.Count(a => a.Origem == Core.Modules.Uninstaller.AppOrigem.Store);
        var protegidos = apps.Count(a => a.Protegido);
        var sugeridos = apps.Count(a => a.Sugerido);
        var silenciosos = apps.Count(a => a.TemDesinstalacaoSilenciosa);

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "gb-medida-apps.txt"),
            $"MEDIDO: {apps.Count} apps ({daStore} da Store), {protegidos} protegidos, "
          + $"{sugeridos} sugeridos, {silenciosos} com desinstalacao silenciosa | "
          + "protegidos: " + string.Join("; ", apps.Where(a => a.Protegido).Take(6).Select(a => a.Nome)) + " | "
          + "sugeridos: " + string.Join("; ", apps.Where(a => a.Sugerido).Take(6).Select(a => a.Nome)));
    }

    [Fact]
    public void Driver_de_video_e_lido_do_registro_com_idade()
    {
        var info = _provider.GetRequiredService<Core.Modules.Drivers.GpuDriverInfo>();
        var placas = info.Listar();

        Assert.NotEmpty(placas);

        Assert.All(placas, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Nome));
            Assert.False(string.IsNullOrWhiteSpace(p.VersaoDoDriver));
        });

        // Adaptador virtual vem carimbado com 21/06/2006 e faria a tela
        // anunciar um driver de 20 anos numa maquina atualizada.
        Assert.DoesNotContain(placas, p =>
            Core.Modules.Drivers.GpuDriverInfo.EhAdaptadorVirtual(p.Nome));

        Assert.All(placas, p =>
            Assert.True(p.MesesDeIdade is null or < 120,
                $"{p.Nome} com driver de {p.MesesDeIdade} meses: parece adaptador virtual"));

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "gb-medida-drivers.txt"),
            "MEDIDO: " + string.Join(" | ", placas.Select(p =>
                $"{p.Nome} [{p.Fabricante}] driver {p.VersaoDoDriver}"
              + (p.VersaoDeMarketing is null ? string.Empty : $" = {p.VersaoDeMarketing}")
              + $", data {p.DataDoDriver:dd/MM/yyyy}, {p.MesesDeIdade} meses")));
    }

    [Theory]
    [InlineData("32.0.15.7680", "576.80")]
    [InlineData("31.0.15.3623", "536.23")]
    [InlineData("30.0.14.9709", "497.09")]
    public void Versao_da_nvidia_vira_a_versao_de_marketing(string bruta, string esperada)
        => Assert.Equal(esperada, Core.Modules.Drivers.GpuDriverInfo.Traduzir(
            Core.Modules.Drivers.FabricanteGpu.Nvidia, bruta));

    [Fact]
    public void Amd_e_intel_nao_ganham_versao_de_marketing_chutada()
    {
        // Nao ha correspondencia previsivel: devolver algo aqui seria inventar.
        Assert.Null(Core.Modules.Drivers.GpuDriverInfo.Traduzir(
            Core.Modules.Drivers.FabricanteGpu.Amd, "31.0.21921.1000"));
        Assert.Null(Core.Modules.Drivers.GpuDriverInfo.Traduzir(
            Core.Modules.Drivers.FabricanteGpu.Intel, "31.0.101.4502"));
    }

    [Fact]
    public async Task Winget_lista_atualizacoes_da_maquina_real()
    {
        var winget = _provider.GetRequiredService<Core.Modules.Uninstaller.WingetService>();
        var versao = await winget.DetectarAsync(CancellationToken.None);

        if (versao is null)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "gb-medida-winget.txt"),
                "MEDIDO: winget nao esta instalado nesta maquina.");
            return;
        }

        var lista = await winget.ListarAsync(CancellationToken.None);

        // Toda linha precisa ter id sem espaco e versao nova preenchida: se a
        // fatia de coluna sair errada, e aqui que aparece.
        Assert.All(lista, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Id));
            Assert.DoesNotContain(' ', a.Id);
            Assert.False(string.IsNullOrWhiteSpace(a.Nome));
            Assert.False(string.IsNullOrWhiteSpace(a.VersaoNova));
        });

        // A fonte so pode ser uma das que o winget conhece. Qualquer outra
        // coisa significa que a ultima coluna pegou pedaco da anterior.
        Assert.All(lista, a =>
            Assert.Contains(a.Fonte.ToLowerInvariant(), new[] { "winget", "msstore", "winget-font" }));

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "gb-medida-winget.txt"),
            $"MEDIDO: winget {versao}, {lista.Count} atualizacoes | "
          + string.Join(" | ", lista.Take(10).Select(a =>
                $"{a.Nome} [{a.Id}] {a.VersaoAtual} -> {a.VersaoNova} ({a.Fonte})"
              + (a.VersaoIncerta ? " INCERTA" : string.Empty))));
    }

    [Fact]
    public void Tabela_do_winget_e_lida_por_posicao_e_nao_por_idioma()
    {
        // Mesma tabela, cabecalho em portugues. Se o parser dependesse da
        // palavra "Available", isto devolveria zero.
        var saida = string.Join("\n", new[]
        {
            "Nome                 Id                     Versão   Disponível  Fonte",
            "-----------------------------------------------------------------------",
            "7-Zip 24.09 (x64)    7zip.7zip              24.09    26.03       winget",
            "Google Cloud SDK     Google.CloudSDK        Unknown  584.0.0     winget",
            "Epic Games Launcher  XP99VR1BPSBQJ2         1.3.175  1.3.189     msstore",
            "3 upgrades available."
        });

        var lista = Core.Modules.Uninstaller.WingetService.Interpretar(saida);

        Assert.Equal(3, lista.Count);
        Assert.Equal("7zip.7zip", lista[0].Id);
        Assert.Equal("26.03", lista[0].VersaoNova);

        // "Unknown" nao e uma versao: o winget nao sabe qual esta instalada.
        Assert.True(lista[1].VersaoIncerta);
        Assert.False(lista[0].VersaoIncerta);

        Assert.True(lista[2].DaStore);

        // O rodape nao pode virar uma linha da tabela.
        Assert.DoesNotContain(lista, a => a.Nome.Contains("upgrades available"));
    }

    [Theory]
    // O host de acesso remoto nao e navegador. Com casamento por prefixo cru,
    // "Google.Chrome" pegava "Google.ChromeRemoteDesktopHost" e ele aparecia na
    // tela como atualizacao de seguranca.
    [InlineData("Google.ChromeRemoteDesktopHost", false)]
    [InlineData("Google.Chrome", true)]
    [InlineData("Git.Git", true)]
    [InlineData("Git.GitLFS", false)]
    [InlineData("7zip.7zip", true)]
    [InlineData("Microsoft.Edge", true)]
    [InlineData("Microsoft.EdgeWebView2Runtime", false)]
    public void Id_do_winget_so_casa_em_fronteira_de_ponto(string id, bool esperado)
    {
        var alvo = new Core.Modules.Uninstaller.AtualizacaoDisponivel
        {
            Nome = id, Id = id, VersaoAtual = "1.0", VersaoNova = "2.0", Fonte = "winget"
        };

        Assert.Equal(esperado, Core.Modules.Uninstaller.UpdateCatalog.EhDeSeguranca(alvo));
    }

    [Theory]
    // Familia inteira continua casando: o padrao termina em ponto de proposito.
    [InlineData("Intel.IntelDriverAndSupportAssistant", Core.Modules.Uninstaller.ClasseDeAtualizacao.Driver)]
    [InlineData("Microsoft.DotNet.SDK.9", Core.Modules.Uninstaller.ClasseDeAtualizacao.Runtime)]
    [InlineData("Valve.Steam", Core.Modules.Uninstaller.ClasseDeAtualizacao.AtualizaSozinho)]
    [InlineData("RARLab.WinRAR", Core.Modules.Uninstaller.ClasseDeAtualizacao.Normal)]
    public void Classificacao_separa_driver_runtime_e_auto_update(
        string id, Core.Modules.Uninstaller.ClasseDeAtualizacao esperada)
    {
        var alvo = new Core.Modules.Uninstaller.AtualizacaoDisponivel
        {
            Nome = id, Id = id, VersaoAtual = "1.0", VersaoNova = "2.0", Fonte = "winget"
        };

        Assert.Equal(esperada, Core.Modules.Uninstaller.UpdateCatalog.Classificar(alvo));
    }

    [Fact]
    public void Codigo_de_saida_do_winget_vira_frase_em_portugues()
    {
        var texto = Core.Modules.Uninstaller.WingetService.Explicar(
            unchecked((int)0x8A150056), string.Empty, string.Empty);

        Assert.Contains("aberto", texto, StringComparison.OrdinalIgnoreCase);

        // Codigo desconhecido nao pode virar mensagem vazia.
        var outro = Core.Modules.Uninstaller.WingetService.Explicar(-12345, string.Empty, string.Empty);
        Assert.Contains("-12345", outro);
    }

    [Fact]
    public async Task Nat_e_classificado_no_vocabulario_dos_jogos()
    {
        var nat = _provider.GetRequiredService<Core.Modules.Network.NatDiagnostics>();
        var resultado = await nat.DiagnosticarAsync(CancellationToken.None);

        // O texto nunca pode sair vazio: ele e o produto desta tela.
        Assert.False(string.IsNullOrWhiteSpace(resultado.Explicacao));
        Assert.False(string.IsNullOrWhiteSpace(resultado.NomeCurto));
        Assert.Contains(resultado.Semaforo, new[] { "verde", "amarelo", "vermelho", "cinza" });

        // Se o STUN respondeu, o endereco publico nao pode ser privado: seria
        // sinal de que lemos o campo errado do pacote.
        if (resultado.EnderecoPublico is not null)
        {
            var b = resultado.EnderecoPublico.GetAddressBytes();
            var privado = b[0] == 10
                       || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                       || (b[0] == 192 && b[1] == 168);

            Assert.False(privado,
                $"STUN devolveu {resultado.EnderecoPublico}, que e endereco privado");
        }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "gb-medida-nat.txt"),
            $"MEDIDO: NAT {resultado.NomeCurto} ({resultado.Semaforo}) | "
          + $"publico {resultado.EnderecoPublico?.ToString() ?? "nao detectado"} | "
          + $"local {resultado.EnderecoLocal} | saltos privados {resultado.SaltosPrivados} | "
          + $"porta estavel {resultado.PortaEstavel} | teredo {resultado.Teredo} | "
          + $"upnp {resultado.UpnpDisponivel} | firewall {string.Join(", ", resultado.PerfisDeFirewallAtivos)}");
    }

    [Fact]
    public async Task Teste_de_velocidade_respeita_a_permissao_de_rede()
    {
        var teste = _provider.GetRequiredService<Core.Modules.Network.SpeedTest>();

        // A permissao e desligada por padrao (regra 8). Sem ela o teste nao sai
        // para a rede e devolve o motivo, em vez de falhar calado.
        if (!teste.Permitido)
        {
            var bloqueado = await teste.MedirAsync(null, CancellationToken.None);

            Assert.False(bloqueado.Funcionou);
            Assert.NotNull(bloqueado.Erro);
            Assert.Equal(0, bloqueado.BytesBaixados);

            File.WriteAllText(Path.Combine(Path.GetTempPath(), "gb-medida-velocidade.txt"),
                "MEDIDO: permissao de rede desligada, teste nao saiu para a internet. " + bloqueado.Erro);
            return;
        }

        var resultado = await teste.MedirAsync(null, CancellationToken.None);

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "gb-medida-velocidade.txt"),
            $"MEDIDO: download {resultado.DownloadMbps} Mbps, upload {resultado.UploadMbps} Mbps, "
          + $"{resultado.BytesBaixados / 1024 / 1024} MB baixados em {resultado.Duracao.TotalSeconds:0.0}s");
    }

    [Fact]
    public async Task Rede_mede_latencia_e_dns_de_verdade()
    {
        var modulo = _provider.GetRequiredService<Core.Modules.Network.NetworkModule>();
        var resultado = await modulo.ScanAsync(null, CancellationToken.None);

        Assert.NotEmpty(resultado.Itens);
        Assert.DoesNotContain(resultado.Itens, i => i.PreMarcado);

        // O reset de Winsock nao tem desfazer: tem que estar como risco alto.
        var winsock = resultado.Itens.Single(i => i.Id == "rede:winsock");
        Assert.Equal(Core.Modules.RiskLevel.Alto, winsock.Risco);

        // Ping ate a internet precisa ter respondido; se nem isso funciona, a
        // medicao nao vale nada e o teste tem que gritar.
        var internet = modulo.UltimoPing.Where(p => p.Rotulo.StartsWith("Internet")).ToList();
        Assert.NotEmpty(internet);

        var texto = "MEDIDO: " + resultado.Resumo + " | ping: "
            + string.Join("; ", modulo.UltimoPing.Select(p =>
                $"{p.Rotulo} {p.Alvo} {(p.Respondeu ? $"{p.MediaMs}ms jitter {p.JitterMs}ms perda {p.PerdaPercentual}%" : "sem resposta")}"))
            + " | dns: "
            + string.Join("; ", modulo.UltimoDns.Select(d =>
                $"{d.Nome} {d.Endereco} {(d.Respondeu ? d.MediaMs + "ms" : "sem resposta")}{(d.EmUso ? " (em uso)" : string.Empty)}"))
            + " | wifi: " + (modulo.UltimoWifi is null ? "cabo" : $"{modulo.UltimoWifi.Ssid} {modulo.UltimoWifi.Padrao} {modulo.UltimoWifi.Banda} {modulo.UltimoWifi.SinalPercentual}%")
            + " | conexoes: "
            + string.Join("; ", modulo.UltimosConsumidores.Take(6).Select(c => $"{c.Nome}={c.Conexoes}"));

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "gb-medida-rede.txt"), texto);
    }

    [Fact]
    public async Task Tweaks_leem_o_estado_real_da_maquina_e_nao_pre_marcam_nada()
    {
        var modulo = _provider.GetRequiredService<Core.Modules.Tweaks.TweaksModule>();
        var resultado = await modulo.ScanAsync(null, CancellationToken.None);

        Assert.NotEmpty(resultado.Itens);

        // Regra 3: nenhum ajuste do sistema vem marcado, nem o recomendado.
        Assert.DoesNotContain(resultado.Itens, i => i.PreMarcado);

        // Regra 4: todo item precisa dizer o que faz e como desfazer.
        Assert.All(resultado.Itens, i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Descricao));
            Assert.False(string.IsNullOrWhiteSpace(i.ComoDesfazer));
        });

        // O VBS aparece, mas so de leitura: alterar isso nao e decisao daqui.
        var vbs = resultado.Itens.Single(i => i.Id == "tweak:vbs");
        Assert.True(vbs.Bloqueado);

        // Windows Update e antivirus tem que estar bloqueados.
        foreach (var nome in new[] { "servico:wuauserv", "servico:WinDefend" })
        {
            var item = resultado.Itens.FirstOrDefault(i => i.Id == nome);
            if (item is not null)
            {
                Assert.True(item.Bloqueado, $"{nome} deveria estar bloqueado");
                Assert.False(string.IsNullOrWhiteSpace(item.MotivoBloqueio));
            }
        }

        var ligados = resultado.Itens
            .Where(i => i.Payload is Core.Modules.Tweaks.TweaksModule.TweakEstado { Ligado: true })
            .Select(i => i.Titulo)
            .ToList();

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "gb-medida-tweaks.txt"),
            $"MEDIDO: {resultado.Resumo} | ja ativos: {string.Join("; ", ligados)} | "
          + "servicos: " + string.Join("; ", resultado.Itens
                .Where(i => i.Id.StartsWith("servico:", StringComparison.Ordinal))
                .Select(i => $"{i.Titulo}={i.GanhoEstimado}{(i.Bloqueado ? " (bloqueado)" : string.Empty)}")));
    }

    [Fact]
    public async Task Dry_run_dos_tweaks_nao_grava_nada_no_registro()
    {
        var modulo = _provider.GetRequiredService<Core.Modules.Tweaks.TweaksModule>();
        var backup = _provider.GetRequiredService<Core.State.IStateBackup>();

        await modulo.ScanAsync(null, CancellationToken.None);

        var antes = backup.Todos.Count;

        // Um tweak de HKCU, que nao precisa de elevacao: se o dry-run vazasse
        // escrita, seria justamente aqui.
        var resultado = await modulo.ApplyAsync(
            new[] { "tweak:aceleracao-mouse" }, dryRun: true, CancellationToken.None);

        Assert.True(resultado.DryRun);
        Assert.Equal(antes, backup.Todos.Count);
        Assert.All(resultado.Acoes, a => Assert.True(a.Sucesso));
    }

    [Fact]
    public async Task Tamanho_total_dos_apps_nao_passa_da_capacidade_dos_discos()
    {
        // A primeira tela real da Fase 4 anunciou "1812,4 GB no total" numa
        // maquina com 953 GB. Duas causas: app cuja InstallLocation aponta para
        // uma pasta generica (media o disco inteiro) e varios apps declarando a
        // MESMA pasta (contada uma vez por app).
        var modulo = _provider.GetRequiredService<Core.Modules.Uninstaller.UninstallerModule>();
        var resultado = await modulo.ScanAsync(null, CancellationToken.None);

        var capacidade = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Sum(d => d.TotalSize);

        var somado = resultado.Itens.Sum(i => i.GanhoBytes);

        Assert.True(somado <= capacidade,
            $"os apps somam {somado / 1024.0 / 1024 / 1024:0.0} GB e os discos so tem "
          + $"{capacidade / 1024.0 / 1024 / 1024:0.0} GB");

        // Nenhum app sozinho pode responder por mais de um terco do disco: se
        // responder, foi pasta generica medida como se fosse dele.
        var teto = capacidade / 3;
        var gigante = resultado.Itens.FirstOrDefault(i => i.GanhoBytes > teto);

        Assert.True(gigante is null,
            $"'{gigante?.Titulo}' sozinho tem {gigante?.GanhoBytes / 1024.0 / 1024 / 1024:0.0} GB");

        // A tela existe para mostrar o que ocupa espaco: o maior tem que estar
        // no topo. Antes da correcao o inventario ordenava pelo tamanho
        // DECLARADO no registro, medido depois.
        var tamanhos = resultado.Itens.Select(i => i.GanhoBytes).ToList();
        Assert.True(
            tamanhos.SequenceEqual(tamanhos.OrderByDescending(t => t)),
            "a lista de apps nao esta em ordem decrescente de tamanho");

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "gb-medida-apps-total.txt"),
            $"MEDIDO: {resultado.Resumo} | discos: {capacidade / 1024.0 / 1024 / 1024:0.0} GB | "
          + "maiores: " + string.Join("; ", resultado.Itens
                .OrderByDescending(i => i.GanhoBytes).Take(8)
                .Select(i => $"{i.Titulo} {(i.GanhoBytes / 1024.0 / 1024 / 1024):0.0}GB")));
    }

    [Theory]
    [InlineData(@"C:\Program Files", false)]
    [InlineData(@"C:\", false)]
    [InlineData("C:", false)]
    [InlineData(@"C:\Program Files\Discord", true)]
    public void Pasta_generica_nao_conta_como_pasta_do_app(string pasta, bool esperado)
        => Assert.Equal(esperado, Core.Modules.Uninstaller.UninstallerModule.PastaConfiavel(pasta));

    [Fact]
    public void Restos_de_apps_antigos_sao_encontrados_sem_falso_positivo_obvio()
    {
        var log = _provider.GetRequiredService<Core.Logging.IGameBoostLogger>();
        var inventario = new Core.Modules.Uninstaller.AppInventory(
            _provider.GetRequiredService<Core.Abstractions.IRegistryService>(), log);
        var scanner = new Core.Modules.Uninstaller.LeftoverScanner(log);

        var instalados = inventario.Listar(medirTamanho: false, CancellationToken.None);
        var restos = scanner.Varrer(instalados, null, CancellationToken.None);

        // Nada de Microsoft, NVIDIA e afins pode entrar: sao as pastas ignoradas.
        Assert.DoesNotContain(restos, r =>
            r.Nome.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)
            || r.Nome.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase)
            || r.Nome.Equals("Packages", StringComparison.OrdinalIgnoreCase)
            || r.Nome.Equals("Temp", StringComparison.OrdinalIgnoreCase));

        Assert.All(restos, r =>
        {
            Assert.True(r.Bytes >= 1024 * 1024, "pasta pequena demais entrou na lista");
            Assert.True(Directory.Exists(r.Caminho), $"caminho inexistente: {r.Caminho}");
        });

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "gb-medida-restos.txt"),
            $"MEDIDO: {restos.Count} pastas sem app correspondente, "
          + $"{(restos.Sum(r => r.Bytes) / 1024.0 / 1024):0} MB | "
          + string.Join(" | ", restos.Take(8).Select(r =>
                $"{r.Nome} ({r.Local}) {(r.Bytes / 1024.0 / 1024):0}MB, {r.Idade}")));
    }

    [Fact]
    public async Task Inicializacao_le_a_maquina_real_e_protege_driver_e_antivirus()
    {
        var modulo = _provider.GetRequiredService<Core.Modules.Startup.StartupModule>();

        var scan = await modulo.ScanAsync(null, CancellationToken.None);

        Assert.NotEmpty(scan.Itens);

        // Regra 3: nada vem marcado. O usuario decide o que abre com a maquina.
        Assert.DoesNotContain(scan.Itens, i => i.PreMarcado);

        // O aviso sobre a estimativa grosseira e obrigatorio (regra 4).
        Assert.Contains(scan.Avisos, a => a.Contains("grosseira"));

        // Driver e antivirus tem que aparecer bloqueados.
        foreach (var item in scan.Itens)
        {
            var texto = (item.Titulo + " " + item.Descricao).ToLowerInvariant();

            if (texto.Contains("securityhealth") || texto.Contains("kaspersky")
                || texto.Contains("realtek"))
            {
                Assert.True(item.Bloqueado, $"'{item.Titulo}' deveria estar bloqueado");
            }
        }

        var protegidos = scan.Itens.Count(i => i.Bloqueado);
        var sugeridos = scan.Itens.Count(i => i.Categoria == "Sugestões");

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "gb-medida-startup.txt"),
            $"MEDIDO: {scan.Itens.Count} entradas, {protegidos} bloqueadas, {sugeridos} sugeridas | "
          + scan.Resumo + " | "
          + string.Join(" | ", scan.Itens.Take(8).Select(i => $"{i.Titulo} [{i.Categoria}] {i.GanhoEstimado}")));
    }

    [Fact]
    public async Task Dry_run_da_inicializacao_nao_toca_no_registro()
    {
        var modulo = _provider.GetRequiredService<Core.Modules.Startup.StartupModule>();
        var backup = _provider.GetRequiredService<Core.State.IStateBackup>();

        var scan = await modulo.ScanAsync(null, CancellationToken.None);
        var alvos = scan.Itens.Where(i => !i.Bloqueado).Select(i => i.Id).ToList();

        var resultado = await modulo.ApplyAsync(alvos, dryRun: true, CancellationToken.None);

        Assert.True(resultado.DryRun);
        Assert.False(backup.TemPendencias, "dry-run nao pode gravar ChangeRecord");
        Assert.All(resultado.Acoes, a => Assert.Contains("Desativaria", a.Detalhe));
    }

    [Fact]
    public void Rot13_do_userassist_vai_e_volta()
    {
        // O UserAssist guarda os nomes em ROT13; errar isso daria lixo no lugar
        // do nome do executavel.
        var original = @"C:\Program Files\Apppp.exe";
        var codificado = Core.Modules.Uninstaller.AppInventory.Rot13(original);

        Assert.NotEqual(original, codificado);
        Assert.Equal(original, Core.Modules.Uninstaller.AppInventory.Rot13(codificado));
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
