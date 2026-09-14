namespace GameBoost.Core.Modules;

/// <summary>Etapa de progresso de uma varredura longa (regra 10: nunca travar a UI).</summary>
public sealed record ModuleProgress(string Etapa, int Percentual);

public sealed class ScanResult
{
    public required string ModuloId { get; init; }
    public IReadOnlyList<ActionItem> Itens { get; init; } = Array.Empty<ActionItem>();
    public string Resumo { get; init; } = string.Empty;
    public DateTimeOffset Momento { get; init; }
    public IReadOnlyList<string> Avisos { get; init; } = Array.Empty<string>();
}

public sealed record AppliedAction(string ItemId, bool Sucesso, string Detalhe, string? ChangeRecordId);

public sealed class ApplyResult
{
    public required string ModuloId { get; init; }
    public IReadOnlyList<AppliedAction> Acoes { get; init; } = Array.Empty<AppliedAction>();
    public string Resumo { get; init; } = string.Empty;

    /// <summary>Ganho realmente medido depois da acao. Nunca estimado (regra 4).</summary>
    public string GanhoMedido { get; init; } = string.Empty;

    public bool DryRun { get; init; }
    public int Sucessos => Acoes.Count(a => a.Sucesso);
    public int Falhas => Acoes.Count(a => !a.Sucesso);
}

/// <summary>Contrato unico de todo modulo do GameBoost (secao 4.2 do spec).</summary>
public interface IModule
{
    string Id { get; }
    string Nome { get; }
    string Descricao { get; }

    Task<ScanResult> ScanAsync(IProgress<ModuleProgress>? progress, CancellationToken ct);

    Task<ApplyResult> ApplyAsync(IReadOnlyList<string> itemIds, bool dryRun, CancellationToken ct);

    Task<ApplyResult> RevertAsync(IReadOnlyList<string> changeIds, bool dryRun, CancellationToken ct);
}
