using GameBoost.Core.Modules.Bottleneck;
using GameBoost.Core.Modules.HealthReport;
using Xunit;

namespace GameBoost.Tests;

public sealed class MetricsBufferTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 13, 20, 0, 0, TimeSpan.FromHours(-3));

    private static MetricsSnapshot Amostra(int segundos, double cpu = 0)
        => new() { Momento = T0.AddSeconds(segundos), CpuPercent = cpu };

    [Fact]
    public void Buffer_vazio_nao_tem_ultimo()
    {
        var buffer = new MetricsBuffer(10);

        Assert.Equal(0, buffer.Quantidade);
        Assert.Null(buffer.Ultimo);
        Assert.Empty(buffer.Todos());
    }

    [Fact]
    public void Buffer_preserva_a_ordem_cronologica()
    {
        var buffer = new MetricsBuffer(10);
        for (var i = 0; i < 5; i++)
            buffer.Adicionar(Amostra(i, cpu: i));

        var todos = buffer.Todos();

        Assert.Equal(5, todos.Count);
        Assert.Equal(new double[] { 0, 1, 2, 3, 4 }, todos.Select(s => s.CpuPercent));
        Assert.Equal(4, buffer.Ultimo!.CpuPercent);
    }

    [Fact]
    public void Buffer_cheio_descarta_o_mais_antigo_e_nao_cresce()
    {
        var buffer = new MetricsBuffer(3);
        for (var i = 0; i < 10; i++)
            buffer.Adicionar(Amostra(i, cpu: i));

        var todos = buffer.Todos();

        Assert.Equal(3, buffer.Quantidade);
        Assert.Equal(new double[] { 7, 8, 9 }, todos.Select(s => s.CpuPercent));
    }

    [Fact]
    public void Janela_de_5_minutos_a_1_hz_cabe_em_300_amostras()
    {
        var buffer = new MetricsBuffer();

        Assert.Equal(300, buffer.Capacidade);
    }

    [Fact]
    public void Ultimos_filtra_pela_janela_de_tempo()
    {
        var buffer = new MetricsBuffer(300);
        for (var i = 0; i < 120; i++)
            buffer.Adicionar(Amostra(i, cpu: i));

        var ultimos60 = buffer.Ultimos(TimeSpan.FromSeconds(60));

        Assert.Equal(61, ultimos60.Count);
        Assert.Equal(59, ultimos60[0].CpuPercent);
    }

    [Fact]
    public void Limpar_zera_o_buffer()
    {
        var buffer = new MetricsBuffer(5);
        buffer.Adicionar(Amostra(0));
        buffer.Limpar();

        Assert.Equal(0, buffer.Quantidade);
        Assert.Null(buffer.Ultimo);
    }

    [Fact]
    public void Capacidade_invalida_e_rejeitada()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MetricsBuffer(0));
    }
}

public sealed class FindingEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 13, 20, 0, 0, TimeSpan.FromHours(-3));

    private readonly FakeLogger _log = new();
    private readonly FakeClock _clock = new();

    /// <summary>Regra controlada: dispara so quando mandado.</summary>
    private sealed class RegraDeTeste : IFindingRule
    {
        public string Id => "teste";
        public bool Dispara { get; set; } = true;
        public int Impacto { get; set; } = 50;
        public FindingSeverity Severidade { get; set; } = FindingSeverity.Atencao;

        public Finding? Avaliar(FindingContext ctx) => Dispara
            ? new Finding
            {
                Id = Id,
                RegraId = Id,
                Titulo = "achado",
                Detalhe = "detalhe",
                Impacto = Impacto,
                Severidade = Severidade,
                Momento = ctx.Atual.Momento
            }
            : null;
    }

    private sealed class RegraQueQuebra : IFindingRule
    {
        public string Id => "quebrada";

        public Finding? Avaliar(FindingContext ctx) => throw new InvalidOperationException("erro proposital");
    }

    private static FindingContext Ctx(int segundos = 0) => new()
    {
        Atual = new MetricsSnapshot { Momento = T0.AddSeconds(segundos) }
    };

    [Fact]
    public void Regras_padrao_cobrem_a_tabela_da_secao_5_5()
    {
        Assert.Equal(17, FindingEngine.RegrasPadrao().Count);
    }

    [Fact]
    public void Finding_repetido_nao_duplica_na_lista()
    {
        var regra = new RegraDeTeste();
        var motor = new FindingEngine(_log, _clock, new IFindingRule[] { regra });

        motor.Avaliar(Ctx(0));
        motor.Avaliar(Ctx(1));
        var terceira = motor.Avaliar(Ctx(2));

        Assert.Single(terceira);
    }

    [Fact]
    public void Finding_que_parou_de_valer_some_so_depois_do_debounce()
    {
        var regra = new RegraDeTeste();
        var motor = new FindingEngine(_log, _clock, new IFindingRule[] { regra });

        motor.Avaliar(Ctx(0));
        regra.Dispara = false;

        // Dentro da janela de 30 s continua na tela, para nao piscar.
        Assert.Single(motor.Avaliar(Ctx(10)));
        Assert.Single(motor.Avaliar(Ctx(29)));

        // Passada a janela, some.
        Assert.Empty(motor.Avaliar(Ctx(40)));
    }

    [Fact]
    public void Regra_que_lanca_excecao_nao_derruba_as_outras()
    {
        var motor = new FindingEngine(_log, _clock, new IFindingRule[]
        {
            new RegraQueQuebra(),
            new RegraDeTeste()
        });

        var findings = motor.Avaliar(Ctx());

        Assert.Single(findings);
        Assert.Contains(_log.Linhas, l => l.Modulo == "Bottleneck" && l.Alvo == "quebrada");
    }

    [Fact]
    public void Findings_saem_ordenados_por_severidade_e_impacto()
    {
        var baixo = new RegraDeTeste { Impacto = 10, Severidade = FindingSeverity.Informativo };
        var motor = new FindingEngine(_log, _clock, new IFindingRule[] { baixo });

        var findings = motor.Avaliar(Ctx());

        Assert.Single(findings);
        Assert.Equal(FindingSeverity.Informativo, findings[0].Severidade);
    }

    [Fact]
    public void Limpar_esvazia_os_findings_ativos()
    {
        var motor = new FindingEngine(_log, _clock, new IFindingRule[] { new RegraDeTeste() });
        motor.Avaliar(Ctx());
        motor.Limpar();

        Assert.Empty(motor.Atuais());
    }
}

public sealed class FindingContextTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 13, 20, 0, 0, TimeSpan.FromHours(-3));

    private static FindingContext Montar(params double[] cpus)
    {
        var historico = cpus
            .Select((c, i) => new MetricsSnapshot { Momento = T0.AddSeconds(i - cpus.Length + 1), CpuPercent = c })
            .ToList();

        return new FindingContext { Atual = historico[^1], Historico = historico };
    }

    [Fact]
    public void Sustentado_exige_a_condicao_em_toda_a_janela()
    {
        var ctx = Montar(95, 96, 97, 98, 99, 97);

        Assert.True(ctx.SustentadoPor(TimeSpan.FromSeconds(10), s => s.CpuPercent > 90));
    }

    [Fact]
    public void Sustentado_falha_se_qualquer_amostra_quebrar_a_condicao()
    {
        var ctx = Montar(95, 96, 10, 98, 99, 97);

        Assert.False(ctx.SustentadoPor(TimeSpan.FromSeconds(10), s => s.CpuPercent > 90));
    }

    [Fact]
    public void Sustentado_exige_um_minimo_de_amostras()
    {
        // Duas amostras nao provam nada sobre 30 segundos.
        var ctx = Montar(95, 97);

        Assert.False(ctx.SustentadoPor(TimeSpan.FromSeconds(30), s => s.CpuPercent > 90));
    }

    [Fact]
    public void Media_na_janela_ignora_valores_ausentes()
    {
        var ctx = Montar(10, 20, 30);

        Assert.Equal(20, ctx.MediaNaJanela(TimeSpan.FromSeconds(10), s => s.CpuPercent));
    }

    [Fact]
    public void Media_sem_amostras_e_nula()
    {
        var ctx = Montar(10);

        Assert.Null(ctx.MediaNaJanela(TimeSpan.FromSeconds(10), _ => (double?)null));
    }
}

public sealed class HealthScoreTests
{
    private static Finding F(HealthArea area, int penalidade, FindingSeverity sev = FindingSeverity.Atencao, int impacto = 50)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            RegraId = "r",
            Titulo = "t",
            Detalhe = "d",
            Area = area,
            Penalidade = penalidade,
            Severidade = sev,
            Impacto = impacto
        };

    [Fact]
    public void Sem_findings_todas_as_areas_valem_100()
    {
        var score = HealthScore.Calcular(Array.Empty<Finding>());

        Assert.Equal(100, score.NotaGeral);
        Assert.Equal("Otimo", score.Conceito);
        Assert.All(score.Areas, a => Assert.Equal(100, a.Nota));
    }

    [Fact]
    public void Penalidades_da_mesma_area_se_somam()
    {
        var score = HealthScore.Calcular(new[]
        {
            F(HealthArea.Desempenho, 25),
            F(HealthArea.Desempenho, 10)
        });

        var desempenho = score.Areas.Single(a => a.Area == HealthArea.Desempenho);

        Assert.Equal(65, desempenho.Nota);
        Assert.Equal(100, score.Areas.Single(a => a.Area == HealthArea.Espaco).Nota);
    }

    [Fact]
    public void Nota_nunca_passa_dos_limites()
    {
        var score = HealthScore.Calcular(new[] { F(HealthArea.Espaco, 500) });

        Assert.Equal(0, score.Areas.Single(a => a.Area == HealthArea.Espaco).Nota);
    }

    [Fact]
    public void Finding_informativo_sem_penalidade_nao_baixa_a_nota()
    {
        // VBS ativo e GPU no limite sao fatos, nao defeitos.
        var score = HealthScore.Calcular(new[] { F(HealthArea.ConfiguracaoParaJogos, 0, FindingSeverity.Informativo) });

        Assert.Equal(100, score.NotaGeral);
    }

    [Fact]
    public void Principais_traz_no_maximo_cinco_ordenados_por_severidade()
    {
        var findings = new[]
        {
            F(HealthArea.Desempenho, 5, FindingSeverity.Informativo, 10),
            F(HealthArea.Desempenho, 5, FindingSeverity.Critico, 90),
            F(HealthArea.Espaco, 5, FindingSeverity.Atencao, 50),
            F(HealthArea.Espaco, 5, FindingSeverity.Informativo, 20),
            F(HealthArea.Inicializacao, 5, FindingSeverity.Informativo, 30),
            F(HealthArea.Inicializacao, 5, FindingSeverity.Informativo, 40)
        };

        var score = HealthScore.Calcular(findings);

        Assert.Equal(5, score.Principais.Count);
        Assert.Equal(FindingSeverity.Critico, score.Principais[0].Severidade);
    }

    [Theory]
    [InlineData(0, "Critico")]
    [InlineData(30, "Precisa de atencao")]
    [InlineData(60, "Da para melhorar")]
    [InlineData(80, "Bom")]
    [InlineData(95, "Otimo")]
    public void Conceito_acompanha_a_nota(int penalidadePorArea, string esperado)
    {
        var desconto = 100 - penalidadePorArea;
        var findings = Enum.GetValues<HealthArea>().Select(a => F(a, desconto)).ToArray();

        var score = HealthScore.Calcular(findings);

        Assert.Equal(esperado, score.Conceito);
    }
}
