using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Modules.GameMode;
using GameBoost.Core.Native;
using GameBoost.Core.Safety;
using GameBoost.Core.Settings;

namespace GameBoost.Core.Modules.Cleaner;

/// <summary>O que uma varredura encontrou num alvo.</summary>
public sealed record AchadoDeLimpeza(
    CleanupTarget Alvo,
    IReadOnlyList<string> Arquivos,
    long Bytes,
    int Ignorados,
    string? Bloqueio);

/// <summary>
/// Limpeza de temporários e caches (seção 5.2).
///
/// Duas camadas de proteção, sempre: o caminho precisa vir do
/// <see cref="CleanupCatalog"/> e, mesmo assim, cada arquivo passa pelo
/// SafetyGuard com a whitelist antes de ser removido. O módulo nunca deriva
/// caminho por conta própria.
/// </summary>
public sealed class CleanerModule : IModule
{
    public const string ModuloId = "cleaner";

    private readonly IFileSystem _fs;
    private readonly IProcessService _processos;
    private readonly IServiceControllerService _servicos;
    private readonly ISafetyGuard _guard;
    private readonly CleanupHistory _historico;
    private readonly IGameBoostLogger _log;
    private readonly IClock _relogio;

    private readonly Dictionary<string, AchadoDeLimpeza> _ultimaVarredura = new(StringComparer.OrdinalIgnoreCase);

    public CleanerModule(
        IFileSystem fs,
        IProcessService processos,
        IServiceControllerService servicos,
        ISafetyGuard guard,
        CleanupHistory historico,
        IGameBoostLogger log,
        IClock relogio)
    {
        _fs = fs;
        _processos = processos;
        _servicos = servicos;
        _guard = guard;
        _historico = historico;
        _log = log;
        _relogio = relogio;
    }

    public string Id => ModuloId;
    public string Nome => "Limpeza";
    public string Descricao => "Libera espaço mostrando exatamente o que sai e quanto ocupa.";

    // ------------------------------------------------------------------
    // Varredura
    // ------------------------------------------------------------------

    public Task<ScanResult> ScanAsync(IProgress<ModuleProgress>? progress, CancellationToken ct)
        => Task.Run(() => Varrer(progress, ct), ct);

    private ScanResult Varrer(IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var alvos = CleanupCatalog.Todos();

        // Varredura pode LER onde a remocao nao pode apagar: e o que permite
        // mostrar uma sugestao sem ganhar permissao de remover.
        var whitelist = CleanupCatalog.RaizesParaMedir();
        var abertos = ProcessosAbertos();

        var itens = new List<ActionItem>();
        var avisos = new List<string>();

        _ultimaVarredura.Clear();

        for (var i = 0; i < alvos.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var alvo = alvos[i];
            progress?.Report(new ModuleProgress(
                $"Medindo {alvo.Titulo}", (int)(100.0 * i / alvos.Count)));

            var achado = Medir(alvo, whitelist, abertos, ct);
            if (achado is null)
                continue;

            _ultimaVarredura[alvo.Id] = achado;
            itens.Add(MontarItem(achado));
        }

        var total = itens.Where(i => i.PreMarcado).Sum(i => i.GanhoBytes);
        var totalGeral = _ultimaVarredura.Values
            .Where(a => !a.Alvo.ApenasSugestao)
            .Sum(a => a.Bytes);

        if (itens.Count == 0)
            avisos.Add("Nada encontrado para limpar. A máquina já está em dia.");

        progress?.Report(new ModuleProgress("Pronto", 100));

        return new ScanResult
        {
            ModuloId = ModuloId,
            Itens = itens.OrderByDescending(i => i.GanhoBytes).ToList(),
            Momento = _relogio.Now,
            Avisos = avisos,
            Resumo = itens.Count == 0
                ? "Nada para limpar."
                : $"Até {GameModeModule.Formatar(total)} liberáveis com o que está marcado, "
                + $"{GameModeModule.Formatar(totalGeral)} no total encontrado."
        };
    }

    private AchadoDeLimpeza? Medir(
        CleanupTarget alvo,
        IReadOnlyList<string> whitelist,
        IReadOnlySet<string> abertos,
        CancellationToken ct)
    {
        var raiz = alvo.Raiz();

        if (string.IsNullOrWhiteSpace(raiz) || !_fs.DirectoryExists(raiz))
            return null;

        // Primeira camada: o proprio caminho do catalogo precisa passar.
        var veredito = _guard.CheckPath(raiz, whitelist);
        if (veredito.Protegido)
        {
            _log.Warn(ModuloId, "Medir", raiz, $"alvo recusado pela blindagem: {veredito.Explicacao}");
            return null;
        }

        var bloqueio = alvo.ExigeFechado
            .Where(p => abertos.Contains(p))
            .Select(p => $"Feche o {p} antes: o cache está em uso.")
            .FirstOrDefault();

        var arquivos = new List<string>();
        long bytes = 0;
        var ignorados = 0;
        var corte = _relogio.Now - alvo.IdadeMinima;

        try
        {
            var opcao = alvo.Recursivo ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var arquivo in _fs.EnumerateFiles(raiz, alvo.Padrao, opcao))
            {
                ct.ThrowIfCancellationRequested();

                // Segunda camada: cada arquivo, um por um.
                if (_guard.CheckPath(arquivo, whitelist).Protegido)
                {
                    ignorados++;
                    continue;
                }

                try
                {
                    if (alvo.IdadeMinima > TimeSpan.Zero)
                    {
                        var escrito = File.GetLastWriteTimeUtc(arquivo);
                        if (escrito > corte.UtcDateTime)
                        {
                            ignorados++;
                            continue;
                        }
                    }

                    bytes += _fs.GetFileSize(arquivo);
                    arquivos.Add(arquivo);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ignorados++;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            _log.Warn(ModuloId, "Medir", raiz, ex.Message);
            return null;
        }

        if (arquivos.Count == 0)
            return null;

        return new AchadoDeLimpeza(alvo, arquivos, bytes, ignorados, bloqueio);
    }

    private ActionItem MontarItem(AchadoDeLimpeza achado)
    {
        var alvo = achado.Alvo;

        var descricao = $"{achado.Arquivos.Count} arquivos, {GameModeModule.Formatar(achado.Bytes)}.";

        if (achado.Ignorados > 0)
            descricao += $" {achado.Ignorados} ignorados (em uso, recentes ou protegidos).";

        if (alvo.Advertencia is not null)
            descricao += $" {alvo.Advertencia}";

        var bloqueado = achado.Bloqueio is not null || alvo.ApenasSugestao;

        var comoDesfazer = alvo.Modo == RemocaoModo.Lixeira
            ? alvo.ComoDesfazer + " Os arquivos vão para a Lixeira, então dá para recuperar."
            : alvo.ComoDesfazer;

        return new ActionItem
        {
            Id = $"clean:{alvo.Id}",
            Categoria = alvo.Categoria,
            Titulo = alvo.Titulo,
            Descricao = descricao,
            Risco = alvo.Risco,
            GanhoBytes = achado.Bytes,
            GanhoEstimado = GameModeModule.Formatar(achado.Bytes),
            PreMarcado = alvo.PreMarcadoPadrao && !bloqueado,
            Bloqueado = bloqueado,
            MotivoBloqueio = achado.Bloqueio
                ?? (alvo.ApenasSugestao ? alvo.Advertencia ?? "Apenas informativo." : null),
            ComoDesfazer = comoDesfazer,
            Payload = achado
        };
    }

    private IReadOnlySet<string> ProcessosAbertos()
    {
        try
        {
            return _processos.GetProcesses()
                .Select(p => ProtectedProcesses.Normalizar(p.Name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------------------
    // Limpeza
    // ------------------------------------------------------------------

    public Task<ApplyResult> ApplyAsync(IReadOnlyList<string> itemIds, bool dryRun, CancellationToken ct)
        => Task.Run(() => Limpar(itemIds, dryRun, ct), ct);

    private ApplyResult Limpar(IReadOnlyList<string> itemIds, bool dryRun, CancellationToken ct)
    {
        var acoes = new List<AppliedAction>();

        // Na remocao vale so a whitelist estrita: Downloads fica de fora.
        var whitelist = CleanupCatalog.RaizesPermitidas();

        long liberadoTotal = 0;
        var removidosTotal = 0;
        var emUsoTotal = 0;
        var categorias = new List<string>();

        foreach (var itemId in itemIds)
        {
            ct.ThrowIfCancellationRequested();

            var id = itemId.StartsWith("clean:", StringComparison.Ordinal) ? itemId[6..] : itemId;

            if (!_ultimaVarredura.TryGetValue(id, out var achado))
            {
                acoes.Add(new AppliedAction(itemId, false, "Item não encontrado: varra de novo.", null));
                continue;
            }

            if (achado.Alvo.ApenasSugestao)
            {
                acoes.Add(new AppliedAction(itemId, false,
                    $"{achado.Alvo.Titulo} é apenas informativo: o GameBoost não remove nada daí.", null));
                continue;
            }

            if (achado.Bloqueio is not null)
            {
                acoes.Add(new AppliedAction(itemId, false, achado.Bloqueio, null));
                continue;
            }

            var resultado = LimparAlvo(achado, whitelist, dryRun, ct);
            acoes.Add(resultado.Acao);

            liberadoTotal += resultado.Bytes;
            removidosTotal += resultado.Removidos;
            emUsoTotal += resultado.EmUso;

            if (resultado.Removidos > 0)
                categorias.Add(achado.Alvo.Titulo);
        }

        if (!dryRun && removidosTotal > 0)
        {
            _historico.Registrar(new CleanupEntry
            {
                Data = _relogio.Now,
                Categorias = categorias,
                BytesLiberados = liberadoTotal,
                ArquivosRemovidos = removidosTotal
            });
        }

        _log.Info(ModuloId, dryRun ? "Limpar (dry-run)" : "Limpar", null,
            $"{removidosTotal} arquivos, {GameModeModule.Formatar(liberadoTotal)}");

        return new ApplyResult
        {
            ModuloId = ModuloId,
            Acoes = acoes,
            DryRun = dryRun,
            Resumo = dryRun
                ? $"Simulação: {removidosTotal} arquivos seriam removidos. Nada foi alterado."
                : $"{removidosTotal} arquivos removidos.",
            GanhoMedido = dryRun
                ? $"Liberaria até {GameModeModule.Formatar(liberadoTotal)}."
                : emUsoTotal > 0
                    ? $"{GameModeModule.Formatar(liberadoTotal)} liberados. "
                    + $"{emUsoTotal} arquivos não puderam ser removidos porque estão em uso."
                    : $"{GameModeModule.Formatar(liberadoTotal)} liberados."
        };
    }

    private (AppliedAction Acao, long Bytes, int Removidos, int EmUso) LimparAlvo(
        AchadoDeLimpeza achado,
        IReadOnlyList<string> whitelist,
        bool dryRun,
        CancellationToken ct)
    {
        var alvo = achado.Alvo;
        var itemId = $"clean:{alvo.Id}";

        if (dryRun)
        {
            return (new AppliedAction(itemId, true,
                $"{alvo.Titulo}: removeria {achado.Arquivos.Count} arquivos "
                + $"({GameModeModule.Formatar(achado.Bytes)}).", null), achado.Bytes, achado.Arquivos.Count, 0);
        }

        var servicoParado = false;
        if (alvo.PararServico is not null)
            servicoParado = PararServico(alvo.PararServico);

        try
        {
            return alvo.Modo == RemocaoModo.Lixeira
                ? RemoverParaLixeira(achado, whitelist, itemId)
                : ApagarDireto(achado, whitelist, itemId, ct);
        }
        finally
        {
            // O servico volta aconteca o que acontecer: deixar o Windows Update
            // parado seria pior que nao ter limpado nada.
            if (servicoParado && alvo.PararServico is not null)
                ReligarServico(alvo.PararServico);
        }
    }

    private (AppliedAction, long, int, int) ApagarDireto(
        AchadoDeLimpeza achado,
        IReadOnlyList<string> whitelist,
        string itemId,
        CancellationToken ct)
    {
        long bytes = 0;
        var removidos = 0;
        var emUso = 0;

        foreach (var arquivo in achado.Arquivos)
        {
            ct.ThrowIfCancellationRequested();

            // Terceira checagem, agora na hora de apagar.
            if (_guard.CheckPath(arquivo, whitelist).Protegido)
                continue;

            try
            {
                var tamanho = _fs.GetFileSize(arquivo);
                _fs.DeleteFile(arquivo);
                bytes += tamanho;
                removidos++;
            }
            catch (IOException)
            {
                // Arquivo em uso. Normal, e conta separado (seção 5.2).
                emUso++;
            }
            catch (UnauthorizedAccessException)
            {
                emUso++;
            }
        }

        var detalhe = $"{achado.Alvo.Titulo}: {removidos} arquivos, {GameModeModule.Formatar(bytes)}."
                    + (emUso > 0 ? $" {emUso} em uso, mantidos." : string.Empty);

        return (new AppliedAction(itemId, true, detalhe, null), bytes, removidos, emUso);
    }

    private (AppliedAction, long, int, int) RemoverParaLixeira(
        AchadoDeLimpeza achado,
        IReadOnlyList<string> whitelist,
        string itemId)
    {
        var permitidos = achado.Arquivos
            .Where(a => !_guard.CheckPath(a, whitelist).Protegido)
            .ToList();

        if (permitidos.Count == 0)
            return (new AppliedAction(itemId, false, "Nada permitido pela blindagem.", null), 0, 0, 0);

        long bytes = 0;
        foreach (var arquivo in permitidos)
        {
            try
            {
                bytes += _fs.GetFileSize(arquivo);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        var ok = RecycleBinBridge.ParaLixeira(permitidos);

        return ok
            ? (new AppliedAction(itemId, true,
                $"{achado.Alvo.Titulo}: {permitidos.Count} arquivos para a Lixeira "
                + $"({GameModeModule.Formatar(bytes)}).", null), bytes, permitidos.Count, 0)
            : (new AppliedAction(itemId, false,
                $"{achado.Alvo.Titulo}: o Windows recusou mover para a Lixeira.", null), 0, 0, 0);
    }

    private bool PararServico(string nome)
    {
        if (_guard.CheckService(nome, apenasPausar: true).Protegido)
            return false;

        var atual = _servicos.GetService(nome);
        if (atual is null || !atual.IsRunning)
            return false;

        var ok = _servicos.Stop(nome, TimeSpan.FromSeconds(20));
        _log.Info(ModuloId, "PararServico", nome, ok ? "parado para a limpeza" : "não parou");
        return ok;
    }

    private void ReligarServico(string nome)
    {
        var ok = _servicos.Start(nome, TimeSpan.FromSeconds(30));
        _log.Log(ok ? LogLevel.Info : LogLevel.Warn, ModuloId, "ReligarServico", nome,
            ok ? "religado" : "NÃO religou: verifique o serviço");
    }

    // ------------------------------------------------------------------
    // Reversão
    // ------------------------------------------------------------------

    /// <summary>
    /// Limpeza não se desfaz: o que foi para a Lixeira o usuário restaura pelo
    /// Explorer, e temporário apagado não volta. Dizer isso é mais honesto que
    /// oferecer um botão que não funciona (regra 4).
    /// </summary>
    public Task<ApplyResult> RevertAsync(IReadOnlyList<string> changeIds, bool dryRun, CancellationToken ct)
        => Task.FromResult(new ApplyResult
        {
            ModuloId = ModuloId,
            DryRun = dryRun,
            Resumo = "A limpeza não tem reversão automática.",
            GanhoMedido = "Arquivos enviados para a Lixeira podem ser restaurados por ela. "
                        + "Temporários apagados são recriados pelos próprios programas."
        });
}
