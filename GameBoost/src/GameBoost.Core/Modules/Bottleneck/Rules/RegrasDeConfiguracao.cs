namespace GameBoost.Core.Modules.Bottleneck.Rules;

/// <summary>"Plano Balanceado ativo durante o jogo" — item nº 1 do consenso de 2026.</summary>
public sealed class PlanoDeEnergiaBalanceadoRule : IFindingRule
{
    public string Id => "plano-balanceado";

    public Finding? Avaliar(FindingContext ctx)
    {
        if (!ctx.Fatos.PlanoEhBalanceado)
            return null;

        return new Finding
        {
            Id = Id,
            RegraId = Id,
            Titulo = ctx.EmJogo
                ? "Plano de energia Balanceado ativo durante o jogo"
                : "Plano de energia Balanceado ativo",
            Detalhe = "No modo Balanceado a CPU baixa o clock quando acha que pode. Alto desempenho evita "
                    + "isso e e o ajuste de maior ganho real, alem de ser o mais facil de desfazer.",
            Severidade = ctx.EmJogo ? FindingSeverity.Atencao : FindingSeverity.Informativo,
            Impacto = ctx.EmJogo ? 80 : 45,
            Acao = FindingAcao.AtivarAltoDesempenho,
            TextoDaAcao = "Ativar Alto desempenho",
            Area = HealthArea.ConfiguracaoParaJogos,
            Penalidade = 20,
            Momento = ctx.Atual.Momento
        };
    }
}

/// <summary>"Driver NVIDIA de fev/2026" — driver velho. Nunca instala nada.</summary>
public sealed class DriverDeVideoAntigoRule : IFindingRule
{
    public const int MesesLimite = 6;

    public string Id => "driver-antigo";

    public Finding? Avaliar(FindingContext ctx)
    {
        if (ctx.Fatos.DataDoDriver is not { } data)
            return null;

        var meses = (int)((ctx.Atual.Momento - data).TotalDays / 30.0);
        if (meses < MesesLimite)
            return null;

        var fabricante = ctx.Fatos.FabricanteDaGpu ?? "de video";

        return new Finding
        {
            Id = Id,
            RegraId = Id,
            Titulo = $"Driver {fabricante} com {meses} meses",
            Detalhe = $"Instalado em {data:MM/yyyy}. Driver atualizado costuma render FPS em jogos novos. "
                    + "O GameBoost nunca instala driver sozinho: abre a pagina oficial do fabricante.",
            Severidade = FindingSeverity.Atencao,
            Impacto = 50,
            Acao = FindingAcao.AbrirSiteDoDriver,
            TextoDaAcao = "Abrir pagina oficial",
            Area = HealthArea.ConfiguracaoParaJogos,
            Penalidade = 15,
            Momento = ctx.Atual.Momento
        };
    }
}

/// <summary>"23 programas iniciam com o Windows".</summary>
public sealed class InicializacaoLotadaRule : IFindingRule
{
    public const int Limite = 12;

    public string Id => "inicializacao-lotada";

    public Finding? Avaliar(FindingContext ctx)
    {
        var quantidade = ctx.Fatos.ItensNaInicializacao;
        if (quantidade < Limite)
            return null;

        return new Finding
        {
            Id = Id,
            RegraId = Id,
            Titulo = $"{quantidade} programas iniciam com o Windows",
            Detalhe = "Podar a inicializacao e, junto com o plano de energia, o ajuste de maior valor e o "
                    + "mais facil de reverter. Cada item desativado tambem libera RAM o dia inteiro.",
            Severidade = quantidade >= 20 ? FindingSeverity.Atencao : FindingSeverity.Informativo,
            Impacto = Math.Min(quantidade * 3, 65),
            Acao = FindingAcao.AbrirInicializacao,
            TextoDaAcao = "Abrir Inicializacao",
            Area = HealthArea.Inicializacao,
            Penalidade = Math.Min((quantidade - Limite) * 4, 40),
            Momento = ctx.Atual.Momento
        };
    }
}

/// <summary>"Gravacao em segundo plano ativa" — Game DVR ligado.</summary>
public sealed class GravacaoEmSegundoPlanoRule : IFindingRule
{
    public string Id => "game-dvr-ativo";

    public Finding? Avaliar(FindingContext ctx)
    {
        if (!ctx.Fatos.GameDvrAtivo && !ctx.Fatos.GameBarOverlayAtivo)
            return null;

        var oQue = ctx.Fatos.GameDvrAtivo && ctx.Fatos.GameBarOverlayAtivo
            ? "A gravacao em segundo plano e o overlay da Game Bar estao ligados"
            : ctx.Fatos.GameDvrAtivo
                ? "A gravacao em segundo plano esta ligada"
                : "O overlay da Game Bar esta ligado";

        return new Finding
        {
            Id = Id,
            RegraId = Id,
            Titulo = "Gravacao em segundo plano ativa",
            Detalhe = $"{oQue}. Consome CPU e GPU o tempo todo, mesmo sem voce gravar nada. "
                    + "O Modo Game desliga e devolve ao sair.",
            Severidade = FindingSeverity.Informativo,
            Impacto = 35,
            Acao = FindingAcao.AbrirModoGame,
            TextoDaAcao = "Desativar no Modo Game",
            Area = HealthArea.ConfiguracaoParaJogos,
            Penalidade = 10,
            Momento = ctx.Atual.Momento
        };
    }
}

/// <summary>
/// "A atualizacao 26H2 desfez 4 ajustes seus" — o diferencial que nenhum
/// concorrente tem (secao 3.6).
/// </summary>
public sealed class PerfilDesfeitoPorUpdateRule : IFindingRule
{
    public string Id => "perfil-desfeito-por-update";

    public Finding? Avaliar(FindingContext ctx)
    {
        var fatos = ctx.Fatos;

        if (string.IsNullOrEmpty(fatos.BuildDoWindows) || string.IsNullOrEmpty(fatos.BuildAnteriormenteVisto))
            return null;

        if (fatos.BuildDoWindows == fatos.BuildAnteriormenteVisto)
            return null;

        if (fatos.TweaksDesfeitosPorUpdate <= 0)
            return null;

        var plural = fatos.TweaksDesfeitosPorUpdate == 1 ? "ajuste seu" : "ajustes seus";

        return new Finding
        {
            Id = Id,
            RegraId = Id,
            Titulo = $"A atualizacao {fatos.BuildDoWindows} desfez {fatos.TweaksDesfeitosPorUpdate} {plural}",
            Detalhe = "Atualizacoes grandes do Windows resetam Game Mode, plano de energia, efeitos visuais e "
                    + "Game Bar. O GameBoost guardou como estava e pode reaplicar.",
            Severidade = FindingSeverity.Atencao,
            Impacto = 75,
            Acao = FindingAcao.ReaplicarPerfil,
            TextoDaAcao = "Reaplicar perfil",
            Area = HealthArea.ConfiguracaoParaJogos,
            Penalidade = 15,
            Momento = ctx.Atual.Momento
        };
    }
}

/// <summary>"Seu monitor de 165 Hz esta em 60 Hz" — acontece muito apos troca de driver.</summary>
public sealed class MonitorEmTaxaBaixaRule : IFindingRule
{
    public string Id => "monitor-taxa-baixa";

    public Finding? Avaliar(FindingContext ctx)
    {
        var atual = ctx.Fatos.TaxaDeAtualizacaoAtual;
        var maxima = ctx.Fatos.TaxaDeAtualizacaoMaxima;

        if (atual <= 0 || maxima <= 0 || atual >= maxima)
            return null;

        return new Finding
        {
            Id = Id,
            RegraId = Id,
            Titulo = $"Seu monitor de {maxima} Hz esta em {atual} Hz",
            Detalhe = "O Windows costuma voltar para 60 Hz depois de atualizar driver ou trocar de cabo. "
                    + "Corrigir isso e o ganho de fluidez mais barato que existe.",
            Severidade = FindingSeverity.Atencao,
            Impacto = 88,
            Acao = FindingAcao.AbrirConfiguracoesDeVideo,
            TextoDaAcao = "Abrir configuracoes de video",
            Area = HealthArea.ConfiguracaoParaJogos,
            Penalidade = 20,
            Momento = ctx.Atual.Momento
        };
    }
}

/// <summary>
/// "Isolamento do nucleo ativo" — informa e mede, nunca desliga (secao 3.4).
/// </summary>
public sealed class VbsAtivoRule : IFindingRule
{
    public string Id => "vbs-ativo";

    public Finding? Avaliar(FindingContext ctx)
    {
        if (!ctx.Fatos.VbsAtivo)
            return null;

        return new Finding
        {
            Id = Id,
            RegraId = Id,
            Titulo = "Isolamento do nucleo (VBS) ativo",
            Detalhe = "Vem ligado por padrao no Windows 11 e a comunidade mede de 5 a 15% de FPS em jogos "
                    + "limitados por CPU. E protecao real contra malware de kernel: vale desligar so em PC "
                    + "exclusivo de jogo, nunca em maquina de trabalho ou banco. O GameBoost nao altera isto; "
                    + "a decisao e sua, nas Configuracoes do Windows.",
            Severidade = FindingSeverity.Informativo,
            Impacto = 25,
            Acao = FindingAcao.AbrirSegurancaDoWindows,
            TextoDaAcao = "Abrir Seguranca do Windows",
            Area = HealthArea.ConfiguracaoParaJogos,
            Penalidade = 0,
            Momento = ctx.Atual.Momento
        };
    }
}

/// <summary>"Jogo instalado em HDD. Mova para SSD".</summary>
public sealed class JogoEmHddRule : IFindingRule
{
    public string Id => "jogo-em-hdd";

    public Finding? Avaliar(FindingContext ctx)
    {
        if (!ctx.Fatos.JogoEmHdd)
            return null;

        var nome = ctx.Fatos.JogoEmHddNome ?? "Um jogo";

        return new Finding
        {
            Id = Id,
            RegraId = Id,
            Titulo = $"{nome} esta instalado em HDD",
            Detalhe = "Lancamentos de 2025 e 2026 assumem SSD e engasgam em disco mecanico, principalmente ao "
                    + "carregar textura. Mover para SSD resolve engasgo que nenhum ajuste de software resolve.",
            Severidade = FindingSeverity.Atencao,
            Impacto = 72,
            Area = HealthArea.ConfiguracaoParaJogos,
            Penalidade = 15,
            Momento = ctx.Atual.Momento
        };
    }
}

/// <summary>Memoria em single channel: metade da banda, sem custo nenhum para descobrir.</summary>
public sealed class MemoriaSingleChannelRule : IFindingRule
{
    public string Id => "memoria-single-channel";

    public Finding? Avaliar(FindingContext ctx)
    {
        if (ctx.Fatos.PentesDeMemoria != 1)
            return null;

        return new Finding
        {
            Id = Id,
            RegraId = Id,
            Titulo = "Memoria em canal unico",
            Detalhe = "Ha um unico pente de RAM instalado. Com dois pentes iguais em canal duplo, a banda de "
                    + "memoria dobra, e isso costuma render FPS de verdade em jogos limitados por CPU e em "
                    + "notebooks com video integrado.",
            Severidade = FindingSeverity.Informativo,
            Impacto = 55,
            Area = HealthArea.ConfiguracaoParaJogos,
            Penalidade = 10,
            Momento = ctx.Atual.Momento
        };
    }
}
