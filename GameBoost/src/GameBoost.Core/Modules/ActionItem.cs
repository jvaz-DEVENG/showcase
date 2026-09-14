namespace GameBoost.Core.Modules;

/// <summary>
/// Unidade acionavel de qualquer modulo. A UI e generica: uma lista de ActionItem
/// com checkbox, badge de risco e botao "?" com a explicacao (secao 4.2 do spec).
/// </summary>
public sealed class ActionItem
{
    public required string Id { get; init; }
    public required string Categoria { get; init; }
    public required string Titulo { get; init; }
    public required string Descricao { get; init; }
    public RiskLevel Risco { get; init; } = RiskLevel.Baixo;

    /// <summary>Texto honesto do ganho. Proibido inventar numero (regra 4).</summary>
    public string GanhoEstimado { get; init; } = string.Empty;

    /// <summary>Bytes que este item deve liberar, quando mensuravel. 0 = nao se aplica.</summary>
    public long GanhoBytes { get; init; }

    /// <summary>Nada que possa causar perda vem marcado (regra 3).</summary>
    public bool PreMarcado { get; init; }

    public required string ComoDesfazer { get; init; }

    /// <summary>Itens protegidos aparecem na lista mas nao podem ser selecionados (regra 6).</summary>
    public bool Bloqueado { get; init; }

    public string? MotivoBloqueio { get; init; }

    /// <summary>Carga util especifica do modulo (ex.: o ProcessInfo por tras do item).</summary>
    public object? Payload { get; init; }
}
