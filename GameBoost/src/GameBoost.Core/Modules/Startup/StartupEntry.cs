namespace GameBoost.Core.Modules.Startup;

public enum StartupOrigem
{
    /// <summary>Chave Run do registro, do usuário ou da máquina.</summary>
    Registro,

    /// <summary>Atalho na pasta Inicializar.</summary>
    Pasta,

    /// <summary>Tarefa agendada com gatilho de logon.</summary>
    Tarefa
}

public enum ImpactoNoBoot
{
    Desconhecido,
    Baixo,
    Medio,
    Alto
}

/// <summary>Um programa que abre junto com o Windows (seção 5.6).</summary>
public sealed record StartupEntry
{
    public required string Id { get; init; }
    public required string Nome { get; init; }
    public required StartupOrigem Origem { get; init; }

    /// <summary>Onde mora: caminho da chave, do atalho ou da tarefa.</summary>
    public required string Local { get; init; }

    public string? Comando { get; init; }
    public string? Executavel { get; init; }
    public string? Publisher { get; init; }

    public bool Ativo { get; set; } = true;

    /// <summary>
    /// True assinado, false sem assinatura, <c>null</c> quando nao deu para
    /// verificar. O Teams mora em WindowsApps, onde nem o administrador le o
    /// arquivo: dizer "sem assinatura digital" ali seria acusacao falsa.
    /// </summary>
    public bool? Assinado { get; init; }
    public long TamanhoDoExecutavel { get; init; }

    public ImpactoNoBoot Impacto { get; init; } = ImpactoNoBoot.Desconhecido;

    public bool Protegido { get; set; }
    public string? MotivoDaProtecao { get; set; }

    /// <summary>Candidato óbvio a desativar, mas nunca pré-marcado (regra 3).</summary>
    public bool Sugerido { get; set; }

    public string TextoDoImpacto => Impacto switch
    {
        ImpactoNoBoot.Alto => "impacto alto no boot",
        ImpactoNoBoot.Medio => "impacto médio no boot",
        ImpactoNoBoot.Baixo => "impacto baixo no boot",
        _ => "impacto desconhecido"
    };

    public string TextoDaOrigem => Origem switch
    {
        StartupOrigem.Registro => "registro",
        StartupOrigem.Pasta => "pasta Inicializar",
        StartupOrigem.Tarefa => "tarefa agendada",
        _ => "desconhecido"
    };
}
