namespace GameBoost.Core.Modules.Bottleneck;

public enum FindingSeverity
{
    Informativo,
    Atencao,
    Critico
}

/// <summary>Para onde o botao do finding leva.</summary>
public enum FindingAcao
{
    Nenhuma,
    AbrirModoGame,
    LimparRam,
    PurgarStandby,
    AbrirAnalisadorDeDisco,
    AbrirInicializacao,
    AbrirTweaks,
    AtivarAltoDesempenho,
    AbrirConfiguracoesDeVideo,
    AbrirSegurancaDoWindows,
    AbrirSiteDoDriver,
    ReaplicarPerfil
}

/// <summary>
/// Um diagnostico em linguagem humana (secao 5.5). O texto e escrito para o
/// usuario, nao para o desenvolvedor: "seu Chrome esta usando 38% da CPU",
/// nao "cpu_usage_threshold_exceeded".
/// </summary>
public sealed record Finding
{
    /// <summary>Estavel por regra e alvo: e o que o debounce usa para nao repetir.</summary>
    public required string Id { get; init; }

    public required string RegraId { get; init; }
    public required string Titulo { get; init; }
    public required string Detalhe { get; init; }
    public FindingSeverity Severidade { get; init; } = FindingSeverity.Informativo;

    /// <summary>Ordena a lista. Quanto maior, mais alto aparece.</summary>
    public int Impacto { get; init; }

    public FindingAcao Acao { get; init; } = FindingAcao.Nenhuma;
    public string? TextoDaAcao { get; init; }

    /// <summary>Area do Relatorio de Saude que este finding penaliza.</summary>
    public HealthArea Area { get; init; } = HealthArea.Desempenho;

    /// <summary>Quanto tira da nota daquela area, de 0 a 100.</summary>
    public int Penalidade { get; init; }

    public DateTimeOffset Momento { get; init; }
}

/// <summary>Areas pontuadas no Relatorio de Saude (secao 5.12).</summary>
public enum HealthArea
{
    Desempenho,
    Espaco,
    Inicializacao,
    ConfiguracaoParaJogos
}
