namespace GameBoost.Core.Modules.Bottleneck;

/// <summary>
/// Tudo que uma regra precisa saber. Passar um contexto em vez de servicos vivos
/// e o que torna cada regra testavel com um snapshot sintetico, sem tocar no
/// Windows (secao 11).
/// </summary>
public sealed class FindingContext
{
    public required MetricsSnapshot Atual { get; init; }

    /// <summary>Historico recente, do mais antigo para o mais novo.</summary>
    public IReadOnlyList<MetricsSnapshot> Historico { get; init; } = Array.Empty<MetricsSnapshot>();

    /// <summary>Fatos do sistema que nao mudam a cada segundo.</summary>
    public SystemFacts Fatos { get; init; } = new();

    /// <summary>Nome do jogo em execucao, quando ha um.</summary>
    public string? JogoEmExecucao { get; init; }

    public bool EmJogo => !string.IsNullOrEmpty(JogoEmExecucao);

    /// <summary>Media de uma metrica na janela pedida. Null se nao houver amostras.</summary>
    public double? MediaNaJanela(TimeSpan janela, Func<MetricsSnapshot, double?> seletor)
    {
        var corte = Atual.Momento - janela;
        var valores = Historico
            .Where(s => s.Momento >= corte)
            .Select(seletor)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        return valores.Count == 0 ? null : valores.Average();
    }

    /// <summary>True se a condicao valeu em toda a janela e houve amostras suficientes.</summary>
    public bool SustentadoPor(TimeSpan janela, Func<MetricsSnapshot, bool> condicao, int minimoDeAmostras = 5)
    {
        var corte = Atual.Momento - janela;
        var amostras = Historico.Where(s => s.Momento >= corte).ToList();

        if (amostras.Count < minimoDeAmostras)
            return false;

        return amostras.All(condicao);
    }
}

/// <summary>
/// Fatos lidos uma vez (ou raramente), nao a cada segundo: plano de energia,
/// build do Windows, driver de video, taxa de atualizacao do monitor.
/// </summary>
public sealed class SystemFacts
{
    public Guid? PlanoDeEnergiaAtivo { get; init; }
    public string? NomeDoPlanoDeEnergia { get; init; }
    public bool PlanoEhBalanceado { get; init; }

    public string? BuildDoWindows { get; init; }
    public string? BuildAnteriormenteVisto { get; init; }
    public int TweaksDesfeitosPorUpdate { get; init; }

    public string? FabricanteDaGpu { get; init; }
    public string? VersaoDoDriver { get; init; }
    public DateTimeOffset? DataDoDriver { get; init; }

    public int TaxaDeAtualizacaoAtual { get; init; }
    public int TaxaDeAtualizacaoMaxima { get; init; }

    public bool VbsAtivo { get; init; }
    public bool GameDvrAtivo { get; init; }
    public bool GameBarOverlayAtivo { get; init; }

    public int ItensNaInicializacao { get; init; }
    public bool JogoEmHdd { get; init; }
    public string? JogoEmHddNome { get; init; }
    public int PentesDeMemoria { get; init; }
    public bool DefenderVarrendo { get; init; }
    public bool WindowsSearchIndexando { get; init; }
}

/// <summary>
/// Uma regra de diagnostico. Devolve null quando nao ha nada a dizer.
/// Cada regra da tabela da secao 5.5 e uma classe destas, testada isoladamente.
/// </summary>
public interface IFindingRule
{
    string Id { get; }

    Finding? Avaliar(FindingContext contexto);
}
