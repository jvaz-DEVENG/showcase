using GameBoost.Core.Abstractions;
using GameBoost.Core.Modules;

namespace GameBoost.Core.Modules.Tweaks;

/// <summary>
/// Um serviço do catálogo da seção 5.7, com a ação sugerida e o porquê.
/// </summary>
public sealed record ServiceTweak
{
    public required string Nome { get; init; }
    public required string Titulo { get; init; }
    public required string Explicacao { get; init; }
    public required RiskLevel Risco { get; init; }

    /// <summary>Para onde o GameBoost sugere mover o tipo de inicialização.</summary>
    public ServiceStartMode Sugerido { get; init; } = ServiceStartMode.Manual;

    /// <summary>
    /// Aparece na lista mas não pode ser alterado (regra 6). O motivo é o texto
    /// de <see cref="Explicacao"/>.
    /// </summary>
    public bool Protegido { get; init; }

    /// <summary>
    /// Serviço que o GameBoost não mexe e também não sugere mexer, mas que vale
    /// explicar porque toda lista de "otimização" da internet manda desligar.
    /// </summary>
    public bool SomenteExplicar { get; init; }
}

/// <summary>
/// Serviços do Windows (seção 5.7).
///
/// A régua aqui é diferente da dos tweaks: um serviço desligado por engano não
/// tira alguns FPS, tira a impressora, a busca ou o Game Pass. Por isso a lista
/// é curta, cada linha diz o que o usuário perde, e **nada vem pré-marcado**.
///
/// Três entradas existem só para desmentir listas de otimização da internet:
/// SysMain, os serviços Xbox e o Windows Update. Elas aparecem explicadas e
/// bloqueadas, porque a informação de que **não** se deve mexer vale tanto
/// quanto a sugestão de mexer.
/// </summary>
public static class ServiceCatalog
{
    public const string Categoria = "Serviços do Windows";

    public static IReadOnlyList<ServiceTweak> Todos { get; } = new[]
    {
        new ServiceTweak
        {
            Nome = "DiagTrack",
            Titulo = "Experiências do Usuário Conectado e Telemetria",
            Explicacao = "Coleta e envia dados de uso para a Microsoft. Desativar não quebra "
                       + "nada do sistema e não afeta atualizações. É o desligamento mais seguro "
                       + "desta lista. Ganho de desempenho: perto de zero — o motivo de desligar é "
                       + "privacidade, não FPS.",
            Risco = RiskLevel.Baixo,
            Sugerido = ServiceStartMode.Disabled
        },

        new ServiceTweak
        {
            Nome = "MapsBroker",
            Titulo = "Gerenciador de Mapas Baixados",
            Explicacao = "Só serve ao aplicativo Mapas com mapas offline. Se você não usa o "
                       + "aplicativo Mapas, não perde nada.",
            Risco = RiskLevel.Baixo,
            Sugerido = ServiceStartMode.Disabled
        },

        new ServiceTweak
        {
            Nome = "RemoteRegistry",
            Titulo = "Registro Remoto",
            Explicacao = "Permite que outra máquina leia e altere o registro desta. Já vem "
                       + "desativado no Windows doméstico; se estiver ligado aqui, desligar é ganho "
                       + "de segurança.",
            Risco = RiskLevel.Baixo,
            Sugerido = ServiceStartMode.Disabled
        },

        new ServiceTweak
        {
            Nome = "WMPNetworkSvc",
            Titulo = "Compartilhamento de Rede do Windows Media Player",
            Explicacao = "Compartilha a biblioteca do Windows Media Player na rede local. "
                       + "Herança de uma época em que isso era usado.",
            Risco = RiskLevel.Baixo,
            Sugerido = ServiceStartMode.Disabled
        },

        new ServiceTweak
        {
            Nome = "Fax",
            Titulo = "Fax",
            Explicacao = "Envia e recebe fax por um modem. Se você tem um modem de fax ligado "
                       + "nesta máquina, mantenha.",
            Risco = RiskLevel.Baixo,
            Sugerido = ServiceStartMode.Disabled
        },

        new ServiceTweak
        {
            Nome = "WSearch",
            Titulo = "Windows Search",
            Explicacao = "Mantém o índice que faz a busca do menu Iniciar e do Explorer ser "
                       + "instantânea. Em Manual, a busca continua funcionando, mas fica lenta e "
                       + "passa a varrer as pastas na hora. Vale considerar em máquina com HDD; em "
                       + "SSD o serviço custa pouco e a busca rápida compensa.",
            Risco = RiskLevel.Baixo,
            Sugerido = ServiceStartMode.Manual
        },

        new ServiceTweak
        {
            Nome = "Spooler",
            Titulo = "Spooler de Impressão",
            Explicacao = "Sem ele não há impressão, nem para PDF. Em Manual ele sobe quando "
                       + "alguma coisa tenta imprimir. Só mude se você realmente não imprime nesta "
                       + "máquina — e lembre que salvar em PDF conta como imprimir.",
            Risco = RiskLevel.Baixo,
            Sugerido = ServiceStartMode.Manual
        },

        // ------------------------------------------------------------------
        // Daqui para baixo: aparece para explicar por que NÃO mexer
        // ------------------------------------------------------------------

        new ServiceTweak
        {
            Nome = "SysMain",
            Titulo = "SysMain (antigo Superfetch)",
            Explicacao = "Toda lista de otimização manda desligar, e está errada. Em SSD o "
                       + "SysMain praticamente não faz leitura antecipada, e o que ele faz hoje é "
                       + "gerenciar memória — inclusive a compressão. Desligar não devolve RAM, "
                       + "devolve cache, e o Windows usa RAM livre para cache de qualquer jeito. "
                       + "Fica como está.",
            Risco = RiskLevel.Medio,
            SomenteExplicar = true,
            Protegido = true
        },

        new ServiceTweak
        {
            Nome = "XblAuthManager",
            Titulo = "Gerenciador de Autenticação do Xbox Live",
            Explicacao = "Sem ele o Game Pass não abre, jogos da Microsoft Store não "
                       + "autenticam e alguns anti-cheat que usam a conta Microsoft falham. Se você "
                       + "não usa nada da Xbox, o serviço fica parado sozinho e não custa nada.",
            Risco = RiskLevel.Medio,
            SomenteExplicar = true,
            Protegido = true
        },

        new ServiceTweak
        {
            Nome = "XboxNetApiSvc",
            Titulo = "Serviço de Rede do Xbox Live",
            Explicacao = "Rede para jogos da Microsoft Store e Game Pass. Mesma história do "
                       + "anterior: parado, não custa; desligado, quebra o Game Pass.",
            Risco = RiskLevel.Medio,
            SomenteExplicar = true,
            Protegido = true
        },

        new ServiceTweak
        {
            Nome = "wuauserv",
            Titulo = "Windows Update",
            Explicacao = "Nunca desativar. Uma máquina sem atualização de segurança é um "
                       + "problema maior do que qualquer FPS que isso renda. O Modo Game já pausa o "
                       + "Update enquanto você joga e o religa ao desligar, que é o jeito certo de "
                       + "resolver a reclamação de update no meio da partida.",
            Risco = RiskLevel.Alto,
            Protegido = true
        },

        new ServiceTweak
        {
            Nome = "WinDefend",
            Titulo = "Antivírus do Microsoft Defender",
            Explicacao = "Proteção em tempo real da máquina. O GameBoost não desliga "
                       + "antivírus, nem o do Windows nem o de terceiros.",
            Risco = RiskLevel.Alto,
            Protegido = true
        },

        new ServiceTweak
        {
            Nome = "SecurityHealthService",
            Titulo = "Serviço de Segurança do Windows",
            Explicacao = "É a Central de Segurança. Desligar cega o Windows sobre o próprio "
                       + "estado de proteção.",
            Risco = RiskLevel.Alto,
            Protegido = true
        }
    };

    public static ServiceTweak? PorNome(string nome)
        => Todos.FirstOrDefault(s => string.Equals(s.Nome, nome, StringComparison.OrdinalIgnoreCase));

    public static string TextoDoModo(ServiceStartMode modo) => modo switch
    {
        ServiceStartMode.Automatic => "automático",
        ServiceStartMode.Manual => "manual",
        ServiceStartMode.Disabled => "desativado",
        ServiceStartMode.Boot => "boot",
        ServiceStartMode.System => "sistema",
        _ => "desconhecido"
    };
}
