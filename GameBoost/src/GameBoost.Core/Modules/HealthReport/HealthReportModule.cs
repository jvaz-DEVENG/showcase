using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Modules.Bottleneck;
using GameBoost.Core.Modules.GameMode;

namespace GameBoost.Core.Modules.HealthReport;

public sealed record HealthSnapshot(
    HealthScore Pontuacao,
    MetricsSnapshot Metricas,
    SystemFacts Fatos,
    string? JogoEmExecucao,
    DateTimeOffset Momento);

/// <summary>
/// Relatorio de saude (secao 5.12): roda todas as varreduras em modo leitura e
/// gera nota por area com os principais findings.
///
/// Nao implementa IModule de proposito: nao ha nada para aplicar nem reverter,
/// e um ApplyAsync que nao faz nada seria pior que nao existir.
/// </summary>
public sealed class HealthReportModule
{
    public const string ModuloId = "healthreport";

    /// <summary>
    /// Amostras coletadas antes de avaliar. A primeira e so linha de base dos
    /// deltas, e as regras de duracao precisam de historico para decidir.
    /// </summary>
    public const int AmostrasParaRelatorio = 8;

    private readonly IMetricsCollector _coletor;
    private readonly SystemFactsReader _fatos;
    private readonly GameDetector _detector;
    private readonly IProcessService _processos;
    private readonly IGameBoostLogger _log;
    private readonly IClock _relogio;

    public HealthReportModule(
        IMetricsCollector coletor,
        SystemFactsReader fatos,
        GameDetector detector,
        IProcessService processos,
        IGameBoostLogger log,
        IClock relogio)
    {
        _coletor = coletor;
        _fatos = fatos;
        _detector = detector;
        _processos = processos;
        _log = log;
        _relogio = relogio;
    }

    public async Task<HealthSnapshot> GerarAsync(IProgress<ModuleProgress>? progresso, CancellationToken ct)
    {
        progresso?.Report(new ModuleProgress("Lendo a configuracao do sistema", 5));
        var fatos = _fatos.Ler(forcar: true);

        progresso?.Report(new ModuleProgress("Medindo o uso da maquina", 15));

        var buffer = new MetricsBuffer(AmostrasParaRelatorio + 2);

        // A primeira coleta so estabelece a linha de base dos deltas.
        _coletor.Coletar();

        for (var i = 0; i < AmostrasParaRelatorio; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(1), ct);

            buffer.Adicionar(_coletor.Coletar());

            var percentual = 15 + (int)(60.0 * (i + 1) / AmostrasParaRelatorio);
            progresso?.Report(new ModuleProgress($"Medindo a maquina ({i + 1}/{AmostrasParaRelatorio})", percentual));
        }

        progresso?.Report(new ModuleProgress("Procurando gargalos", 85));

        var jogo = DetectarJogo();
        var ultimo = buffer.Ultimo
            ?? throw new InvalidOperationException("Nenhuma metrica foi coletada.");

        // Motor proprio, sem debounce herdado de uma sessao de monitoramento:
        // o relatorio precisa refletir so o que foi medido agora.
        var motor = new FindingEngine(_log, _relogio);

        IReadOnlyList<Finding> findings = Array.Empty<Finding>();
        foreach (var snapshot in buffer.Todos())
        {
            findings = motor.Avaliar(new FindingContext
            {
                Atual = snapshot,
                Historico = buffer.Todos(),
                Fatos = fatos,
                JogoEmExecucao = jogo
            });
        }

        progresso?.Report(new ModuleProgress("Pronto", 100));

        var pontuacao = HealthScore.Calcular(findings);
        _log.Info(ModuloId, "Gerar", null,
            $"nota {pontuacao.NotaGeral} ({pontuacao.Conceito}), {findings.Count} findings");

        return new HealthSnapshot(pontuacao, ultimo, fatos, jogo, _relogio.Now);
    }

    private string? DetectarJogo()
    {
        try
        {
            return _detector.Detectar(_processos.GetProcesses())?.Process.Name;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
