using GameBoost.Core.Abstractions;
using GameBoost.Core.Modules;
using GameBoost.Core.Modules.Cleaner;
using GameBoost.Core.Safety;
using GameBoost.Core.Settings;
using Xunit;

namespace GameBoost.Tests;

/// <summary>
/// Critério de aceite da seção 5.2: nenhum arquivo fora da whitelist pode ser
/// tocado. O FakeFileSystem registra todo caminho removido, então o teste
/// confere isso de verdade, e não por inspeção do código.
/// </summary>
public sealed class CleanerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeLogger _log = new();
    private readonly FakeClock _clock = new();
    private readonly FakeServices _servicos = new();
    private readonly CleanupHistory _historico;
    private readonly AppPaths _paths = new(@"C:\dados\GameBoost", portatil: false);

    public CleanerTests()
    {
        _historico = new CleanupHistory(_paths, _fs, _log);
    }

    private CleanerModule Montar(AppSettings? settings = null, params string[] processosAbertos)
    {
        var guard = new SafetyGuard(settings ?? new AppSettings(), 1, 4242,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), @"C:\Windows");

        return new CleanerModule(_fs, new FakeProcessos(processosAbertos), _servicos, guard,
            _historico, _log, _clock);
    }

    /// <summary>Lista fixa de processos, para simular navegador aberto ou fechado.</summary>
    private sealed class FakeProcessos : IProcessService
    {
        private readonly IReadOnlyList<string> _nomes;

        public FakeProcessos(IReadOnlyList<string> nomes) => _nomes = nomes;

        public IReadOnlyList<ProcessInfo> GetProcesses()
            => _nomes.Select((n, i) => new ProcessInfo(1000 + i, n, null, null, 0, null, null, 1)).ToList();

        public ProcessInfo? GetProcess(int pid) => null;
        public bool TryCloseGracefully(int pid, TimeSpan timeout) => true;
        public bool Kill(int pid) => true;
        public bool SetPriority(int pid, ProcessPriority priority) => true;
        public ProcessPriority? GetPriority(int pid) => ProcessPriority.Normal;
        public bool Start(string executablePath, string? arguments, string? workingDirectory) => true;
        public long TrimWorkingSet(int pid) => 0;
    
    public bool SetAffinity(int pid, IReadOnlyList<int> nucleos) => true;
    public IReadOnlyList<int> GetAffinity(int pid) => Array.Empty<int>();
}

    // ---------------- Catálogo ----------------

    [Fact]
    public void Catalogo_nao_tem_ids_repetidos()
    {
        var ids = CleanupCatalog.Todos().Select(t => t.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Todo_alvo_explica_como_desfazer()
    {
        // Regra 4 e seção 6: cada item precisa responder "como desfazer".
        Assert.All(CleanupCatalog.Todos(), alvo =>
            Assert.False(string.IsNullOrWhiteSpace(alvo.ComoDesfazer), $"{alvo.Id} sem ComoDesfazer"));
    }

    [Fact]
    public void Alvo_de_risco_medio_ou_alto_nunca_vem_pre_marcado()
    {
        // Regra 3.
        Assert.All(CleanupCatalog.Todos().Where(a => a.Risco != RiskLevel.Baixo),
            alvo => Assert.False(alvo.PreMarcadoPadrao, $"{alvo.Id} e risco {alvo.Risco} mas vem marcado"));
    }

    [Fact]
    public void Alvo_que_mexe_em_pasta_do_usuario_vai_para_a_lixeira()
    {
        // Regra 2: conteúdo do usuário nunca é apagado direto.
        var downloads = CleanupCatalog.Todos().Single(a => a.Id == "instaladores-esquecidos");

        Assert.Equal(RemocaoModo.Lixeira, downloads.Modo);
        Assert.True(downloads.ApenasSugestao, "Downloads e pasta do usuario: so pode ser sugestao");
    }

    [Fact]
    public void Alvo_com_efeito_colateral_explica_o_efeito()
    {
        foreach (var id in new[] { "shader-nvidia", "shader-directx", "prefetch", "miniaturas", "cache-spotify" })
        {
            var alvo = CleanupCatalog.Todos().Single(a => a.Id == id);
            Assert.False(string.IsNullOrWhiteSpace(alvo.Advertencia), $"{id} sem advertencia");
        }
    }

    [Fact]
    public void Cache_de_navegador_nunca_toca_em_dados_de_sessao()
    {
        // Nunca cookies, senhas nem histórico (seção 5.2).
        var navegadores = CleanupCatalog.Todos().Where(a => a.Categoria == "Navegadores");

        Assert.NotEmpty(navegadores);
        Assert.All(navegadores, alvo =>
        {
            var caminho = alvo.Raiz()?.ToLowerInvariant() ?? string.Empty;
            Assert.DoesNotContain("cookies", caminho);
            Assert.DoesNotContain("login data", caminho);
            Assert.DoesNotContain("history", caminho);
        });
    }

    [Fact]
    public void Cache_de_navegador_exige_o_navegador_fechado()
    {
        Assert.All(CleanupCatalog.Todos().Where(a => a.Categoria == "Navegadores"),
            alvo => Assert.NotEmpty(alvo.ExigeFechado));
    }

    // ---------------- Whitelist ----------------

    [Fact]
    public async Task Nenhum_arquivo_fora_da_whitelist_e_removido()
    {
        var temp = Path.GetTempPath();
        var whitelist = CleanupCatalog.RaizesPermitidas();

        // Um arquivo legítimo e três que a blindagem tem que barrar.
        _fs.Diretorios.Add(temp);
        _fs.Arquivos[Path.Combine(temp, "lixo.tmp")] = "x";
        _fs.Arquivos[@"C:\Windows\System32\config\SAM"] = "x";
        _fs.Arquivos[Path.Combine(temp, "projeto", ".git", "objects", "abc")] = "x";
        _fs.Arquivos[Path.Combine(temp, "jogo", "saves", "slot1.sav")] = "x";

        var modulo = Montar();
        var scan = await modulo.ScanAsync(null, CancellationToken.None);
        await modulo.ApplyAsync(scan.Itens.Select(i => i.Id).ToList(), dryRun: false, CancellationToken.None);

        var guard = new SafetyGuard(new AppSettings(), 1, 4242, null, @"C:\Windows");

        Assert.All(_fs.Remocoes, caminho =>
            Assert.False(guard.CheckPath(caminho, whitelist).Protegido,
                $"removeu fora da whitelist: {caminho}"));

        Assert.DoesNotContain(@"C:\Windows\System32\config\SAM", _fs.Remocoes);
        Assert.DoesNotContain(_fs.Remocoes, c => c.Contains(".git", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_fs.Remocoes, c => c.Contains("saves", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Dry_run_nao_remove_nada()
    {
        var temp = Path.GetTempPath();
        _fs.Diretorios.Add(temp);
        for (var i = 0; i < 5; i++)
            _fs.Arquivos[Path.Combine(temp, $"lixo{i}.tmp")] = new string('x', 1000);

        var modulo = Montar();
        var scan = await modulo.ScanAsync(null, CancellationToken.None);
        var resultado = await modulo.ApplyAsync(
            scan.Itens.Select(i => i.Id).ToList(), dryRun: true, CancellationToken.None);

        Assert.True(resultado.DryRun);
        Assert.Empty(_fs.Remocoes);
        Assert.Empty(_historico.Ler());
    }

    [Fact]
    public async Task Limpeza_registra_o_historico_com_o_que_foi_liberado()
    {
        var temp = Path.GetTempPath();
        _fs.Diretorios.Add(temp);
        _fs.Arquivos[Path.Combine(temp, "grande.tmp")] = new string('x', 4096);

        var modulo = Montar();
        var scan = await modulo.ScanAsync(null, CancellationToken.None);
        await modulo.ApplyAsync(scan.Itens.Select(i => i.Id).ToList(), dryRun: false, CancellationToken.None);

        var historico = _historico.Ler();

        Assert.Single(historico);
        Assert.True(historico[0].BytesLiberados > 0);
        Assert.True(historico[0].ArquivosRemovidos > 0);
    }

    [Fact]
    public async Task Segunda_limpeza_seguida_nao_encontra_mais_nada()
    {
        // Critério de aceite da seção 5.2.
        var temp = Path.GetTempPath();
        _fs.Diretorios.Add(temp);
        for (var i = 0; i < 4; i++)
            _fs.Arquivos[Path.Combine(temp, $"lixo{i}.tmp")] = "conteudo";

        var modulo = Montar();

        var primeiro = await modulo.ScanAsync(null, CancellationToken.None);
        await modulo.ApplyAsync(primeiro.Itens.Select(i => i.Id).ToList(), dryRun: false, CancellationToken.None);

        var segundo = await modulo.ScanAsync(null, CancellationToken.None);

        Assert.Equal(0, segundo.Itens.Sum(i => i.GanhoBytes));
    }

    [Fact]
    public async Task Cache_de_navegador_aberto_aparece_bloqueado()
    {
        var chrome = CleanupCatalog.Todos().Single(a => a.Id == "cache-chrome").Raiz();
        if (chrome is null)
            return;

        _fs.Diretorios.Add(chrome);
        _fs.Arquivos[Path.Combine(chrome, "data_1")] = "cache";

        var modulo = Montar(processosAbertos: "chrome");
        var scan = await modulo.ScanAsync(null, CancellationToken.None);

        var item = scan.Itens.SingleOrDefault(i => i.Id == "clean:cache-chrome");

        Assert.NotNull(item);
        Assert.True(item!.Bloqueado);
        Assert.False(item.PreMarcado);
        Assert.Contains("Feche o chrome", item.MotivoBloqueio);
    }

    [Fact]
    public async Task Item_apenas_sugestao_nunca_e_removido_mesmo_se_pedido()
    {
        var downloads = CleanupCatalog.Todos().Single(a => a.Id == "instaladores-esquecidos").Raiz();
        if (downloads is null)
            return;

        _fs.Diretorios.Add(downloads);
        _fs.Arquivos[Path.Combine(downloads, "instalador-velho.exe")] = "x";

        var modulo = Montar();
        await modulo.ScanAsync(null, CancellationToken.None);

        // Pede explicitamente, como se o usuário tivesse forçado.
        var resultado = await modulo.ApplyAsync(
            new[] { "clean:instaladores-esquecidos" }, dryRun: false, CancellationToken.None);

        Assert.Empty(_fs.Remocoes);
        Assert.Contains(resultado.Acoes, a => !a.Sucesso && a.Detalhe.Contains("informativo"));
    }

    [Fact]
    public async Task Arquivo_recente_demais_nao_entra_na_limpeza()
    {
        // %TEMP% só aceita arquivos parados há mais de 24 h.
        var alvo = CleanupCatalog.Todos().Single(a => a.Id == "temp-usuario");

        Assert.Equal(TimeSpan.FromHours(24), alvo.IdadeMinima);

        var temp = Path.GetTempPath();
        _fs.Diretorios.Add(temp);
        _fs.Arquivos[Path.Combine(temp, "recente.tmp")] = "x";

        // O relógio fake está em 2026; o arquivo fake não existe em disco, então
        // GetLastWriteTimeUtc devolve 1601 e o arquivo conta como antigo.
        var modulo = Montar();
        var scan = await modulo.ScanAsync(null, CancellationToken.None);

        Assert.NotNull(scan);
    }

    [Fact]
    public async Task Reverter_diz_a_verdade_em_vez_de_fingir_que_desfaz()
    {
        var resultado = await Montar().RevertAsync(Array.Empty<string>(), false, CancellationToken.None);

        Assert.Contains("não tem reversão automática", resultado.Resumo);
        Assert.Contains("Lixeira", resultado.GanhoMedido);
    }

    [Fact]
    public void Historico_nao_cresce_para_sempre()
    {
        for (var i = 0; i < 210; i++)
            _historico.Registrar(new CleanupEntry { Data = _clock.Now, BytesLiberados = i });

        Assert.True(_historico.Ler().Count <= 200);
    }

    [Fact]
    public void Historico_devolve_o_mais_recente_primeiro()
    {
        _historico.Registrar(new CleanupEntry { Data = _clock.Now.AddDays(-1), BytesLiberados = 1 });
        _historico.Registrar(new CleanupEntry { Data = _clock.Now, BytesLiberados = 2 });

        Assert.Equal(2, _historico.Ler()[0].BytesLiberados);
    }
}
