using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Modules.GameMode;

namespace GameBoost.Core.Modules.Profiles;

/// <summary>Um jogo começou a rodar.</summary>
public sealed record JogoAbriu(DetectedGame Jogo, GameProfile? Perfil, bool TelaCheia);

/// <summary>Um jogo que estava rodando saiu.</summary>
public sealed record JogoFechou(string Executavel, string Nome, TimeSpan Duracao);

/// <summary>
/// Vigia os processos e avisa quando um jogo abre ou fecha (seção 5.1).
///
/// **Por polling, não por WMI.** O spec sugere `Win32_ProcessStartTrace`, que
/// entrega o evento na hora. O problema é que ele exige uma consulta WMI
/// permanente, e foi justamente WMI em laço que estourou o orçamento de CPU na
/// Fase 1 — a coleta chegou a 5% antes de o WMI sair dela. Um polling de 2 s
/// lista processos por `NtQuerySystemInformation`, que é o caminho barato, e
/// 2 segundos de atraso não importam para algo que o usuário vai confirmar num
/// diálogo.
/// </summary>
public sealed class GameWatcher : IDisposable
{
    /// <summary>Intervalo do spec (5.1).</summary>
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(2);

    private readonly IProcessService _processos;
    private readonly GameDetector _detector;
    private readonly ProfileStore _perfis;
    private readonly IGameBoostLogger _log;
    private readonly IClock _relogio;

    private Thread? _thread;
    private CancellationTokenSource? _cts;

    /// <summary>Jogo em execução agora, se houver.</summary>
    private DetectedGame? _atual;
    private DateTimeOffset _desde;

    /// <summary>
    /// Executáveis que o usuário já recusou nesta sessão. Sem isto, recusar o
    /// convite faria a pergunta voltar dois segundos depois, para sempre.
    /// </summary>
    private readonly HashSet<string> _recusados = new(StringComparer.OrdinalIgnoreCase);

    public GameWatcher(
        IProcessService processos,
        GameDetector detector,
        ProfileStore perfis,
        IGameBoostLogger log,
        IClock relogio)
    {
        _processos = processos;
        _detector = detector;
        _perfis = perfis;
        _log = log;
        _relogio = relogio;
    }

    public event Action<JogoAbriu>? AoAbrir;
    public event Action<JogoFechou>? AoFechar;

    public bool Ativo => _thread is { IsAlive: true };

    public DetectedGame? JogoAtual => _atual;

    public void Iniciar()
    {
        if (Ativo)
            return;

        _cts = new CancellationTokenSource();

        // Thread dedicada e de prioridade baixa, como o monitor de gargalos: o
        // vigia nunca pode disputar CPU com o jogo que ele está vigiando.
        _thread = new Thread(() => Laco(_cts.Token))
        {
            IsBackground = true,
            Name = "GameBoost.GameWatcher",
            Priority = ThreadPriority.BelowNormal
        };

        _thread.Start();
        _log.Info("profiles", "Watcher", null, "vigia de jogos iniciado");
    }

    public void Parar()
    {
        _cts?.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(3));
        _thread = null;
        _log.Info("profiles", "Watcher", null, "vigia de jogos parado");
    }

    /// <summary>O usuário disse não para este jogo; não perguntar de novo hoje.</summary>
    public void Recusar(string executavel) => _recusados.Add(GameProfile.Normalizar(executavel));

    public void EsquecerRecusas() => _recusados.Clear();

    private void Laco(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Verificar();
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
            {
                _log.Warn("profiles", "Watcher", null, ex.Message);
            }

            if (ct.WaitHandle.WaitOne(Intervalo))
                return;
        }
    }

    private void Verificar()
    {
        var processos = _processos.GetProcesses();

        // O jogo que já estava rodando ainda está?
        if (_atual is not null)
        {
            var aindaVivo = processos.Any(p => p.Pid == _atual.Process.Pid);

            if (!aindaVivo)
            {
                var fechado = _atual;
                var duracao = _relogio.Now - _desde;
                _atual = null;

                _log.Info("profiles", "Watcher", fechado.Process.Name,
                    $"jogo saiu depois de {duracao.TotalMinutes:0} min");

                AoFechar?.Invoke(new JogoFechou(
                    GameProfile.Normalizar(fechado.Process.Name),
                    fechado.Process.Name,
                    duracao));
            }
            else
            {
                // Já há jogo rodando: não procurar outro. Dois jogos ao mesmo
                // tempo é caso raro, e trocar de perfil no meio seria pior que
                // ficar no primeiro.
                return;
            }
        }

        var detectado = _detector.Detectar(processos);

        if (detectado is null)
            return;

        var chave = GameProfile.Normalizar(detectado.Process.Name);

        if (_recusados.Contains(chave))
            return;

        var telaCheia = Native.WindowInfo.EstaEmTelaCheia(detectado.Process.Pid);

        // A tela cheia é o terceiro critério do spec, e vale como reforço: um
        // processo pesado com janela ocupando o monitor inteiro é jogo com
        // muito mais frequência do que é outra coisa.
        if (detectado.Confianca < 50 && !telaCheia)
            return;

        _atual = detectado;
        _desde = _relogio.Now;

        var perfil = _perfis.Buscar(chave);

        _log.Info("profiles", "Watcher", detectado.Process.Name,
            $"{detectado.Motivo}, confiança {detectado.Confianca}, tela cheia {telaCheia}");

        AoAbrir?.Invoke(new JogoAbriu(detectado, perfil, telaCheia));
    }

    public void Dispose()
    {
        Parar();
        _cts?.Dispose();
    }
}
