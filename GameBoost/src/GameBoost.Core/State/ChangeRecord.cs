using System.Text.Json.Serialization;

namespace GameBoost.Core.State;

public enum ChangeType
{
    Registry,
    Service,
    Power,
    File,
    Startup,
    Process,
    TimerResolution
}

/// <summary>
/// Regra 1: toda alteracao de sistema grava o valor anterior aqui antes de ser
/// aplicada. Sem ChangeRecord, a alteracao nao acontece.
/// </summary>
public sealed class ChangeRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Modulo { get; set; } = string.Empty;
    public ChangeType Tipo { get; set; }

    /// <summary>Identificador do alvo: caminho da chave, nome do servico, GUID do plano.</summary>
    public string Alvo { get; set; } = string.Empty;

    /// <summary>Detalhe dentro do alvo: nome do valor no registro, por exemplo.</summary>
    public string? SubAlvo { get; set; }

    public string? ValorAnterior { get; set; }
    public string? ValorNovo { get; set; }

    /// <summary>Quando o valor anterior nao existia, reverter significa apagar, nao regravar.</summary>
    public bool ValorAnteriorExistia { get; set; } = true;

    /// <summary>Metadados livres do modulo (ex.: RegistryRoot e tipo do valor).</summary>
    public Dictionary<string, string> Extras { get; set; } = new();

    public DateTimeOffset Data { get; set; }
    public bool Revertido { get; set; }
    public DateTimeOffset? DataReversao { get; set; }

    [JsonIgnore]
    public bool Pendente => !Revertido;
}
