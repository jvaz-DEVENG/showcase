namespace GameBoost.Core.Modules.Bottleneck.Rules;

/// <summary>Formatacao compartilhada pelas regras. Texto para gente, nao para log.</summary>
internal static class Fmt
{
    public static string Bytes(long bytes) => GameMode.GameModeModule.Formatar(bytes);

    public static string Bytes(double bytes) => GameMode.GameModeModule.Formatar((long)bytes);

    public static string Percent(double valor) => $"{valor:0}%";
}

/// <summary>"Chrome esta usando 38% da CPU" — processo pesado fora do jogo.</summary>
public sealed class ProcessoDevorandoCpuRule : IFindingRule
{
    public const double Limite = 25.0;
    private static readonly TimeSpan Janela = TimeSpan.FromSeconds(30);

    public string Id => "processo-cpu-alta";

    public Finding? Avaliar(FindingContext ctx)
    {
        var vilao = ctx.Atual.Processos
            .Where(p => p.CpuPercent >= Limite)
            .Where(p => !EhOJogo(p, ctx))
            .MaxBy(p => p.CpuPercent);

        if (vilao is null)
            return null;

        // Um pico de 1 segundo nao e problema. So vale se persistir.
        var sustentado = ctx.SustentadoPor(Janela,
            s => s.Processos.Any(p => p.Nome.Equals(vilao.Nome, StringComparison.OrdinalIgnoreCase)
                                   && p.CpuPercent >= Limite));

        if (!sustentado)
            return null;

        var instancias = vilao.Instancias > 1 ? $" em {vilao.Instancias} processos" : string.Empty;

        return new Finding
        {
            Id = $"{Id}:{vilao.Nome}",
            RegraId = Id,
            Titulo = $"{vilao.Nome} esta usando {Fmt.Percent(vilao.CpuPercent)} da CPU",
            Detalhe = $"Ha pelo menos 30 segundos{instancias}, ocupando {Fmt.Bytes(vilao.WorkingSetBytes)} de RAM. "
                    + "Fechar antes de jogar libera CPU para o jogo.",
            Severidade = vilao.CpuPercent >= 50 ? FindingSeverity.Critico : FindingSeverity.Atencao,
            Impacto = (int)vilao.CpuPercent,
            Acao = FindingAcao.AbrirModoGame,
            TextoDaAcao = "Ver no Modo Game",
            Area = HealthArea.Desempenho,
            Penalidade = vilao.CpuPercent >= 50 ? 20 : 10,
            Momento = ctx.Atual.Momento
        };
    }

    private static bool EhOJogo(ProcessUsage p, FindingContext ctx)
        => ctx.JogoEmExecucao is not null
           && p.Nome.Equals(ctx.JogoEmExecucao, StringComparison.OrdinalIgnoreCase);
}

/// <summary>"So 1,2 GB de RAM livre" — memoria no limite.</summary>
public sealed class RamNoLimiteRule : IFindingRule
{
    public const double LimitePercent = 10.0;

    public string Id => "ram-no-limite";

    public Finding? Avaliar(FindingContext ctx)
    {
        if (ctx.Atual.RamTotalBytes == 0 || ctx.Atual.RamDisponivelPercent >= LimitePercent)
            return null;

        var maiores = ctx.Atual.Processos
            .OrderByDescending(p => p.WorkingSetBytes)
            .Take(2)
            .ToList();

        var culpados = maiores.Count == 2
            ? $"{maiores[0].Nome} e {maiores[1].Nome} usam {Fmt.Bytes(maiores[0].WorkingSetBytes + maiores[1].WorkingSetBytes)}."
            : string.Empty;

        return new Finding
        {
            Id = Id,
            RegraId = Id,
            Titulo = $"So {Fmt.Bytes(ctx.Atual.RamDisponivelBytes)} de RAM livre",
            Detalhe = $"{Fmt.Percent(ctx.Atual.RamUsadaPercent)} da memoria em uso. {culpados}".Trim(),
            Severidade = FindingSeverity.Critico,
            Impacto = 90,
            Acao = FindingAcao.LimparRam,
            TextoDaAcao = "Limpar RAM",
            Area = HealthArea.Desempenho,
            Penalidade = 25,
            Momento = ctx.Atual.Momento
        };
    }
}

/// <summary>"6 GB de RAM presos em cache do sistema" — Standby List inchada.</summary>
public sealed class StandbyListInchadaRule : IFindingRule
{
    public const double LimitePercent = 40.0;

    public string Id => "standby-inchada";

    public Finding? Avaliar(FindingContext ctx)
    {
        if (ctx.Atual.StandbyBytes == 0 || ctx.Atual.StandbyPercent < LimitePercent)
            return null;

        return new Finding
        {
            Id = Id,
            RegraId = Id,
            Titulo = $"{Fmt.Bytes(ctx.Atual.StandbyBytes)} de RAM presos em cache do sistema",
            Detalhe = "A Standby List guarda dados ja usados por precaucao. Liberar ajuda quando falta RAM, "
                    + "mas nao aumenta FPS por si so.",
            Severidade = FindingSeverity.Informativo,
            Impacto = 40,
            Acao = FindingAcao.PurgarStandby,
            TextoDaAcao = "Purgar Standby",
            Area = HealthArea.Desempenho,
            Penalidade = 5,
            Momento = ctx.Atual.Momento
        };
    }
}

/// <summary>"C: com 94% de uso" — disco do sistema cheio.</summary>
public sealed class DiscoDoSistemaCheioRule : IFindingRule
{
    public const double LimitePercent = 90.0;

    public string Id => "disco-cheio";

    public Finding? Avaliar(FindingContext ctx)
    {
        if (ctx.Atual.DiscoSistemaUsadoPercent < LimitePercent)
            return null;

        return new Finding
        {
            Id = Id,
            RegraId = Id,
            Titulo = $"Disco do sistema com {Fmt.Percent(ctx.Atual.DiscoSistemaUsadoPercent)} de uso",
            Detalhe = "SSD muito cheio perde desempenho de escrita, e o Windows precisa de espaco livre "
                    + "para arquivo de paginacao e atualizacoes.",
            Severidade = ctx.Atual.DiscoSistemaUsadoPercent >= 95 ? FindingSeverity.Critico : FindingSeverity.Atencao,
            Impacto = 70,
            Acao = FindingAcao.AbrirAnalisadorDeDisco,
            TextoDaAcao = "Ver o que ocupa espaco",
            Area = HealthArea.Espaco,
            Penalidade = ctx.Atual.DiscoSistemaUsadoPercent >= 95 ? 40 : 20,
            Momento = ctx.Atual.Momento
        };
    }
}

/// <summary>"GPU no maximo" — o gargalo e a placa de video.</summary>
public sealed class GpuNoLimiteRule : IFindingRule
{
    public const double LimitePercent = 95.0;

    public string Id => "gpu-no-limite";

    public Finding? Avaliar(FindingContext ctx)
    {
        if (!ctx.Atual.GpuPercent.Disponivel || ctx.Atual.GpuPercent.Valor < LimitePercent)
            return null;

        if (!ctx.SustentadoPor(TimeSpan.FromSeconds(20),
                s => s.GpuPercent.Disponivel && s.GpuPercent.Valor >= LimitePercent))
        {
            return null;
        }

        return new Finding
        {
            Id = Id,
            RegraId = Id,
            Titulo = "Placa de video no limite",
            Detalhe = "A GPU esta em 100%. Se o FPS estiver baixo, o caminho e reduzir resolucao ou "
                    + "qualidade grafica, ou ligar DLSS/FSR. Fechar programas nao ajuda neste caso.",
            Severidade = FindingSeverity.Informativo,
            Impacto = 60,
            Area = HealthArea.Desempenho,
            Penalidade = 0,
            Momento = ctx.Atual.Momento
        };
    }
}

/// <summary>"Gargalo de CPU: o processador nao alimenta a placa de video".</summary>
public sealed class GargaloDeCpuEmJogoRule : IFindingRule
{
    public string Id => "gargalo-cpu-em-jogo";

    public Finding? Avaliar(FindingContext ctx)
    {
        if (!ctx.EmJogo || !ctx.Atual.GpuPercent.Disponivel)
            return null;

        if (ctx.Atual.CpuPercent < 95 || ctx.Atual.GpuPercent.Valor >= 60)
            return null;

        if (!ctx.SustentadoPor(TimeSpan.FromSeconds(15),
                s => s.CpuPercent >= 90 && s.GpuPercent.Disponivel && s.GpuPercent.Valor < 70))
        {
            return null;
        }

        return new Finding
        {
            Id = Id,
            RegraId = Id,
            Titulo = "Gargalo de CPU",
            Detalhe = $"A CPU esta em {Fmt.Percent(ctx.Atual.CpuPercent)} e a GPU em "
                    + $"{Fmt.Percent(ctx.Atual.GpuPercent.Valor)}: o processador nao consegue preparar quadros "
                    + "rapido o suficiente e a placa de video fica esperando. Fechar apps em segundo plano ajuda.",
            Severidade = FindingSeverity.Atencao,
            Impacto = 85,
            Acao = FindingAcao.AbrirModoGame,
            TextoDaAcao = "Abrir Modo Game",
            Area = HealthArea.Desempenho,
            Penalidade = 15,
            Momento = ctx.Atual.Momento
        };
    }
}

/// <summary>"CPU a 97 graus reduzindo clock" — throttling termico.</summary>
public sealed class ThrottlingTermicoRule : IFindingRule
{
    public const double LimiteTemperatura = 90.0;

    public string Id => "throttling-termico";

    public Finding? Avaliar(FindingContext ctx)
    {
        if (!ctx.Atual.TemperaturaCpu.Disponivel || ctx.Atual.TemperaturaCpu.Valor < LimiteTemperatura)
            return null;

        if (ctx.Atual.CpuPercent < 70)
            return null;

        var frequencia = ctx.Atual.CpuFrequenciaPercent;
        var reduzindo = frequencia.Disponivel && frequencia.Valor < 80;

        return new Finding
        {
            Id = Id,
            RegraId = Id,
            Titulo = $"CPU a {ctx.Atual.TemperaturaCpu.Valor:0} graus",
            Detalhe = reduzindo
                ? "O processador esta reduzindo a velocidade para nao superaquecer. Sinal de cooler sujo "
                + "ou pasta termica velha."
                : "Temperatura alta sob carga. Se o FPS cair de repente durante o jogo, o motivo e este.",
            Severidade = FindingSeverity.Critico,
            Impacto = 95,
            Area = HealthArea.Desempenho,
            Penalidade = 20,
            Momento = ctx.Atual.Momento
        };
    }
}

/// <summary>"Windows Defender fazendo varredura completa" — informativo, nunca desativar.</summary>
public sealed class ProcessoDeSistemaOcupandoDiscoRule : IFindingRule
{
    private static readonly IReadOnlyDictionary<string, string> Conhecidos =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MsMpEng"] = "O Windows Defender esta varrendo arquivos",
            ["SearchIndexer"] = "O Windows Search esta indexando arquivos",
            ["SearchHost"] = "A busca do Windows esta trabalhando",
            ["TiWorker"] = "O Windows esta instalando uma atualizacao",
            ["TrustedInstaller"] = "O Windows esta instalando uma atualizacao",
            ["wuauclt"] = "O Windows Update esta trabalhando",
            ["CompatTelRunner"] = "O Windows esta coletando dados de compatibilidade"
        };

    public string Id => "sistema-ocupando-disco";

    public Finding? Avaliar(FindingContext ctx)
    {
        var culpado = ctx.Atual.Processos
            .Where(p => Conhecidos.ContainsKey(p.Nome) && p.CpuPercent >= 8)
            .MaxBy(p => p.CpuPercent);

        if (culpado is null)
            return null;

        if (!ctx.SustentadoPor(TimeSpan.FromSeconds(20),
                s => s.Processos.Any(p => p.Nome.Equals(culpado.Nome, StringComparison.OrdinalIgnoreCase)
                                       && p.CpuPercent >= 5)))
        {
            return null;
        }

        return new Finding
        {
            Id = $"{Id}:{culpado.Nome}",
            RegraId = Id,
            Titulo = Conhecidos[culpado.Nome],
            Detalhe = $"Usando {Fmt.Percent(culpado.CpuPercent)} da CPU. E uma tarefa normal do Windows e "
                    + "termina sozinha. O GameBoost nao desativa protecao nem atualizacao.",
            Severidade = FindingSeverity.Informativo,
            Impacto = 30,
            Area = HealthArea.Desempenho,
            Penalidade = 0,
            Momento = ctx.Atual.Momento
        };
    }
}
