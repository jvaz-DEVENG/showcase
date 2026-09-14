using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Safety;
using GameBoost.Core.Services;
using GameBoost.Core.Settings;
using GameBoost.Core.State;

namespace GameBoost.Core.Modules.GameMode;

/// <summary>
/// O Modo Game da v1 portado para IModule/ActionItem.
///
/// ScanAsync  -> lista o que sera encerrado, ja com a blindagem aplicada.
/// ApplyAsync -> liga o Modo Game com os itens confirmados pelo usuario.
/// RevertAsync-> desliga, devolve o sistema e reabre os apps essenciais.
/// </summary>
public sealed class GameModeModule : IModule
{
    public const string ModuloId = "gamemode";

    private static readonly TimeSpan EsperaDeFechamento = TimeSpan.FromSeconds(3);

    private readonly IProcessService _processes;
    private readonly IMemoryService _memory;
    private readonly IPowerService _power;
    private readonly ISafetyGuard _guard;
    private readonly IStateBackup _backup;
    private readonly IRollbackEngine _rollback;
    private readonly ISessionStore _sessions;
    private readonly SystemSilencer _silencer;
    private readonly GameDetector _detector;
    private readonly AppSettings _settings;
    private readonly IGameBoostLogger _log;
    private readonly IClock _clock;

    public GameModeModule(
        IProcessService processes,
        IMemoryService memory,
        IPowerService power,
        ISafetyGuard guard,
        IStateBackup backup,
        IRollbackEngine rollback,
        ISessionStore sessions,
        SystemSilencer silencer,
        GameDetector detector,
        AppSettings settings,
        IGameBoostLogger log,
        IClock clock)
    {
        _processes = processes;
        _memory = memory;
        _power = power;
        _guard = guard;
        _backup = backup;
        _rollback = rollback;
        _sessions = sessions;
        _silencer = silencer;
        _detector = detector;
        _settings = settings;
        _log = log;
        _clock = clock;
    }

    public string Id => ModuloId;
    public string Nome => "Modo Game";
    public string Descricao => "Encerra o que voce confirmar, libera RAM e prepara o sistema para o jogo.";

    public bool Ativo => _sessions.Load().Ativo;

    // ------------------------------------------------------------------
    // Varredura
    // ------------------------------------------------------------------

    public Task<ScanResult> ScanAsync(IProgress<ModuleProgress>? progress, CancellationToken ct)
        => Task.Run(() => Varrer(progress, ct), ct);

    private ScanResult Varrer(IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new ModuleProgress("Lendo processos", 10));
        var processos = _processes.GetProcesses();
        ct.ThrowIfCancellationRequested();

        progress?.Report(new ModuleProgress("Procurando o jogo", 35));
        var jogo = _detector.Detectar(processos);

        progress?.Report(new ModuleProgress("Aplicando a blindagem", 60));
        var itens = new List<ActionItem>();
        var avisos = new List<string>();

        // Agrupa por nome: o Chrome com 40 processos vira um unico item.
        var grupos = processos
            .Where(p => p.Pid > 4)
            .GroupBy(p => ProtectedProcesses.Normalizar(p.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var grupo in grupos)
        {
            ct.ThrowIfCancellationRequested();

            var representante = grupo.OrderByDescending(p => p.WorkingSetBytes).First();
            var veredito = _guard.CheckProcess(representante);

            // Protegidos por seguranca nem aparecem: poluiriam a lista com
            // dezenas de svchost que o usuario nunca deve tocar.
            if (veredito.Protegido && veredito.Motivo is
                ProtectionReason.ProcessoCritico or
                ProtectionReason.DentroDoWindows or
                ProtectionReason.OutraSessao or
                ProtectionReason.ProprioApp)
            {
                continue;
            }

            var ehOJogo = jogo is not null && grupo.Any(p => p.Pid == jogo.Process.Pid);
            var ramGrupo = grupo.Sum(p => p.WorkingSetBytes);

            if (_settings.SoOEssencial && !ehOJogo && !veredito.Protegido)
            {
                var limite = _settings.LimiteRamMegabytes * 1024L * 1024L;
                if (ramGrupo < limite)
                    continue;
            }

            itens.Add(MontarItem(grupo.Key, grupo.ToList(), representante, veredito, ehOJogo, ramGrupo));
        }

        progress?.Report(new ModuleProgress("Medindo a memoria", 85));
        var memoria = _memory.GetSnapshot();

        if (jogo is null)
            avisos.Add("Nenhum jogo detectado. A prioridade de CPU nao sera alterada.");

        var plano = _power.GetActivePlan();
        if (plano is not null && plano.Id == WindowsPowerService.Balanceado)
            avisos.Add("Plano de energia Balanceado ativo: o Modo Game vai trocar para Alto desempenho.");

        var marcados = itens.Count(i => i.PreMarcado);
        var ramMarcada = itens.Where(i => i.PreMarcado).Sum(i => i.GanhoBytes);

        progress?.Report(new ModuleProgress("Pronto", 100));

        return new ScanResult
        {
            ModuloId = ModuloId,
            Itens = itens.OrderByDescending(i => i.GanhoBytes).ToList(),
            Momento = _clock.Now,
            Avisos = avisos,
            Resumo = jogo is not null
                ? $"{marcados} apps marcados, ate {Formatar(ramMarcada)} de RAM. Jogo detectado: {jogo.Process.Name}. RAM livre agora: {Formatar(memoria.AvailableBytes)}."
                : $"{marcados} apps marcados, ate {Formatar(ramMarcada)} de RAM. RAM livre agora: {Formatar(memoria.AvailableBytes)}."
        };
    }

    private ActionItem MontarItem(
        string nome,
        IReadOnlyList<ProcessInfo> grupo,
        ProcessInfo representante,
        ProtectionVerdict veredito,
        bool ehOJogo,
        long ramGrupo)
    {
        var eEssencial = _settings.Essenciais.Any(e =>
            ProtectedProcesses.Normalizar(e.Nome).Equals(nome, StringComparison.OrdinalIgnoreCase));

        var preMarcado = !ehOJogo && !veredito.Protegido && _guard.PodePreMarcar(representante);

        var descricao = grupo.Count > 1
            ? $"{grupo.Count} processos, {Formatar(ramGrupo)} de RAM."
            : $"{Formatar(ramGrupo)} de RAM.";

        if (!string.IsNullOrWhiteSpace(representante.WindowTitle))
            descricao += $" Janela aberta: {representante.WindowTitle}";

        if (ehOJogo)
            descricao = "Este parece ser o jogo. " + descricao;

        var risco = ehOJogo || veredito.Protegido
            ? RiskLevel.Alto
            : ProtectedProcesses.NuncaPreMarcados.Contains(nome)
                ? RiskLevel.Medio
                : RiskLevel.Baixo;

        return new ActionItem
        {
            Id = $"proc:{nome}",
            Categoria = ehOJogo ? "Jogo" : veredito.Protegido ? "Protegido" : "Aplicativos",
            Titulo = representante.Name,
            Descricao = descricao,
            Risco = risco,
            GanhoBytes = ramGrupo,
            GanhoEstimado = $"ate {Formatar(ramGrupo)} de RAM",
            PreMarcado = preMarcado,
            Bloqueado = veredito.Protegido || ehOJogo,
            MotivoBloqueio = ehOJogo
                ? "O GameBoost nunca encerra o jogo que voce vai jogar."
                : veredito.Protegido ? veredito.Explicacao : null,
            ComoDesfazer = eEssencial
                ? "Marcado como essencial: reabre sozinho ao desligar o Modo Game."
                : "Aparece na lista de restauracao ao desligar o Modo Game, com botao Restaurar.",
            Payload = grupo
        };
    }

    // ------------------------------------------------------------------
    // Ativacao
    // ------------------------------------------------------------------

    public Task<ApplyResult> ApplyAsync(IReadOnlyList<string> itemIds, bool dryRun, CancellationToken ct)
        => Task.Run(() => Ativar(itemIds, dryRun, ct), ct);

    private ApplyResult Ativar(IReadOnlyList<string> itemIds, bool dryRun, CancellationToken ct)
    {
        var acoes = new List<AppliedAction>();
        var antes = _memory.GetSnapshot();

        var sessao = new GameSession
        {
            Ativo = !dryRun,
            AtivadoEm = _clock.Now,
            RamLivreAntes = antes.AvailableBytes,
            RamTotal = antes.TotalBytes
        };

        var processos = _processes.GetProcesses();
        var jogo = _detector.Detectar(processos);

        if (jogo is not null)
        {
            sessao.JogoDetectado = jogo.Process.Name;
            sessao.PidDoJogo = jogo.Process.Pid;
        }

        // 1. Encerrar os apps confirmados.
        var nomesSelecionados = itemIds
            .Where(id => id.StartsWith("proc:", StringComparison.Ordinal))
            .Select(id => id[5..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var grupo in processos.GroupBy(p => ProtectedProcesses.Normalizar(p.Name), StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            if (!nomesSelecionados.Contains(grupo.Key))
                continue;

            // Segunda checagem antes de matar: a lista pode ter vindo de um scan
            // antigo, ou o usuario pode ter editado o JSON na mao.
            var representante = grupo.OrderByDescending(p => p.WorkingSetBytes).First();
            var veredito = _guard.CheckProcess(representante);
            if (veredito.Protegido)
            {
                acoes.Add(new AppliedAction($"proc:{grupo.Key}", false, $"Bloqueado: {veredito.Explicacao}", null));
                continue;
            }

            if (jogo is not null && grupo.Any(p => p.Pid == jogo.Process.Pid))
            {
                acoes.Add(new AppliedAction($"proc:{grupo.Key}", false, "Bloqueado: e o jogo detectado.", null));
                continue;
            }

            acoes.Add(EncerrarGrupo(grupo.Key, grupo.ToList(), sessao, dryRun));
        }

        // 2. Plano de energia.
        if (_settings.AplicarPlanoEnergia)
            acoes.Add(AplicarPlanoDeEnergia(dryRun));

        // 3. Silenciar o sistema.
        if (_settings.SilenciarNotificacoes || _settings.DesativarGameDvr)
        {
            foreach (var tweak in SystemSilencer.Tweaks)
            {
                ct.ThrowIfCancellationRequested();
                var changeId = _silencer.Aplicar(tweak, dryRun);
                acoes.Add(new AppliedAction($"tweak:{tweak.ValueName}", true,
                    changeId is null ? $"{tweak.Nome}: ja estava aplicado." : $"{tweak.Nome}: aplicado.", changeId));
            }
        }

        if (_settings.PausarWindowsUpdate)
        {
            var changeId = _silencer.PausarWindowsUpdate(dryRun);
            acoes.Add(new AppliedAction("service:wuauserv", true,
                changeId is null
                    ? "Windows Update ja estava parado."
                    : "Windows Update pausado ate desligar o Modo Game.", changeId));
        }

        // 4. Prioridade do jogo.
        if (_settings.SubirPrioridadeDoJogo && jogo is not null)
            acoes.Add(SubirPrioridade(jogo, dryRun));

        // 5. Limpeza de RAM: por ultimo, para medir depois dos apps ja fechados.
        if (_settings.LimparRam)
            acoes.Add(LimparRam(dryRun, ct));

        if (_settings.PurgarStandbyList && !dryRun)
        {
            var ok = _memory.PurgeStandbyList();
            acoes.Add(new AppliedAction("mem:standby", ok,
                ok ? "Standby List purgada." : "Standby List nao pode ser purgada (precisa de administrador).", null));
        }

        var depois = _memory.GetSnapshot();
        sessao.RamLivreDepois = depois.AvailableBytes;

        if (!dryRun)
            _sessions.Save(sessao);

        var ganho = depois.AvailableBytes - antes.AvailableBytes;
        var encerrados = sessao.AppsEncerrados.Count;

        _log.Info(ModuloId, dryRun ? "Ativar (dry-run)" : "Ativar", null,
            $"{encerrados} apps encerrados, RAM livre {Formatar(antes.AvailableBytes)} -> {Formatar(depois.AvailableBytes)}");

        return new ApplyResult
        {
            ModuloId = ModuloId,
            Acoes = acoes,
            DryRun = dryRun,
            Resumo = dryRun
                ? "Simulacao concluida. Nada foi alterado."
                : $"Modo Game ativo. {encerrados} apps encerrados.",
            GanhoMedido = dryRun
                ? "Dry-run nao mede ganho."
                : ganho > 0
                    ? $"RAM livre: {Formatar(antes.AvailableBytes)} -> {Formatar(depois.AvailableBytes)} ({Formatar(ganho)} a mais)"
                    : $"RAM livre: {Formatar(depois.AvailableBytes)}. Sem ganho mensuravel de memoria."
        };
    }

    private AppliedAction EncerrarGrupo(string nome, IReadOnlyList<ProcessInfo> grupo, GameSession sessao, bool dryRun)
    {
        var itemId = $"proc:{nome}";

        if (dryRun)
            return new AppliedAction(itemId, true, $"Encerraria {grupo.Count} processo(s) de {nome}.", null);

        var essencial = _settings.Essenciais.FirstOrDefault(e =>
            ProtectedProcesses.Normalizar(e.Nome).Equals(nome, StringComparison.OrdinalIgnoreCase));

        var principal = grupo
            .OrderByDescending(p => !string.IsNullOrWhiteSpace(p.WindowTitle))
            .ThenByDescending(p => p.WorkingSetBytes)
            .First();

        var fechados = 0;
        var forcados = 0;

        foreach (var p in grupo)
        {
            // Fechamento gracioso primeiro: o app pede para salvar o que estiver aberto.
            if (_processes.TryCloseGracefully(p.Pid, EsperaDeFechamento))
            {
                fechados++;
                continue;
            }

            if (_processes.Kill(p.Pid))
            {
                fechados++;
                forcados++;
            }
        }

        if (fechados == 0)
            return new AppliedAction(itemId, false, $"Nao foi possivel encerrar {nome}.", null);

        sessao.AppsEncerrados.Add(new ClosedApp
        {
            Nome = principal.Name,
            ExecutablePath = principal.ExecutablePath ?? essencial?.ExecutablePath,
            Argumentos = ExtrairArgumentos(principal) ?? essencial?.Argumentos,
            WorkingDirectory = principal.ExecutablePath is null ? null : Path.GetDirectoryName(principal.ExecutablePath),
            Essencial = essencial is not null
        });

        var detalhe = forcados > 0
            ? $"{fechados} processo(s) encerrados ({forcados} a forca apos {EsperaDeFechamento.TotalSeconds:0} s)."
            : $"{fechados} processo(s) encerrados normalmente.";

        return new AppliedAction(itemId, true, detalhe, null);
    }

    /// <summary>A linha de comando do WMI vem com o exe na frente: remove para sobrar so os argumentos.</summary>
    private static string? ExtrairArgumentos(ProcessInfo p)
    {
        if (string.IsNullOrWhiteSpace(p.CommandLine))
            return null;

        var linha = p.CommandLine.Trim();

        if (linha.StartsWith('"'))
        {
            var fim = linha.IndexOf('"', 1);
            if (fim > 0 && fim + 1 < linha.Length)
                return linha[(fim + 1)..].Trim();
            return null;
        }

        var espaco = linha.IndexOf(' ');
        return espaco > 0 ? linha[(espaco + 1)..].Trim() : null;
    }

    private AppliedAction AplicarPlanoDeEnergia(bool dryRun)
    {
        var atual = _power.GetActivePlan();
        if (atual is null)
            return new AppliedAction("power:plan", false, "Nao foi possivel ler o plano de energia atual.", null);

        var planos = _power.GetPlans();
        var alvo = planos.FirstOrDefault(p => p.Id == WindowsPowerService.DesempenhoMaximo)
                   ?? planos.FirstOrDefault(p => p.Id == WindowsPowerService.AltoDesempenho);

        if (alvo is null)
            return new AppliedAction("power:plan", false,
                "Nenhum plano de Alto desempenho disponivel nesta maquina.", null);

        if (atual.Id == alvo.Id)
            return new AppliedAction("power:plan", true, $"Plano {alvo.Name} ja estava ativo.", null);

        if (dryRun)
            return new AppliedAction("power:plan", true, $"Trocaria {atual.Name} por {alvo.Name}.", null);

        var record = _backup.Registrar(new ChangeRecord
        {
            Modulo = ModuloId,
            Tipo = ChangeType.Power,
            Alvo = "plano-de-energia",
            ValorAnterior = atual.Id.ToString(),
            ValorNovo = alvo.Id.ToString(),
            Extras = { ["nome"] = $"Plano de energia ({atual.Name})" }
        });

        var ok = _power.SetActivePlan(alvo.Id);
        return new AppliedAction("power:plan", ok,
            ok ? $"Plano de energia: {atual.Name} -> {alvo.Name}." : "Nao foi possivel trocar o plano de energia.",
            ok ? record.Id : null);
    }

    private AppliedAction SubirPrioridade(DetectedGame jogo, bool dryRun)
    {
        var anterior = _processes.GetPriority(jogo.Process.Pid);
        if (anterior is null)
            return new AppliedAction("priority:game", false, "Nao foi possivel ler a prioridade do jogo.", null);

        if (anterior == ProcessPriority.High)
            return new AppliedAction("priority:game", true, $"{jogo.Process.Name} ja estava em prioridade Alta.", null);

        if (dryRun)
            return new AppliedAction("priority:game", true,
                $"Subiria {jogo.Process.Name} de {anterior} para Alta.", null);

        // Prioridade morre com o processo: nao precisa de ChangeRecord, mas fica no log.
        var ok = _processes.SetPriority(jogo.Process.Pid, ProcessPriority.High);
        _log.Info(ModuloId, "Prioridade", jogo.Process.Name,
            ok ? $"{anterior} -> High ({jogo.Motivo})" : "falhou");

        return new AppliedAction("priority:game", ok,
            ok ? $"{jogo.Process.Name}: prioridade Alta (nunca Tempo real)." : "Nao foi possivel subir a prioridade.",
            null);
    }

    private AppliedAction LimparRam(bool dryRun, CancellationToken ct)
    {
        if (dryRun)
            return new AppliedAction("mem:workingset", true, "Faria EmptyWorkingSet em todos os processos.", null);

        long total = 0;
        var tocados = 0;

        foreach (var p in _processes.GetProcesses())
        {
            ct.ThrowIfCancellationRequested();

            var liberado = _processes.TrimWorkingSet(p.Pid);
            if (liberado > 0)
            {
                total += liberado;
                tocados++;
            }
        }

        return new AppliedAction("mem:workingset", true,
            $"{Formatar(total)} retirados do working set de {tocados} processos.", null);
    }

    // ------------------------------------------------------------------
    // Desligamento
    // ------------------------------------------------------------------

    public Task<ApplyResult> RevertAsync(IReadOnlyList<string> changeIds, bool dryRun, CancellationToken ct)
        => Task.Run(() => Desligar(changeIds, dryRun), ct);

    private ApplyResult Desligar(IReadOnlyList<string> changeIds, bool dryRun)
    {
        var acoes = new List<AppliedAction>();

        // 1. Desfazer as alteracoes de sistema, da mais recente para a mais antiga.
        var doModulo = _backup.Pendentes
            .Where(r => r.Modulo == ModuloId)
            .Where(r => changeIds.Count == 0 || changeIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToList();

        foreach (var resultado in _rollback.Reverter(doModulo, dryRun))
            acoes.Add(new AppliedAction(resultado.ChangeId, resultado.Sucesso, resultado.Detalhe, resultado.ChangeId));

        // 2. Reabrir os apps marcados com estrela.
        var sessao = _sessions.Load();
        var reabertos = 0;

        foreach (var app in sessao.AppsEncerrados.Where(a => a.Essencial && !a.Reaberto))
        {
            if (string.IsNullOrWhiteSpace(app.ExecutablePath))
            {
                acoes.Add(new AppliedAction($"reopen:{app.Nome}", false,
                    $"{app.Nome}: caminho do executavel desconhecido, reabra manualmente.", null));
                continue;
            }

            if (dryRun)
            {
                acoes.Add(new AppliedAction($"reopen:{app.Nome}", true, $"Reabriria {app.Nome}.", null));
                continue;
            }

            var ok = _processes.Start(app.ExecutablePath, app.Argumentos, app.WorkingDirectory);
            app.Reaberto = ok;
            if (ok)
                reabertos++;

            acoes.Add(new AppliedAction($"reopen:{app.Nome}", ok,
                ok ? $"{app.Nome} reaberto." : $"Nao foi possivel reabrir {app.Nome}.", null));
        }

        var naoEssenciais = sessao.AppsEncerrados.Count(a => !a.Essencial);

        if (!dryRun)
        {
            sessao.Ativo = false;
            _sessions.Save(sessao);
        }

        _log.Info(ModuloId, dryRun ? "Desligar (dry-run)" : "Desligar", null,
            $"{doModulo.Count} alteracoes revertidas, {reabertos} apps reabertos");

        return new ApplyResult
        {
            ModuloId = ModuloId,
            Acoes = acoes,
            DryRun = dryRun,
            Resumo = dryRun
                ? "Simulacao de desligamento. Nada foi alterado."
                : $"Modo Game desligado. {reabertos} apps reabertos, {naoEssenciais} aguardando na lista de restauracao.",
            GanhoMedido = sessao.RamLivreAntes > 0
                ? $"Durante a sessao a RAM livre foi de {Formatar(sessao.RamLivreAntes)} para {Formatar(sessao.RamLivreDepois)}."
                : string.Empty
        };
    }

    /// <summary>Reabre um app da lista de restauracao, um por vez, pelo botao da UI.</summary>
    public bool RestaurarApp(string nome)
    {
        var sessao = _sessions.Load();
        var app = sessao.AppsEncerrados.FirstOrDefault(a =>
            a.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase) && !a.Reaberto);

        if (app?.ExecutablePath is null)
            return false;

        var ok = _processes.Start(app.ExecutablePath, app.Argumentos, app.WorkingDirectory);
        if (ok)
        {
            app.Reaberto = true;
            _sessions.Save(sessao);
        }

        _log.Info(ModuloId, "RestaurarApp", nome, ok ? "reaberto" : "falhou");
        return ok;
    }

    public static string Formatar(long bytes)
    {
        var negativo = bytes < 0;
        var valor = Math.Abs((double)bytes);

        string texto;
        if (valor >= 1024L * 1024 * 1024)
            texto = $"{valor / (1024.0 * 1024 * 1024):0.0} GB";
        else if (valor >= 1024 * 1024)
            texto = $"{valor / (1024.0 * 1024):0} MB";
        else if (valor >= 1024)
            texto = $"{valor / 1024.0:0} KB";
        else
            texto = $"{valor:0} B";

        return negativo ? "-" + texto : texto;
    }
}
