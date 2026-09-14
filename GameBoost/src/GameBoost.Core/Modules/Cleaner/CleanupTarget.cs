namespace GameBoost.Core.Modules.Cleaner;

/// <summary>O que fazer com os arquivos de um alvo.</summary>
public enum RemocaoModo
{
    /// <summary>Temporário de sistema: pode ser apagado direto (regra 2).</summary>
    ApagarDireto,

    /// <summary>Conteúdo do usuário: vai para a Lixeira, sempre (regra 2).</summary>
    Lixeira
}

/// <summary>
/// Um alvo de limpeza. A whitelist da seção 5.2 é isto e nada além: o Cleaner
/// nunca deriva caminho por conta própria, e cada alvo passa pelo SafetyGuard
/// antes de qualquer leitura.
/// </summary>
public sealed record CleanupTarget
{
    public required string Id { get; init; }
    public required string Categoria { get; init; }
    public required string Titulo { get; init; }
    public required string Descricao { get; init; }

    /// <summary>Pasta raiz. Resolvida na hora: variáveis de ambiente mudam por usuário.</summary>
    public required Func<string?> Raiz { get; init; }

    public string Padrao { get; init; } = "*";
    public bool Recursivo { get; init; } = true;

    /// <summary>Só remove arquivos mais antigos que isto. Zero desliga a checagem.</summary>
    public TimeSpan IdadeMinima { get; init; } = TimeSpan.Zero;

    public RiskLevel Risco { get; init; } = RiskLevel.Baixo;
    public RemocaoModo Modo { get; init; } = RemocaoModo.ApagarDireto;

    /// <summary>Regra 3: nada que possa causar perda vem marcado.</summary>
    public bool PreMarcadoPadrao { get; init; }

    /// <summary>Processos que precisam estar fechados. Senão o item aparece bloqueado.</summary>
    public IReadOnlyList<string> ExigeFechado { get; init; } = Array.Empty<string>();

    /// <summary>Serviço parado durante a limpeza e religado depois.</summary>
    public string? PararServico { get; init; }

    public required string ComoDesfazer { get; init; }

    /// <summary>Explicação honesta do efeito colateral, quando existe.</summary>
    public string? Advertencia { get; init; }

    /// <summary>
    /// Só aparece em "Sugestões", nunca como item acionável. Usado para o que é
    /// arriscado demais para o app decidir, como instaladores em Downloads.
    /// </summary>
    public bool ApenasSugestao { get; init; }
}
