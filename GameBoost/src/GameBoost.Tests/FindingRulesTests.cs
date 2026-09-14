using GameBoost.Core.Modules.Bottleneck;
using GameBoost.Core.Modules.Bottleneck.Rules;
using Xunit;

namespace GameBoost.Tests;

/// <summary>
/// Cada regra da tabela da secao 5.5 testada com snapshots sinteticos: dada
/// uma metrica, sai o Finding esperado. Nada aqui toca no Windows.
/// </summary>
public sealed class FindingRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 13, 20, 0, 0, TimeSpan.FromHours(-3));

    private static MetricsSnapshot Snapshot(
        double cpu = 10,
        long ramTotal = 16L * 1024 * 1024 * 1024,
        long ramLivre = 8L * 1024 * 1024 * 1024,
        long standby = 0,
        Medida? gpu = null,
        double discoUsado = 50,
        Medida? temperatura = null,
        Medida? frequencia = null,
        IReadOnlyList<ProcessUsage>? processos = null,
        int segundos = 0)
        => new()
        {
            Momento = T0.AddSeconds(segundos),
            CpuPercent = cpu,
            NucleosLogicos = 8,
            RamTotalBytes = ramTotal,
            RamDisponivelBytes = ramLivre,
            StandbyBytes = standby,
            GpuPercent = gpu ?? Medida.Indisponivel,
            DiscoSistemaUsadoPercent = discoUsado,
            TemperaturaCpu = temperatura ?? Medida.Indisponivel,
            CpuFrequenciaPercent = frequencia ?? Medida.Indisponivel,
            Processos = processos ?? Array.Empty<ProcessUsage>()
        };

    private static ProcessUsage Proc(string nome, double cpu, long ramMb = 500, int instancias = 1)
        => new(0, nome, cpu, ramMb * 1024 * 1024, 0, instancias);

    /// <summary>Monta um historico com o mesmo snapshot repetido, para as regras de duracao.</summary>
    private static FindingContext Contexto(
        MetricsSnapshot atual,
        int segundosDeHistorico = 40,
        SystemFacts? fatos = null,
        string? jogo = null,
        Func<int, MetricsSnapshot>? gerador = null)
    {
        var historico = new List<MetricsSnapshot>();
        for (var s = -segundosDeHistorico; s <= 0; s++)
        {
            historico.Add(gerador is not null
                ? gerador(s)
                : atual with { Momento = atual.Momento.AddSeconds(s) });
        }

        return new FindingContext
        {
            Atual = atual,
            Historico = historico,
            Fatos = fatos ?? new SystemFacts(),
            JogoEmExecucao = jogo
        };
    }

    // ---------------- Processo devorando CPU ----------------

    [Fact]
    public void Processo_acima_do_limite_por_30s_vira_finding()
    {
        var atual = Snapshot(cpu: 60, processos: new[] { Proc("chrome", 38, instancias: 62) });

        var finding = new ProcessoDevorandoCpuRule().Avaliar(Contexto(atual));

        Assert.NotNull(finding);
        Assert.Contains("chrome", finding!.Titulo, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("38%", finding.Titulo);
        Assert.Equal(FindingAcao.AbrirModoGame, finding.Acao);
    }

    [Fact]
    public void Pico_curto_de_cpu_nao_vira_finding()
    {
        // So o instante atual esta acima do limite; o historico esta calmo.
        var atual = Snapshot(cpu: 60, processos: new[] { Proc("chrome", 38) });

        var contexto = Contexto(atual, gerador: s => s == 0
            ? atual
            : Snapshot(cpu: 5, processos: new[] { Proc("chrome", 2) }, segundos: s));

        Assert.Null(new ProcessoDevorandoCpuRule().Avaliar(contexto));
    }

    [Fact]
    public void O_proprio_jogo_nunca_e_acusado_de_devorar_cpu()
    {
        var atual = Snapshot(cpu: 95, processos: new[] { Proc("cs2", 80) });

        Assert.Null(new ProcessoDevorandoCpuRule().Avaliar(Contexto(atual, jogo: "cs2")));
    }

    [Fact]
    public void Processo_abaixo_do_limite_nao_vira_finding()
    {
        var atual = Snapshot(processos: new[] { Proc("discord", ProcessoDevorandoCpuRule.Limite - 1) });

        Assert.Null(new ProcessoDevorandoCpuRule().Avaliar(Contexto(atual)));
    }

    // ---------------- RAM ----------------

    [Fact]
    public void Ram_abaixo_de_10_por_cento_vira_finding_critico()
    {
        var atual = Snapshot(
            ramTotal: 16L * 1024 * 1024 * 1024,
            ramLivre: 1_200L * 1024 * 1024,
            processos: new[] { Proc("chrome", 5, 2500), Proc("Discord", 2, 1600) });

        var finding = new RamNoLimiteRule().Avaliar(Contexto(atual));

        Assert.NotNull(finding);
        Assert.Equal(FindingSeverity.Critico, finding!.Severidade);
        Assert.Contains("chrome", finding.Detalhe, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FindingAcao.LimparRam, finding.Acao);
    }

    [Fact]
    public void Ram_folgada_nao_vira_finding()
    {
        Assert.Null(new RamNoLimiteRule().Avaliar(Contexto(Snapshot())));
    }

    [Fact]
    public void Standby_acima_de_40_por_cento_vira_finding()
    {
        var total = 16L * 1024 * 1024 * 1024;
        var atual = Snapshot(ramTotal: total, standby: (long)(total * 0.45));

        var finding = new StandbyListInchadaRule().Avaliar(Contexto(atual));

        Assert.NotNull(finding);
        Assert.Equal(FindingAcao.PurgarStandby, finding!.Acao);
        // Honestidade: liberar Standby nao aumenta FPS por si so.
        Assert.Contains("nao aumenta FPS", finding.Detalhe);
    }

    // ---------------- Disco ----------------

    [Theory]
    [InlineData(94.0, FindingSeverity.Atencao)]
    [InlineData(96.0, FindingSeverity.Critico)]
    public void Disco_cheio_vira_finding_com_severidade_crescente(double usado, FindingSeverity esperada)
    {
        var finding = new DiscoDoSistemaCheioRule().Avaliar(Contexto(Snapshot(discoUsado: usado)));

        Assert.NotNull(finding);
        Assert.Equal(esperada, finding!.Severidade);
        Assert.Equal(HealthArea.Espaco, finding.Area);
    }

    [Fact]
    public void Disco_com_espaco_nao_vira_finding()
    {
        Assert.Null(new DiscoDoSistemaCheioRule().Avaliar(Contexto(Snapshot(discoUsado: 70))));
    }

    // ---------------- GPU e gargalo ----------------

    [Fact]
    public void Gpu_no_limite_vira_finding_informativo_sem_penalidade()
    {
        var atual = Snapshot(gpu: Medida.De(98));

        var finding = new GpuNoLimiteRule().Avaliar(Contexto(atual));

        Assert.NotNull(finding);
        Assert.Equal(FindingSeverity.Informativo, finding!.Severidade);
        // GPU no maximo e um fato, nao um defeito: nao baixa a nota.
        Assert.Equal(0, finding.Penalidade);
    }

    [Fact]
    public void Gpu_indisponivel_nunca_gera_finding()
    {
        var atual = Snapshot(gpu: Medida.Indisponivel);

        Assert.Null(new GpuNoLimiteRule().Avaliar(Contexto(atual)));
    }

    [Fact]
    public void Cpu_no_talo_com_gpu_ociosa_em_jogo_e_gargalo_de_cpu()
    {
        var atual = Snapshot(cpu: 97, gpu: Medida.De(45));

        var finding = new GargaloDeCpuEmJogoRule().Avaliar(Contexto(atual, jogo: "cs2"));

        Assert.NotNull(finding);
        Assert.Contains("Gargalo de CPU", finding!.Titulo);
    }

    [Fact]
    public void Gargalo_de_cpu_so_vale_com_jogo_aberto()
    {
        var atual = Snapshot(cpu: 97, gpu: Medida.De(45));

        Assert.Null(new GargaloDeCpuEmJogoRule().Avaliar(Contexto(atual)));
    }

    // ---------------- Temperatura ----------------

    [Fact]
    public void Cpu_quente_sob_carga_vira_finding_critico()
    {
        var atual = Snapshot(cpu: 85, temperatura: Medida.De(97), frequencia: Medida.De(65));

        var finding = new ThrottlingTermicoRule().Avaliar(Contexto(atual));

        Assert.NotNull(finding);
        Assert.Equal(FindingSeverity.Critico, finding!.Severidade);
        Assert.Contains("reduzindo a velocidade", finding.Detalhe);
    }

    [Fact]
    public void Temperatura_indisponivel_nao_inventa_finding()
    {
        var atual = Snapshot(cpu: 99, temperatura: Medida.Indisponivel);

        Assert.Null(new ThrottlingTermicoRule().Avaliar(Contexto(atual)));
    }

    [Fact]
    public void Cpu_quente_mas_ociosa_nao_vira_finding()
    {
        var atual = Snapshot(cpu: 20, temperatura: Medida.De(95));

        Assert.Null(new ThrottlingTermicoRule().Avaliar(Contexto(atual)));
    }

    // ---------------- Processos do sistema ----------------

    [Fact]
    public void Defender_varrendo_e_informativo_e_nunca_sugere_desativar()
    {
        var atual = Snapshot(processos: new[] { Proc("MsMpEng", 30) });

        var finding = new ProcessoDeSistemaOcupandoDiscoRule().Avaliar(Contexto(atual));

        Assert.NotNull(finding);
        Assert.Equal(FindingSeverity.Informativo, finding!.Severidade);
        Assert.Equal(FindingAcao.Nenhuma, finding.Acao);
        Assert.Contains("nao desativa", finding.Detalhe);
    }

    // ---------------- Configuracao ----------------

    [Fact]
    public void Plano_balanceado_em_jogo_pesa_mais_que_fora_do_jogo()
    {
        var fatos = new SystemFacts { PlanoEhBalanceado = true };
        var regra = new PlanoDeEnergiaBalanceadoRule();

        var emJogo = regra.Avaliar(Contexto(Snapshot(), fatos: fatos, jogo: "cs2"));
        var foraDeJogo = regra.Avaliar(Contexto(Snapshot(), fatos: fatos));

        Assert.NotNull(emJogo);
        Assert.NotNull(foraDeJogo);
        Assert.True(emJogo!.Impacto > foraDeJogo!.Impacto);
        Assert.Equal(FindingAcao.AtivarAltoDesempenho, emJogo.Acao);
    }

    [Fact]
    public void Plano_de_alto_desempenho_nao_gera_finding()
    {
        Assert.Null(new PlanoDeEnergiaBalanceadoRule()
            .Avaliar(Contexto(Snapshot(), fatos: new SystemFacts { PlanoEhBalanceado = false })));
    }

    [Fact]
    public void Driver_com_mais_de_seis_meses_vira_finding_que_nunca_instala_nada()
    {
        var fatos = new SystemFacts
        {
            FabricanteDaGpu = "NVIDIA",
            DataDoDriver = T0.AddMonths(-8)
        };

        var finding = new DriverDeVideoAntigoRule().Avaliar(Contexto(Snapshot(), fatos: fatos));

        Assert.NotNull(finding);
        Assert.Equal(FindingAcao.AbrirSiteDoDriver, finding!.Acao);
        Assert.Contains("nunca instala driver", finding.Detalhe);
    }

    [Fact]
    public void Driver_recente_nao_vira_finding()
    {
        var fatos = new SystemFacts { DataDoDriver = T0.AddMonths(-2) };

        Assert.Null(new DriverDeVideoAntigoRule().Avaliar(Contexto(Snapshot(), fatos: fatos)));
    }

    [Fact]
    public void Driver_de_data_desconhecida_nao_inventa_finding()
    {
        Assert.Null(new DriverDeVideoAntigoRule().Avaliar(Contexto(Snapshot(), fatos: new SystemFacts())));
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(23, true)]
    public void Inicializacao_lotada_depende_do_limite(int quantidade, bool esperaFinding)
    {
        var fatos = new SystemFacts { ItensNaInicializacao = quantidade };

        var finding = new InicializacaoLotadaRule().Avaliar(Contexto(Snapshot(), fatos: fatos));

        Assert.Equal(esperaFinding, finding is not null);
        if (finding is not null)
        {
            Assert.Equal(HealthArea.Inicializacao, finding.Area);
            Assert.Contains(quantidade.ToString(), finding.Titulo);
        }
    }

    [Fact]
    public void Game_dvr_ligado_vira_finding()
    {
        var fatos = new SystemFacts { GameDvrAtivo = true };

        var finding = new GravacaoEmSegundoPlanoRule().Avaliar(Contexto(Snapshot(), fatos: fatos));

        Assert.NotNull(finding);
        Assert.Equal(FindingAcao.AbrirModoGame, finding!.Acao);
    }

    [Fact]
    public void Update_que_desfez_ajustes_vira_finding()
    {
        var fatos = new SystemFacts
        {
            BuildDoWindows = "26H2",
            BuildAnteriormenteVisto = "24H2",
            TweaksDesfeitosPorUpdate = 4
        };

        var finding = new PerfilDesfeitoPorUpdateRule().Avaliar(Contexto(Snapshot(), fatos: fatos));

        Assert.NotNull(finding);
        Assert.Contains("26H2", finding!.Titulo);
        Assert.Contains("4", finding.Titulo);
        Assert.Equal(FindingAcao.ReaplicarPerfil, finding.Acao);
    }

    [Fact]
    public void Mesma_build_do_windows_nao_gera_finding_de_update()
    {
        var fatos = new SystemFacts
        {
            BuildDoWindows = "24H2",
            BuildAnteriormenteVisto = "24H2",
            TweaksDesfeitosPorUpdate = 4
        };

        Assert.Null(new PerfilDesfeitoPorUpdateRule().Avaliar(Contexto(Snapshot(), fatos: fatos)));
    }

    [Fact]
    public void Build_nova_sem_tweak_desfeito_nao_gera_finding()
    {
        var fatos = new SystemFacts
        {
            BuildDoWindows = "26H2",
            BuildAnteriormenteVisto = "24H2",
            TweaksDesfeitosPorUpdate = 0
        };

        Assert.Null(new PerfilDesfeitoPorUpdateRule().Avaliar(Contexto(Snapshot(), fatos: fatos)));
    }

    [Fact]
    public void Monitor_em_60hz_sendo_capaz_de_165_vira_finding_de_alto_impacto()
    {
        var fatos = new SystemFacts { TaxaDeAtualizacaoAtual = 60, TaxaDeAtualizacaoMaxima = 165 };

        var finding = new MonitorEmTaxaBaixaRule().Avaliar(Contexto(Snapshot(), fatos: fatos));

        Assert.NotNull(finding);
        Assert.Contains("165", finding!.Titulo);
        Assert.Contains("60", finding.Titulo);
        Assert.True(finding.Impacto > 80);
    }

    [Fact]
    public void Monitor_ja_na_taxa_maxima_nao_gera_finding()
    {
        var fatos = new SystemFacts { TaxaDeAtualizacaoAtual = 165, TaxaDeAtualizacaoMaxima = 165 };

        Assert.Null(new MonitorEmTaxaBaixaRule().Avaliar(Contexto(Snapshot(), fatos: fatos)));
    }

    [Fact]
    public void Vbs_ativo_e_informativo_nunca_desativa_e_nao_baixa_a_nota()
    {
        var finding = new VbsAtivoRule().Avaliar(Contexto(Snapshot(), fatos: new SystemFacts { VbsAtivo = true }));

        Assert.NotNull(finding);
        Assert.Equal(FindingSeverity.Informativo, finding!.Severidade);
        Assert.Equal(0, finding.Penalidade);
        Assert.Equal(FindingAcao.AbrirSegurancaDoWindows, finding.Acao);
        Assert.Contains("nao altera isto", finding.Detalhe);
    }

    [Fact]
    public void Jogo_em_hdd_vira_finding()
    {
        var fatos = new SystemFacts { JogoEmHdd = true, JogoEmHddNome = "Cyberpunk 2077" };

        var finding = new JogoEmHddRule().Avaliar(Contexto(Snapshot(), fatos: fatos));

        Assert.NotNull(finding);
        Assert.Contains("Cyberpunk 2077", finding!.Titulo);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(0, false)]
    public void Single_channel_so_vira_finding_com_exatamente_um_pente(int pentes, bool espera)
    {
        var fatos = new SystemFacts { PentesDeMemoria = pentes };

        var finding = new MemoriaSingleChannelRule().Avaliar(Contexto(Snapshot(), fatos: fatos));

        Assert.Equal(espera, finding is not null);
    }

    // ---------------- Nenhuma regra dispara numa maquina saudavel ----------------

    [Fact]
    public void Maquina_saudavel_nao_gera_nenhum_finding()
    {
        var saudavel = Snapshot(
            cpu: 8,
            ramLivre: 10L * 1024 * 1024 * 1024,
            gpu: Medida.De(5),
            discoUsado: 55,
            processos: new[] { Proc("explorer", 1), Proc("chrome", 3) });

        var fatos = new SystemFacts
        {
            PlanoEhBalanceado = false,
            DataDoDriver = T0.AddMonths(-1),
            ItensNaInicializacao = 6,
            TaxaDeAtualizacaoAtual = 144,
            TaxaDeAtualizacaoMaxima = 144,
            PentesDeMemoria = 2,
            BuildDoWindows = "26H2",
            BuildAnteriormenteVisto = "26H2"
        };

        var contexto = Contexto(saudavel, fatos: fatos);

        foreach (var regra in FindingEngine.RegrasPadrao())
            Assert.Null(regra.Avaliar(contexto));
    }
}
