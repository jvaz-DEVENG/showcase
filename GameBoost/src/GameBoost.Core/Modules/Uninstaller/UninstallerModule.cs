using System.Diagnostics;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Modules.GameMode;
using GameBoost.Core.Native;

namespace GameBoost.Core.Modules.Uninstaller;

/// <summary>
/// Desinstalador (seção 5.3).
///
/// Cada app é desinstalado pelo seu próprio desinstalador — o GameBoost nunca
/// apaga arquivos de programa por conta própria. O que ele faz depois é
/// procurar o que ficou para trás e oferecer a remoção disso, sempre para a
/// Lixeira e sempre desmarcado.
/// </summary>
public sealed class UninstallerModule : IModule
{
    public const string ModuloId = "uninstaller";

    /// <summary>Um app que não sai em 10 minutos não vai sair (seção 5.3).</summary>
    private static readonly TimeSpan Limite = TimeSpan.FromMinutes(10);

    private readonly AppInventory _inventario;
    private readonly LeftoverScanner _restos;
    private readonly WingetService _winget;
    private readonly IProcessService _processos;
    private readonly IGameBoostLogger _log;
    private readonly IClock _relogio;

    private IReadOnlyList<InstalledApp> _ultimaLista = Array.Empty<InstalledApp>();

    public UninstallerModule(
        AppInventory inventario,
        LeftoverScanner restos,
        WingetService winget,
        IProcessService processos,
        IGameBoostLogger log,
        IClock relogio)
    {
        _inventario = inventario;
        _restos = restos;
        _winget = winget;
        _processos = processos;
        _log = log;
        _relogio = relogio;
    }

    public string Id => ModuloId;
    public string Nome => "Aplicativos";
    public string Descricao => "Lista tudo que está instalado e desinstala em lote, com os restos.";

    public IReadOnlyList<InstalledApp> UltimaLista => _ultimaLista;

    // ------------------------------------------------------------------

    public Task<ScanResult> ScanAsync(IProgress<ModuleProgress>? progress, CancellationToken ct)
        => Task.Run(() => Varrer(progress, ct), ct);

    private ScanResult Varrer(IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new ModuleProgress("Lendo o registro e a Store", 10));

        var apps = _inventario.Listar(medirTamanho: false, ct);
        _ultimaLista = apps;

        progress?.Report(new ModuleProgress("Medindo o tamanho de cada app", 40));

        // Medir pasta a pasta é o passo caro; só vale para os que têm pasta
        // propria e confiavel, e em paralelo.
        var comPasta = apps
            .Where(a => a.PastaDeInstalacao is not null && PastaConfiavel(a.PastaDeInstalacao!))
            .ToList();

        Parallel.ForEach(
            comPasta,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            app =>
            {
                if (app.TamanhoMedido == 0 && Directory.Exists(app.PastaDeInstalacao!))
                    app.TamanhoMedido = Medir(app.PastaDeInstalacao!, ct);
            });

        progress?.Report(new ModuleProgress("Montando a lista", 90));

        // Reordenar aqui, e não no inventário: lá o tamanho ainda é só o que o
        // registro declara. O ARK ocupa 309 GB de verdade e aparecia atrás do
        // AutoCAD, que declara 4 GB — justo o contrário do que a tela serve para
        // mostrar.
        var itens = apps
            .OrderByDescending(a => a.Tamanho)
            .ThenBy(a => a.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Select(MontarItem)
            .ToList();

        // Somar o tamanho de todos daria numero maior que o disco: varios
        // apps declaram a MESMA pasta (suites, componentes de um mesmo
        // produto) e seriam contados uma vez cada. Conta pasta unica.
        var total = apps
            .Where(a => a.PastaDeInstalacao is not null)
            .GroupBy(a => a.PastaDeInstalacao!, StringComparer.OrdinalIgnoreCase)
            .Sum(g => g.Max(a => a.Tamanho))
            + apps.Where(a => a.PastaDeInstalacao is null).Sum(a => a.Tamanho);

        var naoUsados = apps.Count(a => a.NuncaUsado && !a.Protegido);

        progress?.Report(new ModuleProgress("Pronto", 100));

        return new ScanResult
        {
            ModuloId = ModuloId,
            Itens = itens,
            Momento = _relogio.Now,
            Resumo = $"{apps.Count} aplicativos, cerca de {GameModeModule.Formatar(total)} no total. "
                   + $"{naoUsados} sem registro de uso no último ano.",
            Avisos = apps.Any(a => a.Sugerido)
                ? new[] { $"{apps.Count(a => a.Sugerido)} apps parecem bloatware. Aparecem marcados como sugestão, nunca selecionados." }
                : Array.Empty<string>()
        };
    }

    private ActionItem MontarItem(InstalledApp app)
    {
        var detalhes = new List<string>();

        if (app.Versao is not null)
            detalhes.Add($"v{app.Versao}");

        if (app.Publisher is not null)
            detalhes.Add(app.Publisher);

        detalhes.Add(app.Origem == AppOrigem.Store ? "Microsoft Store" : "Win32");
        detalhes.Add(app.QuandoUsou);

        if (!app.TemDesinstalacaoSilenciosa && !app.Protegido)
            detalhes.Add("abre o desinstalador próprio");

        var risco = app.Protegido ? RiskLevel.Alto
            : app.Sugerido ? RiskLevel.Baixo
            : RiskLevel.Medio;

        return new ActionItem
        {
            Id = $"app:{app.Id}",
            Categoria = app.Protegido ? "Protegidos"
                : app.Sugerido ? "Sugestões de bloatware"
                : app.Origem == AppOrigem.Store ? "Microsoft Store"
                : "Programas",
            Titulo = app.Nome,
            Descricao = string.Join(" · ", detalhes),
            Risco = risco,
            GanhoBytes = app.Tamanho,
            GanhoEstimado = app.Tamanho > 0 ? GameModeModule.Formatar(app.Tamanho) : "tamanho desconhecido",

            // Regra 3: desinstalar apaga configuração e às vezes save. Nada
            // vem marcado, nem o bloatware mais óbvio.
            PreMarcado = false,

            Bloqueado = app.Protegido,
            MotivoBloqueio = app.MotivoDaProtecao,
            ComoDesfazer = app.Origem == AppOrigem.Store
                ? "Reinstale pela Microsoft Store. Configurações e dados do app se perdem."
                : "Só reinstalando pelo instalador original. Configurações e dados se perdem.",
            Payload = app
        };
    }

    /// <summary>
    /// Recusa pasta generica como se fosse do app. Varios instaladores gravam
    /// InstallLocation apontando para "C:\Program Files" ou para a raiz do
    /// disco; medir aquilo atribuiria o disco inteiro a um unico programa, e foi
    /// o que fez o resumo somar 1812 GB num disco de 953 GB.
    /// </summary>
    internal static bool PastaConfiavel(string pasta)
    {
        var limpo = pasta.Trim().TrimEnd('\\', '/');

        if (limpo.Length < 4)
            return false;

        // Raiz de volume: "C:" ou "C:\".
        if (limpo.Length <= 3 && limpo.Contains(':'))
            return false;

        var genericas = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        foreach (var generica in genericas)
        {
            if (!string.IsNullOrWhiteSpace(generica)
                && limpo.Equals(generica.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static long Medir(string pasta, CancellationToken ct)
    {
        long total = 0;
        var pilha = new Stack<string>();
        pilha.Push(pasta);

        while (pilha.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var atual = pilha.Pop();

            foreach (var entrada in FastFind.Listar(atual))
            {
                if (entrada.EhPasta)
                    pilha.Push(Path.Combine(atual, entrada.Nome));
                else
                    total += entrada.Tamanho;
            }
        }

        return total;
    }

    // ------------------------------------------------------------------
    // Desinstalação
    // ------------------------------------------------------------------

    public Task<ApplyResult> ApplyAsync(IReadOnlyList<string> itemIds, bool dryRun, CancellationToken ct)
        => Task.Run(() => Desinstalar(itemIds, dryRun, ct), ct);

    private ApplyResult Desinstalar(IReadOnlyList<string> itemIds, bool dryRun, CancellationToken ct)
    {
        var acoes = new List<AppliedAction>();
        long liberado = 0;
        var removidos = 0;

        foreach (var itemId in itemIds)
        {
            ct.ThrowIfCancellationRequested();

            var id = itemId.StartsWith("app:", StringComparison.Ordinal) ? itemId[4..] : itemId;
            var app = _ultimaLista.FirstOrDefault(a => a.Id == id);

            if (app is null)
            {
                acoes.Add(new AppliedAction(itemId, false, "App não encontrado: varra de novo.", null));
                continue;
            }

            // Segunda checagem antes de agir: a lista pode ter vindo de um scan
            // antigo, e protegido continua protegido.
            if (app.Protegido)
            {
                acoes.Add(new AppliedAction(itemId, false,
                    $"{app.Nome} está protegido: {app.MotivoDaProtecao}", null));
                continue;
            }

            if (dryRun)
            {
                acoes.Add(new AppliedAction(itemId, true,
                    $"Desinstalaria {app.Nome} ({GameModeModule.Formatar(app.Tamanho)}) "
                    + $"{(app.TemDesinstalacaoSilenciosa ? "em silêncio" : "abrindo o desinstalador próprio")}.",
                    null));
                continue;
            }

            if (EstaEmExecucao(app))
            {
                acoes.Add(new AppliedAction(itemId, false,
                    $"{app.Nome} está em execução. Feche antes de desinstalar.", null));
                continue;
            }

            var resultado = Executar(app, ct);
            acoes.Add(new AppliedAction(itemId, resultado.Sucesso, resultado.Detalhe, null));

            if (resultado.Sucesso)
            {
                removidos++;
                liberado += app.Tamanho;
            }
        }

        _log.Info(ModuloId, dryRun ? "Desinstalar (dry-run)" : "Desinstalar", null,
            $"{removidos} de {itemIds.Count}, {GameModeModule.Formatar(liberado)}");

        return new ApplyResult
        {
            ModuloId = ModuloId,
            Acoes = acoes,
            DryRun = dryRun,
            Resumo = dryRun
                ? "Simulação concluída. Nada foi desinstalado."
                : $"{removidos} de {itemIds.Count} desinstalados.",
            GanhoMedido = dryRun
                ? "Dry-run não mede ganho."
                : removidos == 0
                    ? "Nada foi removido."
                    : $"Cerca de {GameModeModule.Formatar(liberado)} liberados. "
                    + "Varra de novo para ver o que ficou para trás."
        };
    }

    private bool EstaEmExecucao(InstalledApp app)
    {
        if (app.PastaDeInstalacao is null)
            return false;

        try
        {
            return _processos.GetProcesses().Any(p =>
                p.ExecutablePath is not null
                && p.ExecutablePath.StartsWith(app.PastaDeInstalacao, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private (bool Sucesso, string Detalhe) Executar(InstalledApp app, CancellationToken ct)
    {
        try
        {
            if (app.Origem == AppOrigem.Store && app.PacoteDaStore is not null)
                return RemoverDaStore(app, ct);

            if (app.CodigoMsi is not null)
                return Rodar("msiexec.exe", $"/x {app.CodigoMsi} /qn /norestart", app, ct);

            if (!string.IsNullOrWhiteSpace(app.ComandoSilencioso))
            {
                var (exe, args) = Separar(app.ComandoSilencioso!);
                return Rodar(exe, args, app, ct);
            }

            if (!string.IsNullOrWhiteSpace(app.Comando))
            {
                // Sem comando silencioso, o desinstalador do app abre a própria
                // janela. O GameBoost não fica esperando: o usuário conduz.
                var (exe, args) = Separar(app.Comando!);

                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = true
                });

                return (true, $"{app.Nome}: o desinstalador do próprio app foi aberto. "
                            + "Conclua por ele e varra de novo.");
            }

            return (false, $"{app.Nome} não informa como ser desinstalado.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                      or InvalidOperationException
                                      or FileNotFoundException)
        {
            _log.Error(ModuloId, "Desinstalar", app.Nome, ex.Message, ex);
            return (false, $"{app.Nome}: {ex.Message}");
        }
    }

    private (bool, string) RemoverDaStore(InstalledApp app, CancellationToken ct)
    {
        var comando = $"$ProgressPreference='SilentlyContinue'; "
                    + $"Remove-AppxPackage -Package '{app.PacoteDaStore}' -ErrorAction Stop";

        var codificado = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(comando));

        return Rodar("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {codificado}", app, ct);
    }

    private (bool, string) Rodar(string executavel, string argumentos, InstalledApp app, CancellationToken ct)
    {
        var info = new ProcessStartInfo
        {
            FileName = executavel,
            Arguments = argumentos,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var processo = Process.Start(info);

        if (processo is null)
            return (false, $"{app.Nome}: não foi possível iniciar a desinstalação.");

        // Os dois fluxos precisam de leitor, senão o processo trava quando um
        // deles enche.
        var saida = processo.StandardOutput.ReadToEndAsync(ct);
        var erro = processo.StandardError.ReadToEndAsync(ct);

        if (!processo.WaitForExit((int)Limite.TotalMilliseconds))
        {
            processo.Kill(entireProcessTree: true);
            return (false, $"{app.Nome}: passou de {Limite.TotalMinutes:0} minutos e foi interrompido.");
        }

        var codigo = processo.ExitCode;

        // 0 é sucesso; 3010 é sucesso pedindo reinício; 1605 é "já não estava
        // instalado", que para o usuário dá no mesmo.
        return codigo switch
        {
            0 => (true, $"{app.Nome} desinstalado."),
            3010 => (true, $"{app.Nome} desinstalado. Reinicie para concluir."),
            1605 => (true, $"{app.Nome} já não estava instalado."),
            1602 => (false, $"{app.Nome}: a desinstalação foi cancelada."),
            _ => (false, $"{app.Nome}: o desinstalador terminou com código {codigo}. "
                       + Primeira(erro.GetAwaiter().GetResult()))
        };
    }

    private static string Primeira(string texto)
    {
        var linha = texto.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(linha) ? string.Empty : linha.Trim();
    }

    /// <summary>Separa "C:\App\unins.exe /S" em executável e argumentos, respeitando aspas.</summary>
    internal static (string Executavel, string Argumentos) Separar(string comando)
    {
        var texto = comando.Trim();

        if (texto.StartsWith('"'))
        {
            var fim = texto.IndexOf('"', 1);

            if (fim > 0)
                return (texto[1..fim], fim + 1 < texto.Length ? texto[(fim + 1)..].Trim() : string.Empty);
        }

        // Sem aspas: o executável vai até o ".exe".
        var indice = texto.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);

        if (indice > 0)
        {
            var corte = indice + 4;
            return (texto[..corte], corte < texto.Length ? texto[corte..].Trim() : string.Empty);
        }

        var espaco = texto.IndexOf(' ');
        return espaco > 0 ? (texto[..espaco], texto[(espaco + 1)..]) : (texto, string.Empty);
    }

    // ------------------------------------------------------------------

    /// <summary>
    /// Desinstalação não se desfaz. Dizer isso é mais honesto que oferecer um
    /// botão que não funciona (regra 4).
    /// </summary>
    public Task<ApplyResult> RevertAsync(IReadOnlyList<string> changeIds, bool dryRun, CancellationToken ct)
        => Task.FromResult(new ApplyResult
        {
            ModuloId = ModuloId,
            DryRun = dryRun,
            Resumo = "Desinstalação não tem reversão automática.",
            GanhoMedido = "Para voltar atrás é preciso reinstalar o app. "
                        + "Se você criou um ponto de restauração antes, ele também serve."
        });

    // ==================================================================
    // Aba "Atualizações" (winget)
    // ==================================================================

    private IReadOnlyList<AtualizacaoDisponivel> _ultimasAtualizacoes = Array.Empty<AtualizacaoDisponivel>();

    public IReadOnlyList<AtualizacaoDisponivel> UltimasAtualizacoes => _ultimasAtualizacoes;

    public WingetService Winget => _winget;

    /// <summary>
    /// Lista o que tem versão nova, já classificado e com o que estiver aberto
    /// marcado como tal.
    ///
    /// O winget **não** é chamado na varredura principal de propósito: ele
    /// consulta a rede e leva dezenas de segundos na primeira vez do dia.
    /// Amarrar isso à lista de apps instalados faria a tela inteira esperar por
    /// uma informação que nem todo mundo abriu para ver.
    /// </summary>
    public async Task<ScanResult> VarrerAtualizacoesAsync(IProgress<ModuleProgress>? progresso, CancellationToken ct)
    {
        progresso?.Report(new ModuleProgress("Procurando o winget", 10));

        if (await _winget.DetectarAsync(ct) is null)
        {
            return new ScanResult
            {
                ModuloId = ModuloId,
                Momento = _relogio.Now,
                Resumo = "O winget não está instalado nesta máquina.",
                Avisos = new[]
                {
                    "O winget vem junto com o App Installer, da Microsoft. Sem ele esta aba "
                  + "não tem como funcionar. O botão abre a página dele na Microsoft Store."
                }
            };
        }

        progresso?.Report(new ModuleProgress("Consultando o winget (pode demorar)", 30));

        var lista = await _winget.ListarAsync(ct);
        _ultimasAtualizacoes = lista;

        progresso?.Report(new ModuleProgress("Cruzando com os apps instalados", 80));

        // Quais estão rodando agora: atualizar app aberto falha ou, pior, mata
        // o que o usuário estava usando.
        var rodando = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var processo in _processos.GetProcesses())
                rodando.Add(processo.Name);
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            _log.Warn(ModuloId, "Atualizacoes", null, ex.Message);
        }

        var itens = lista.Select(a => MontarAtualizacao(a, rodando)).ToList();

        progresso?.Report(new ModuleProgress("Pronto", 100));

        var seguranca = lista.Count(UpdateCatalog.EhDeSeguranca);
        var sozinhos = lista.Count(a => UpdateCatalog.Classificar(a) == ClasseDeAtualizacao.AtualizaSozinho);

        var avisos = new List<string>();

        if (seguranca > 0)
        {
            avisos.Add($"{seguranca} são de programas que abrem arquivo vindo da internet "
                     + "(navegador, compactador, leitor de PDF, Java). Nesses, ficar "
                     + "desatualizado é problema de segurança, não de conforto.");
        }

        if (sozinhos > 0)
        {
            avisos.Add($"{sozinhos} se atualizam sozinhos e aparecem marcados assim. "
                     + "Forçar pelo winget pode brigar com o atualizador do próprio app.");
        }

        return new ScanResult
        {
            ModuloId = ModuloId,
            Itens = itens,
            Momento = _relogio.Now,
            Resumo = lista.Count == 0
                ? "Tudo em dia: o winget não encontrou atualização pendente."
                : $"{lista.Count} aplicativos com versão nova disponível.",
            Avisos = avisos
        };
    }

    private ActionItem MontarAtualizacao(AtualizacaoDisponivel a, IReadOnlySet<string> rodando)
    {
        var classe = UpdateCatalog.Classificar(a);
        var seguranca = UpdateCatalog.EhDeSeguranca(a);

        // O executável do app não vem na tabela do winget. O que dá para
        // comparar é o nome, e só quando ele é específico o bastante: um nome de
        // três letras casaria com qualquer coisa.
        var primeiraPalavra = a.Nome.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        var aberto = primeiraPalavra.Length >= 4
                     && rodando.Any(nome => nome.StartsWith(primeiraPalavra, StringComparison.OrdinalIgnoreCase));

        var detalhes = new List<string>
        {
            a.VersaoIncerta ? $"versão instalada desconhecida, repositório tem {a.VersaoNova}"
                            : $"{a.VersaoAtual} para {a.VersaoNova}"
        };

        detalhes.Add(a.DaStore ? "Microsoft Store" : "winget");

        if (seguranca)
            detalhes.Add("atualização de segurança");

        if (aberto)
            detalhes.Add($"{primeiraPalavra} parece estar aberto — feche antes");

        var explicacao = UpdateCatalog.Explicacao(classe);

        if (explicacao.Length > 0)
            detalhes.Add(explicacao);

        var risco = classe switch
        {
            ClasseDeAtualizacao.Driver => RiskLevel.Alto,
            ClasseDeAtualizacao.Runtime => RiskLevel.Medio,
            ClasseDeAtualizacao.AtualizaSozinho => RiskLevel.Medio,
            ClasseDeAtualizacao.Incerta => RiskLevel.Medio,
            _ => RiskLevel.Baixo
        };

        return new ActionItem
        {
            Id = $"update:{a.Id}",
            Categoria = UpdateCatalog.Categoria(classe),
            Titulo = a.Nome,
            Descricao = string.Join(" · ", detalhes),
            Risco = risco,
            GanhoEstimado = a.VersaoNova,

            // Regra 3: nem a atualização de segurança vem marcada. Instalar
            // versão nova pode quebrar o que funcionava, e a escolha é de quem
            // usa a máquina.
            PreMarcado = false,

            // Driver é o único bloqueado: o GameBoost não instala driver, e
            // isso vale também quando o winget se oferece para fazer.
            Bloqueado = classe == ClasseDeAtualizacao.Driver,
            MotivoBloqueio = classe == ClasseDeAtualizacao.Driver
                ? UpdateCatalog.Explicacao(ClasseDeAtualizacao.Driver)
                : null,
            RotuloBloqueio = "Driver",

            ComoDesfazer = a.DaStore
                ? "A Microsoft Store não permite voltar a uma versão anterior."
                : "Não há como voltar a versão pelo winget. Se a versão nova der problema, "
                + "reinstale a antiga pelo instalador do fabricante.",
            Payload = a
        };
    }

    /// <summary>
    /// Atualiza um app por vez, em sequência, com progresso por item.
    ///
    /// `winget upgrade --all` faria tudo de uma vez e seria mais curto, mas
    /// perde o que importa: com `--all`, um app que falha some no meio da saída
    /// e não dá para dizer ao usuário qual foi nem por quê.
    /// </summary>
    public async Task<ApplyResult> AtualizarAsync(
        IReadOnlyList<string> itemIds, bool dryRun, IProgress<ModuleProgress>? progresso, CancellationToken ct)
    {
        var alvos = itemIds
            .Where(id => id.StartsWith("update:", StringComparison.Ordinal))
            .Select(id => id["update:".Length..])
            .Select(id => _ultimasAtualizacoes.FirstOrDefault(a => a.Id == id))
            .Where(a => a is not null)
            .Select(a => a!)
            .ToList();

        var acoes = new List<AppliedAction>();

        for (var i = 0; i < alvos.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var alvo = alvos[i];

            progresso?.Report(new ModuleProgress(
                $"Atualizando {alvo.Nome} ({i + 1} de {alvos.Count})",
                alvos.Count == 0 ? 100 : (i + 1) * 100 / alvos.Count));

            if (UpdateCatalog.Classificar(alvo) == ClasseDeAtualizacao.Driver)
            {
                acoes.Add(new AppliedAction($"update:{alvo.Id}", false,
                    $"{alvo.Nome} é driver: o GameBoost não instala driver.", null));
                continue;
            }

            var registro = await _winget.AtualizarAsync(alvo, dryRun, ct);

            acoes.Add(new AppliedAction(
                $"update:{alvo.Id}",
                registro.Sucesso,
                registro.Sucesso
                    ? $"{alvo.Nome} atualizado para {alvo.VersaoNova}."
                    : $"{alvo.Nome}: {registro.Erro}",
                null));
        }

        var ok = acoes.Count(a => a.Sucesso);

        return new ApplyResult
        {
            ModuloId = ModuloId,
            Acoes = acoes,
            DryRun = dryRun,
            Resumo = dryRun
                ? $"Simulação: {acoes.Count} aplicativos seriam atualizados."
                : $"{ok} de {acoes.Count} aplicativos atualizados.",
            GanhoMedido = acoes.Any(a => !a.Sucesso)
                ? $"O log detalhado do winget fica em {WingetService.PastaDeLogs()}"
                : string.Empty
        };
    }

    /// <summary>Restos deixados por apps já removidos (aba própria, seção 5.3).</summary>
    public IReadOnlyList<Resto> VarrerRestos(IProgress<string>? progresso, CancellationToken ct)
        => _restos.Varrer(_ultimaLista, progresso, ct);

    /// <summary>Manda restos para a Lixeira. Nunca apaga de vez.</summary>
    public (int Removidos, long Bytes) RemoverRestos(IReadOnlyList<Resto> escolhidos)
    {
        if (escolhidos.Count == 0)
            return (0, 0);

        var caminhos = escolhidos.Select(r => r.Caminho).ToList();
        var bytes = escolhidos.Sum(r => r.Bytes);

        var ok = RecycleBinBridge.ParaLixeira(caminhos);

        _log.Info(ModuloId, "RemoverRestos", null,
            ok ? $"{caminhos.Count} pastas para a Lixeira, {GameModeModule.Formatar(bytes)}"
               : "o Windows recusou mover para a Lixeira");

        return ok ? (caminhos.Count, bytes) : (0, 0);
    }
}
