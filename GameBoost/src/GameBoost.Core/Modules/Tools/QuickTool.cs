namespace GameBoost.Core.Modules.Tools;

/// <summary>Onde a ferramenta age.</summary>
public enum ToolKind
{
    /// <summary>Faz algo na máquina e devolve o resultado.</summary>
    Acao,

    /// <summary>Só abre uma ferramenta do Windows.</summary>
    Atalho,

    /// <summary>Só lê e devolve texto, sem alterar nada.</summary>
    Leitura,

    /// <summary>Processo longo, com saída acompanhada ao vivo.</summary>
    Demorada
}

/// <summary>
/// Uma ferramenta rápida da seção 5.13: atalhos para o que normalmente exige
/// prompt de comando ou caça no Painel de Controle.
/// </summary>
public sealed record QuickTool
{
    public required string Id { get; init; }
    public required string Categoria { get; init; }
    public required string Titulo { get; init; }
    public required string Descricao { get; init; }

    public ToolKind Tipo { get; init; } = ToolKind.Acao;
    public RiskLevel Risco { get; init; } = RiskLevel.Baixo;
    public bool PrecisaAdmin { get; init; }

    /// <summary>Texto do aviso antes de executar. Nulo roda direto.</summary>
    public string? Confirmacao { get; init; }

    public required string ComoDesfazer { get; init; }

    /// <summary>Quanto tempo costuma levar, para a UI não parecer travada.</summary>
    public string? Duracao { get; init; }
}

public sealed record ToolResult(bool Sucesso, string Mensagem, string? Detalhe = null)
{
    public static ToolResult Ok(string mensagem, string? detalhe = null) => new(true, mensagem, detalhe);

    public static ToolResult Falha(string mensagem, string? detalhe = null) => new(false, mensagem, detalhe);
}
