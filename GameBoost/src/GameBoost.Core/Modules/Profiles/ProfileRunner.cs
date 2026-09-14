using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Modules.Tweaks;
using GameBoost.Core.Native;
using GameBoost.Core.State;

namespace GameBoost.Core.Modules.Profiles;

/// <summary>O que foi aplicado numa sessão de perfil, para poder desfazer.</summary>
public sealed class SessaoDePerfil
{
    public required GameProfile Perfil { get; init; }
    public required int Pid { get; init; }
    public DateTimeOffset Inicio { get; init; }

    public ProcessPriority? PrioridadeAnterior { get; set; }
    public IReadOnlyList<int> AfinidadeAnterior { get; set; } = Array.Empty<int>();
    public bool TimerAlterado { get; set; }
    public List<string> ChangeIds { get; } = new();
    public List<string> Aplicado { get; } = new();
}

/// <summary>
/// Aplica e desfaz um perfil de jogo (seção 5.11).
///
/// A regra que rege esta classe: **tudo que entra tem que sair.** Cada coisa
/// aplicada guarda o valor anterior antes, e a saída percorre a lista de trás
/// para frente. Se o jogo fecha e algo falha ao reverter, o resto continua
/// sendo revertido — um erro no timer não pode deixar a prioridade alterada
/// para sempre.
/// </summary>
public sealed class ProfileRunner
{
    private readonly IProcessService _processos;
    private readonly TweaksModule _tweaks;
    private readonly IStateBackup _backup;
    private readonly IRollbackEngine _rollback;
    private readonly IGameBoostLogger _log;
    private readonly IClock _relogio;

    public ProfileRunner(
        IProcessService processos,
        TweaksModule tweaks,
        IStateBackup backup,
        IRollbackEngine rollback,
        IGameBoostLogger log,
        IClock relogio)
    {
        _processos = processos;
        _tweaks = tweaks;
        _backup = backup;
        _rollback = rollback;
        _log = log;
        _relogio = relogio;
    }

    public SessaoDePerfil? Atual { get; private set; }

    public bool EmSessao => Atual is not null;

    // ==================================================================
    // Entrar
    // ==================================================================

    public SessaoDePerfil Aplicar(GameProfile perfil, int pid)
    {
        if (Atual is not null)
        {
            _log.Warn("profiles", "Aplicar", perfil.Nome,
                "já havia sessão aberta; a anterior é desfeita antes");
            Desfazer();
        }

        var sessao = new SessaoDePerfil
        {
            Perfil = perfil,
            Pid = pid,
            Inicio = _relogio.Now
        };

        AplicarPrioridade(sessao);
        AplicarAfinidade(sessao);
        AplicarTimer(sessao);
        AplicarTweaks(sessao);

        Atual = sessao;

        perfil.UltimaVez = _relogio.Now;
        perfil.VezesUsado++;

        _log.Info("profiles", "Aplicar", perfil.Nome,
            sessao.Aplicado.Count == 0 ? "nada a aplicar" : string.Join(", ", sessao.Aplicado));

        return sessao;
    }

    private void AplicarPrioridade(SessaoDePerfil sessao)
    {
        var alvo = sessao.Perfil.Prioridade;

        if (alvo is null or ProcessPriority.Normal)
            return;

        sessao.PrioridadeAnterior = _processos.GetPriority(sessao.Pid);

        if (_processos.SetPriority(sessao.Pid, alvo.Value))
            sessao.Aplicado.Add($"prioridade {GameProfile.TextoDaPrioridade(alvo.Value)}");
    }

    private void AplicarAfinidade(SessaoDePerfil sessao)
    {
        if (sessao.Perfil.Afinidade.Count == 0)
            return;

        sessao.AfinidadeAnterior = _processos.GetAffinity(sessao.Pid);

        if (_processos.SetAffinity(sessao.Pid, sessao.Perfil.Afinidade))
            sessao.Aplicado.Add($"{sessao.Perfil.Afinidade.Count} núcleos reservados");
    }

    /// <summary>
    /// Resolução de timer em 0,5 ms.
    ///
    /// Vale muito menos do que a internet promete. Desde o Windows 10 2004 o
    /// pedido é **por processo**, não global: o GameBoost pedir 0,5 ms afeta o
    /// GameBoost, não o jogo. O que ainda acontece é o sistema honrar a menor
    /// resolução pedida por alguém para temporizadores globais, e é daí que vem
    /// o ganho residual no Windows 10.
    ///
    /// Por isso fica desligado por padrão e o texto na tela não promete FPS.
    /// </summary>
    private void AplicarTimer(SessaoDePerfil sessao)
    {
        if (!sessao.Perfil.TimerDeMeioMilissegundo)
            return;

        // 5000 unidades de 100 ns = 0,5 ms.
        if (NativeMethodsBridge.SetTimerResolution(5000, out var atual))
        {
            sessao.TimerAlterado = true;
            sessao.Aplicado.Add($"timer em {atual / 10000.0:0.0} ms");

            _backup.Registrar(new ChangeRecord
            {
                Modulo = "profiles",
                Tipo = ChangeType.TimerResolution,
                Alvo = "timer",
                ValorAnterior = "padrão do sistema",
                ValorNovo = "0,5 ms",
                Extras = { ["nome"] = $"Timer de 0,5 ms para {sessao.Perfil.Nome}" }
            });
        }
    }

    private void AplicarTweaks(SessaoDePerfil sessao)
    {
        var ligar = sessao.Perfil.Tweaks.Where(t => t.Ligar).Select(t => "tweak:" + t.Id).ToList();

        if (ligar.Count == 0)
            return;

        // Os ChangeRecord são criados pelo próprio TweaksModule. O que fica
        // guardado aqui são os ids, para desfazer só o que este perfil ligou.
        var antes = _backup.Pendentes.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var resultado = _tweaks.ApplyAsync(ligar, dryRun: false, CancellationToken.None)
            .GetAwaiter().GetResult();

        foreach (var registro in _backup.Pendentes.Where(r => !antes.Contains(r.Id)))
            sessao.ChangeIds.Add(registro.Id);

        if (resultado.Sucessos > 0)
            sessao.Aplicado.Add($"{resultado.Sucessos} tweaks");
    }

    // ==================================================================
    // Sair
    // ==================================================================

    /// <summary>
    /// Desfaz tudo o que a sessão aplicou, na ordem inversa.
    ///
    /// Cada passo é independente: uma falha não interrompe os seguintes. É o
    /// mesmo princípio do RollbackEngine, e pelo mesmo motivo — o pior estado
    /// possível é aquele em que metade das alterações ficou de pé.
    /// </summary>
    public IReadOnlyList<string> Desfazer()
    {
        var sessao = Atual;

        if (sessao is null)
            return Array.Empty<string>();

        Atual = null;

        var feitos = new List<string>();

        if (sessao.ChangeIds.Count > 0)
        {
            try
            {
                var voltas = _rollback.Reverter(sessao.ChangeIds, dryRun: false);
                feitos.Add($"{voltas.Count(v => v.Sucesso)} tweaks revertidos");
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                _log.Error("profiles", "Desfazer", "tweaks", ex.Message, ex);
            }
        }

        if (sessao.TimerAlterado)
        {
            try
            {
                if (NativeMethodsBridge.RestoreTimerResolution(5000, out _))
                    feitos.Add("timer devolvido ao padrão");
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                _log.Warn("profiles", "Desfazer", "timer", ex.Message);
            }
        }

        // Prioridade e afinidade só valem se o processo ainda existir. Quando o
        // jogo já fechou, não há o que restaurar — e é o caso normal.
        if (_processos.GetProcess(sessao.Pid) is not null)
        {
            if (sessao.AfinidadeAnterior.Count > 0)
            {
                _processos.SetAffinity(sessao.Pid, sessao.AfinidadeAnterior);
                feitos.Add("afinidade restaurada");
            }

            if (sessao.PrioridadeAnterior is not null)
            {
                _processos.SetPriority(sessao.Pid, sessao.PrioridadeAnterior.Value);
                feitos.Add("prioridade restaurada");
            }
        }

        _log.Info("profiles", "Desfazer", sessao.Perfil.Nome,
            feitos.Count == 0 ? "nada a desfazer" : string.Join(", ", feitos));

        return feitos;
    }
}
