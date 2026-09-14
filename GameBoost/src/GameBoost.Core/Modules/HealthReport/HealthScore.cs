using GameBoost.Core.Modules.Bottleneck;

namespace GameBoost.Core.Modules.HealthReport;

public sealed record AreaScore(HealthArea Area, int Nota, IReadOnlyList<Finding> Findings)
{
    public string Nome => Area switch
    {
        HealthArea.Desempenho => "Desempenho",
        HealthArea.Espaco => "Espaco",
        HealthArea.Inicializacao => "Inicializacao",
        HealthArea.ConfiguracaoParaJogos => "Configuracao para jogos",
        _ => Area.ToString()
    };

    public string Conceito => Nota switch
    {
        >= 90 => "Otimo",
        >= 75 => "Bom",
        >= 50 => "Da para melhorar",
        >= 25 => "Precisa de atencao",
        _ => "Critico"
    };
}

/// <summary>
/// Nota 0 a 100 por area (secao 5.12).
///
/// A nota parte de 100 e desconta a penalidade de cada finding. Findings
/// puramente informativos tem penalidade zero de proposito: VBS ativo e GPU no
/// limite sao fatos, nao defeitos, e baixar a nota por eles seria pressionar o
/// usuario a mexer no que nao deve (regra 4).
/// </summary>
public sealed class HealthScore
{
    public static HealthScore Calcular(IReadOnlyList<Finding> findings)
    {
        var areas = new List<AreaScore>();

        foreach (var area in Enum.GetValues<HealthArea>())
        {
            var doGrupo = findings.Where(f => f.Area == area).ToList();
            var desconto = doGrupo.Sum(f => f.Penalidade);
            var nota = Math.Clamp(100 - desconto, 0, 100);

            areas.Add(new AreaScore(area, nota, doGrupo
                .OrderByDescending(f => f.Severidade)
                .ThenByDescending(f => f.Impacto)
                .ToList()));
        }

        return new HealthScore
        {
            Areas = areas,
            Principais = findings
                .OrderByDescending(f => f.Severidade)
                .ThenByDescending(f => f.Impacto)
                .Take(5)
                .ToList()
        };
    }

    public required IReadOnlyList<AreaScore> Areas { get; init; }

    /// <summary>Os 5 principais findings, como pede a secao 5.12.</summary>
    public required IReadOnlyList<Finding> Principais { get; init; }

    /// <summary>Media das areas, arredondada.</summary>
    public int NotaGeral => Areas.Count == 0 ? 100 : (int)Math.Round(Areas.Average(a => a.Nota));

    public string Conceito => NotaGeral switch
    {
        >= 90 => "Otimo",
        >= 75 => "Bom",
        >= 50 => "Da para melhorar",
        >= 25 => "Precisa de atencao",
        _ => "Critico"
    };
}
