using GameBoost.Core.Abstractions;

namespace GameBoost.Core.Modules.Profiles;

/// <summary>Um tweak por executável, aplicado só enquanto o jogo roda.</summary>
public sealed class TweakDoPerfil
{
    public string Id { get; set; } = string.Empty;
    public bool Ligar { get; set; } = true;
}

/// <summary>
/// Perfil de um jogo (seção 5.11).
///
/// Um perfil diz o que fazer quando **este** jogo abre e o que desfazer quando
/// ele fecha. A reversão não é opcional nem configurável: sair do jogo devolve
/// a máquina ao estado anterior, sempre. Um perfil que só sabe ligar coisas
/// seria a mesma armadilha dos boosters que deixam o Windows Update desligado
/// para sempre.
/// </summary>
public sealed class GameProfile
{
    /// <summary>Nome do executável sem extensão, em minúsculas. É a chave.</summary>
    public string Executavel { get; set; } = string.Empty;

    public string Nome { get; set; } = string.Empty;
    public string? Caminho { get; set; }
    public string? Launcher { get; set; }

    /// <summary>
    /// Ativar o Modo Game sozinho ao detectar o jogo. **Desligado por padrão**:
    /// a primeira vez sempre pergunta (regra 3).
    /// </summary>
    public bool Automatico { get; set; }

    public bool AtivarModoGame { get; set; } = true;

    /// <summary>GUID do plano de energia. Null usa o do Modo Game.</summary>
    public string? PlanoDeEnergia { get; set; }

    /// <summary>Prioridade do processo do jogo.</summary>
    public ProcessPriority? Prioridade { get; set; } = ProcessPriority.AboveNormal;

    /// <summary>
    /// Núcleos reservados para o jogo, por índice. Lista vazia = não mexer.
    ///
    /// **Desligado por padrão, e com motivo.** Limitar afinidade quase sempre
    /// piora: o agendador do Windows sabe mais sobre cache e SMT que qualquer
    /// lista fixa. Isto existe para o caso raro de jogo antigo que se perde em
    /// CPU de muitos núcleos.
    /// </summary>
    public List<int> Afinidade { get; set; } = new();

    /// <summary>Resolução de timer em 0,5 ms enquanto o jogo roda.</summary>
    public bool TimerDeMeioMilissegundo { get; set; }

    /// <summary>Tweaks do catálogo aplicados só durante a sessão.</summary>
    public List<TweakDoPerfil> Tweaks { get; set; } = new();

    /// <summary>Processos a encerrar ao entrar. Nunca inclui protegido.</summary>
    public List<string> FecharApps { get; set; } = new();

    /// <summary>Apps a reabrir ao sair, no formato do Modo Game.</summary>
    public List<string> ReabrirApps { get; set; } = new();

    public DateTimeOffset? UltimaVez { get; set; }
    public int VezesUsado { get; set; }

    /// <summary>Resumo de uma linha para a tela.</summary>
    public string Resumo
    {
        get
        {
            var partes = new List<string>();

            if (AtivarModoGame)
                partes.Add("Modo Game");

            if (Prioridade is not null and not ProcessPriority.Normal)
                partes.Add($"prioridade {TextoDaPrioridade(Prioridade.Value)}");

            if (Afinidade.Count > 0)
                partes.Add($"{Afinidade.Count} núcleos");

            if (TimerDeMeioMilissegundo)
                partes.Add("timer 0,5 ms");

            if (FecharApps.Count > 0)
                partes.Add($"fecha {FecharApps.Count} apps");

            if (Tweaks.Count > 0)
                partes.Add($"{Tweaks.Count} tweaks");

            partes.Add(Automatico ? "ativa sozinho" : "pergunta antes");

            return string.Join(" · ", partes);
        }
    }

    public static string TextoDaPrioridade(ProcessPriority p) => p switch
    {
        ProcessPriority.High => "alta",
        ProcessPriority.AboveNormal => "acima do normal",
        ProcessPriority.Normal => "normal",
        ProcessPriority.BelowNormal => "abaixo do normal",
        ProcessPriority.Idle => "ociosa",
        _ => "desconhecida"
    };

    /// <summary>
    /// Perfil padrão para um jogo recém-detectado. Conservador de propósito: o
    /// que ele faz é exatamente o que o Modo Game já faria, sem nada extra.
    /// </summary>
    public static GameProfile Padrao(string executavel, string nome, string? caminho, string? launcher)
        => new()
        {
            Executavel = Normalizar(executavel),
            Nome = nome,
            Caminho = caminho,
            Launcher = launcher,
            Automatico = false,
            AtivarModoGame = true,
            Prioridade = ProcessPriority.AboveNormal
        };

    public static string Normalizar(string executavel)
    {
        var nome = executavel.Trim();

        if (nome.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            nome = nome[..^4];

        return nome.ToLowerInvariant();
    }
}
