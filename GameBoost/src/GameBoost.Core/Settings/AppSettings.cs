using System.Text.Json.Serialization;

namespace GameBoost.Core.Settings;

/// <summary>App que o usuario marcou com estrela para reabrir sozinho ao sair do Modo Game.</summary>
public sealed class EssentialApp
{
    public string Nome { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string? Argumentos { get; set; }
    public string? WorkingDirectory { get; set; }
}

public sealed class AppSettings
{
    /// <summary>Incrementar sempre que o formato mudar e escrever o migrador correspondente.</summary>
    public const int VersaoAtual = 2;

    /// <summary>
    /// Comeca em 0 de proposito: um settings.json da v1, que nao gravava este
    /// campo, precisa desserializar como 0 para cair no migrador. Com o padrao
    /// em VersaoAtual o arquivo antigo se disfarçaria de atual. Quem grava
    /// carimba a versao corrente no Save.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    public string Tema { get; set; } = "Dark";

    /// <summary>Processos que o usuario nunca quer que apareçam pre-marcados.</summary>
    public List<string> NuncaEncerrar { get; set; } = new();

    /// <summary>Processos que o usuario sempre quer encerrar, mesmo sem pre-marcacao padrao.</summary>
    public List<string> SempreEncerrar { get; set; } = new();

    public List<EssentialApp> Essenciais { get; set; } = new();

    public bool PausarWindowsUpdate { get; set; } = true;
    public bool SilenciarNotificacoes { get; set; } = true;
    public bool DesativarGameDvr { get; set; } = true;
    public bool AplicarPlanoEnergia { get; set; } = true;
    public bool SubirPrioridadeDoJogo { get; set; } = true;
    public bool LimparRam { get; set; } = true;
    public bool PurgarStandbyList { get; set; } = true;

    /// <summary>Modo "so o essencial": so lista processos acima destes limites.</summary>
    public bool SoOEssencial { get; set; }
    public double LimiteCpuPercent { get; set; } = 2.0;
    public long LimiteRamMegabytes { get; set; } = 300;

    /// <summary>
    /// Build do Windows na ultima execucao. E o que permite detectar que uma
    /// atualizacao grande desfez os ajustes do usuario (secao 3.4).
    /// </summary>
    public string? UltimaBuildDoWindows { get; set; }

    public bool IniciarComWindows { get; set; }
    public bool IniciarMinimizado { get; set; }
    public bool VerificarAtualizacoes { get; set; } = true;

    /// <summary>
    /// Autoriza o app a falar com a internet: teste de velocidade, consulta
    /// STUN para descobrir o tipo de NAT e comparacao de DNS.
    ///
    /// **Desligado por padrao.** Regra 8 do spec: sem telemetria e sem chamada
    /// externa que o usuario nao tenha pedido. Nada aqui manda dado nenhum
    /// sobre a maquina; ainda assim, sair para a rede e uma escolha dele.
    /// </summary>
    public bool PermitirAcessoARede { get; set; }

    /// <summary>Icone na bandeja com o menu da secao 6.</summary>
    public bool MostrarNaBandeja { get; set; } = true;

    /// <summary>
    /// Vigiar os processos e avisar quando um jogo abrir (secao 5.1).
    ///
    /// Ligado por padrao: e a funcao que o usuario espera de um booster, e o
    /// custo e um polling de 2 s numa thread de prioridade baixa. Mesmo ligado,
    /// ele **pergunta** antes de ativar qualquer coisa, a nao ser que o perfil
    /// daquele jogo diga o contrario.
    /// </summary>
    public bool DetectarJogosAutomaticamente { get; set; } = true;

    /// <summary>As tres telas da primeira abertura ja foram vistas.</summary>
    public bool OnboardingConcluido { get; set; }
}
