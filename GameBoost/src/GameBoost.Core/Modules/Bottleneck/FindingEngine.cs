using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Modules.Bottleneck.Rules;

namespace GameBoost.Core.Modules.Bottleneck;

/// <summary>
/// Roda todas as regras e devolve os findings ordenados por impacto.
///
/// O debounce de 30 s (criterio de aceite da secao 5.5) impede que a lista
/// pisque a cada segundo: um finding que ja esta na tela nao e reemitido, e um
/// que sumiu so e esquecido depois da janela, evitando que oscile na fronteira
/// do limite.
/// </summary>
public sealed class FindingEngine
{
    public static readonly TimeSpan JanelaDeDebounce = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyList<IFindingRule> _regras;
    private readonly IGameBoostLogger _log;
    private readonly IClock _relogio;
    private readonly Dictionary<string, (Finding Finding, DateTimeOffset VistoPorUltimo)> _ativos = new();
    private readonly object _gate = new();

    public FindingEngine(IGameBoostLogger log, IClock relogio, IReadOnlyList<IFindingRule>? regras = null)
    {
        _log = log;
        _relogio = relogio;
        _regras = regras ?? RegrasPadrao();
    }

    /// <summary>Todas as regras da tabela da secao 5.5.</summary>
    public static IReadOnlyList<IFindingRule> RegrasPadrao() => new IFindingRule[]
    {
        new ProcessoDevorandoCpuRule(),
        new RamNoLimiteRule(),
        new StandbyListInchadaRule(),
        new DiscoDoSistemaCheioRule(),
        new GpuNoLimiteRule(),
        new GargaloDeCpuEmJogoRule(),
        new ThrottlingTermicoRule(),
        new ProcessoDeSistemaOcupandoDiscoRule(),
        new PlanoDeEnergiaBalanceadoRule(),
        new DriverDeVideoAntigoRule(),
        new InicializacaoLotadaRule(),
        new GravacaoEmSegundoPlanoRule(),
        new PerfilDesfeitoPorUpdateRule(),
        new MonitorEmTaxaBaixaRule(),
        new VbsAtivoRule(),
        new JogoEmHddRule(),
        new MemoriaSingleChannelRule()
    };

    public int QuantidadeDeRegras => _regras.Count;

    /// <summary>
    /// Avalia o contexto e devolve a lista atual de findings, ja com debounce.
    /// Uma regra que lance excecao e isolada: as demais continuam valendo.
    /// </summary>
    public IReadOnlyList<Finding> Avaliar(FindingContext contexto)
    {
        var agora = contexto.Atual.Momento;
        var encontrados = new List<Finding>();

        foreach (var regra in _regras)
        {
            try
            {
                var finding = regra.Avaliar(contexto);
                if (finding is not null)
                    encontrados.Add(finding);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
            {
                _log.Error("Bottleneck", "Regra", regra.Id, "falhou ao avaliar", ex);
            }
        }

        lock (_gate)
        {
            foreach (var finding in encontrados)
            {
                if (_ativos.TryGetValue(finding.Id, out var existente))
                {
                    // Ja esta na tela: atualiza o texto, mantem a posicao.
                    _ativos[finding.Id] = (finding, agora);
                }
                else
                {
                    _ativos[finding.Id] = (finding, agora);
                    _log.Info("Bottleneck", "Finding", finding.RegraId, finding.Titulo);
                }
            }

            // Some so depois da janela: evita piscar na fronteira do limite.
            var expirados = _ativos
                .Where(kv => agora - kv.Value.VistoPorUltimo > JanelaDeDebounce)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var id in expirados)
                _ativos.Remove(id);

            return _ativos.Values
                .Select(v => v.Finding)
                .OrderByDescending(f => f.Severidade)
                .ThenByDescending(f => f.Impacto)
                .ToList();
        }
    }

    public IReadOnlyList<Finding> Atuais()
    {
        lock (_gate)
        {
            return _ativos.Values
                .Select(v => v.Finding)
                .OrderByDescending(f => f.Severidade)
                .ThenByDescending(f => f.Impacto)
                .ToList();
        }
    }

    public void Limpar()
    {
        lock (_gate) _ativos.Clear();
    }
}
