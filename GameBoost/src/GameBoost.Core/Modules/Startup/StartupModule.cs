using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Modules.GameMode;
using GameBoost.Core.Native;
using GameBoost.Core.State;

namespace GameBoost.Core.Modules.Startup;

/// <summary>
/// Gerenciador de inicialização (seção 5.6).
///
/// Desativar grava no `StartupApproved` **no mesmo formato do Gerenciador de
/// Tarefas**: assim o Windows mostra o item como desativado nos dois lugares, e
/// o usuário pode reverter por onde preferir. Escrever de outro jeito deixaria
/// o Gerenciador de Tarefas mentindo.
/// </summary>
public sealed class StartupModule : IModule
{
    public const string ModuloId = "startup";

    private const string RunUsuario = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunMaquina = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunMaquina32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";

    private const string AprovadoUsuarioRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string AprovadoUsuarioPasta = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    private readonly IRegistryService _registro;
    private readonly IStateBackup _backup;
    private readonly IRollbackEngine _rollback;
    private readonly IGameBoostLogger _log;
    private readonly IClock _relogio;

    private IReadOnlyList<StartupEntry> _ultimaLista = Array.Empty<StartupEntry>();

    public StartupModule(
        IRegistryService registro,
        IStateBackup backup,
        IRollbackEngine rollback,
        IGameBoostLogger log,
        IClock relogio)
    {
        _registro = registro;
        _backup = backup;
        _rollback = rollback;
        _log = log;
        _relogio = relogio;
    }

    public string Id => ModuloId;
    public string Nome => "Inicialização";
    public string Descricao => "O que abre junto com o Windows, e quanto isso pesa no boot.";

    // ---------------- Proteção ----------------

    /// <summary>Nunca desativar: tira áudio, rede, acessibilidade ou proteção.</summary>
    private static readonly (string Trecho, string Motivo)[] Intocaveis =
    {
        ("securityhealth", "É a Segurança do Windows."),
        ("windows defender", "É o antivírus do Windows."),
        ("realtek", "Driver de áudio: desativar pode tirar o som."),
        ("audio", "Componente de áudio: desativar pode tirar o som."),
        ("synaptics", "Driver do touchpad: desativar pode travar o touchpad."),
        ("elan", "Driver do touchpad."),
        ("touchpad", "Driver do touchpad."),
        ("bluetooth", "Driver de Bluetooth: desativar derruba mouse e fone sem fio."),
        ("intel(r) graphics", "Componente do driver de vídeo."),
        ("nvidia", "Componente do driver de vídeo."),
        ("amd", "Componente do driver de vídeo."),
        ("kaspersky", "É o seu antivírus."),
        ("avast", "É o seu antivírus."),
        ("bitdefender", "É o seu antivírus."),
        ("mcafee", "É o seu antivírus."),
        ("norton", "É o seu antivírus."),
        ("eset", "É o seu antivírus."),
        ("narrator", "Recurso de acessibilidade."),
        ("magnify", "Recurso de acessibilidade.")
    };

    /// <summary>Sugestões da seção 5.6: aparecem com dica, nunca marcadas.</summary>
    private static readonly string[] Sugeridos =
    {
        "spotify", "discord", "steam", "epic", "ea app", "origin", "ubisoft",
        "teams", "skype", "adobe", "acrobat", "itunes", "icloud", "cortana",
        "onedrive", "dropbox", "zoom", "slack", "java update", "quicktime",
        "wildtangent", "ccleaner", "utorrent", "qbittorrent"
    };

    // ---------------- Varredura ----------------

    public Task<ScanResult> ScanAsync(IProgress<ModuleProgress>? progress, CancellationToken ct)
        => Task.Run(() => Varrer(progress, ct), ct);

    private ScanResult Varrer(IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var entradas = new List<StartupEntry>();

        progress?.Report(new ModuleProgress("Lendo o registro", 20));
        entradas.AddRange(DoRegistro(RegistryRoot.CurrentUser, RunUsuario, "HKCU"));
        entradas.AddRange(DoRegistro(RegistryRoot.LocalMachine, RunMaquina, "HKLM"));
        entradas.AddRange(DoRegistro(RegistryRoot.LocalMachine, RunMaquina32, "HKLM32"));

        progress?.Report(new ModuleProgress("Lendo a pasta Inicializar", 50));
        entradas.AddRange(DaPasta(Environment.SpecialFolder.Startup, "usuário"));
        entradas.AddRange(DaPasta(Environment.SpecialFolder.CommonStartup, "todos os usuários"));

        progress?.Report(new ModuleProgress("Conferindo o que está desativado", 70));
        AplicarEstado(entradas);

        foreach (var entrada in entradas)
        {
            ct.ThrowIfCancellationRequested();
            Avaliar(entrada);
        }

        _ultimaLista = entradas;

        var ativos = entradas.Count(e => e.Ativo);
        var estimativa = EstimarSegundos(entradas.Where(e => e.Ativo));

        progress?.Report(new ModuleProgress("Pronto", 100));

        return new ScanResult
        {
            ModuloId = ModuloId,
            Itens = entradas.Select(MontarItem).ToList(),
            Momento = _relogio.Now,
            Resumo = $"{ativos} programas abrem com o Windows, de {entradas.Count} registrados. "
                   + $"Estimativa grosseira: cerca de {estimativa} segundos a mais no boot.",
            Avisos = new[]
            {
                "A estimativa de tempo é grosseira: vem do tamanho do executável, não de "
              + "medição real do boot. Serve para comparar itens entre si, não como número absoluto."
            }
        };
    }

    private IEnumerable<StartupEntry> DoRegistro(RegistryRoot raiz, string chave, string rotulo)
    {
        IReadOnlyList<string> valores;

        try
        {
            valores = _registro.GetValueNames(raiz, chave);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            _log.Warn(ModuloId, "Registro", chave, ex.Message);
            yield break;
        }

        foreach (var nome in valores)
        {
            var comando = _registro.GetValue(raiz, chave, nome) as string;

            if (string.IsNullOrWhiteSpace(comando))
                continue;

            var executavel = ExtrairExecutavel(comando);

            // Uma chamada só: WinVerifyTrust não é barato e antes rodava duas
            // vezes por entrada.
            var assinatura = Assinatura(executavel);

            yield return new StartupEntry
            {
                Id = $"reg:{rotulo}:{nome}",
                Nome = nome,
                Origem = StartupOrigem.Registro,
                Local = $"{rotulo}\\...\\Run",
                Comando = comando,
                Executavel = executavel,
                Publisher = assinatura.Publisher,
                Assinado = assinatura.Assinado,
                TamanhoDoExecutavel = Tamanho(executavel),
                Impacto = Estimar(executavel)
            };
        }
    }

    private IEnumerable<StartupEntry> DaPasta(Environment.SpecialFolder pasta, string rotulo)
    {
        var caminho = Environment.GetFolderPath(pasta);

        if (string.IsNullOrWhiteSpace(caminho) || !Directory.Exists(caminho))
            yield break;

        foreach (var arquivo in Directory.EnumerateFiles(caminho))
        {
            if (arquivo.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                continue;

            var nome = Path.GetFileNameWithoutExtension(arquivo);

            yield return new StartupEntry
            {
                Id = $"pasta:{rotulo}:{Path.GetFileName(arquivo)}",
                Nome = nome,
                Origem = StartupOrigem.Pasta,
                Local = $"Inicializar ({rotulo})",
                Comando = arquivo,
                Executavel = arquivo,
                TamanhoDoExecutavel = Tamanho(arquivo),
                Impacto = ImpactoNoBoot.Medio
            };
        }
    }

    /// <summary>
    /// Lê o StartupApproved para saber o que já está desativado. O primeiro
    /// byte diz o estado: valores ímpares e 2 significam ativo; 3 significa
    /// desativado pelo usuário.
    /// </summary>
    private void AplicarEstado(List<StartupEntry> entradas)
    {
        foreach (var (chave, origem) in new[]
                 {
                     (AprovadoUsuarioRun, StartupOrigem.Registro),
                     (AprovadoUsuarioPasta, StartupOrigem.Pasta)
                 })
        {
            IReadOnlyList<string> nomes;

            try
            {
                nomes = _registro.GetValueNames(RegistryRoot.CurrentUser, chave);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }

            foreach (var nome in nomes)
            {
                if (_registro.GetValue(RegistryRoot.CurrentUser, chave, nome) is not byte[] dados || dados.Length == 0)
                    continue;

                var desativado = (dados[0] & 0x01) != 0 && dados[0] != 0x02;

                foreach (var entrada in entradas.Where(e =>
                             e.Origem == origem
                             && (e.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase)
                                 || e.Id.EndsWith(nome, StringComparison.OrdinalIgnoreCase))))
                {
                    entrada.Ativo = !desativado;
                }
            }
        }
    }

    private static void Avaliar(StartupEntry entrada)
    {
        var texto = $"{entrada.Nome} {entrada.Executavel} {entrada.Publisher}".ToLowerInvariant();

        foreach (var (trecho, motivo) in Intocaveis)
        {
            if (texto.Contains(trecho, StringComparison.Ordinal))
            {
                entrada.Protegido = true;
                entrada.MotivoDaProtecao = motivo;
                return;
            }
        }

        entrada.Sugerido = Sugeridos.Any(s => texto.Contains(s, StringComparison.Ordinal));
    }

    private ActionItem MontarItem(StartupEntry e)
    {
        // O impacto já aparece na coluna da direita; repetir aqui só poluía a
        // linha.
        var detalhes = new List<string> { e.TextoDaOrigem };

        if (!e.Ativo)
            detalhes.Insert(0, "já desativado");

        if (e.Publisher is not null)
            detalhes.Add(e.Publisher);
        else if (EhExecutavel(e.Executavel))
        {
            // Atalho da pasta Inicializar aponta para outro lugar e nunca é
            // assinado: falar de assinatura dele não diria nada sobre o programa.
            detalhes.Add(e.Assinado == false
                ? "sem assinatura digital"
                : "assinatura não verificável");
        }

        return new ActionItem
        {
            Id = $"startup:{e.Id}",
            Categoria = e.Protegido ? "Protegidos"
                : !e.Ativo ? "Já desativados"
                : e.Sugerido ? "Sugestões"
                : "Ativos",
            Titulo = e.Nome,
            Descricao = string.Join(" · ", detalhes)
                      + (e.Comando is not null ? $" · {e.Comando}" : string.Empty),
            Risco = e.Protegido ? RiskLevel.Alto : e.Sugerido ? RiskLevel.Baixo : RiskLevel.Medio,
            GanhoBytes = 0,
            GanhoEstimado = e.Ativo ? e.TextoDoImpacto : "já desativado",

            // Regra 3: o usuário decide o que abre com a máquina dele.
            PreMarcado = false,

            Bloqueado = e.Protegido || !e.Ativo,
            MotivoBloqueio = e.Protegido ? e.MotivoDaProtecao
                : !e.Ativo ? "Já está desativado."
                : null,
            ComoDesfazer = "Reativar pelo próprio GameBoost, pelo Gerenciador de Tarefas "
                         + "ou em Configurações, Aplicativos, Inicializar. O programa continua "
                         + "instalado: só deixa de abrir sozinho.",
            Payload = e
        };
    }

    // ---------------- Estimativa ----------------

    /// <summary>
    /// Estimativa pelo tamanho do executável. A seção 5.6 pede isso
    /// explicitamente como aproximação, e o resumo avisa que é grosseira:
    /// medir boot de verdade exigiria o Windows Performance Toolkit.
    /// </summary>
    private static ImpactoNoBoot Estimar(string? executavel)
    {
        var tamanho = Tamanho(executavel);

        return tamanho switch
        {
            0 => ImpactoNoBoot.Desconhecido,
            > 50L * 1024 * 1024 => ImpactoNoBoot.Alto,
            > 5L * 1024 * 1024 => ImpactoNoBoot.Medio,
            _ => ImpactoNoBoot.Baixo
        };
    }

    private static int EstimarSegundos(IEnumerable<StartupEntry> ativos)
        => ativos.Sum(e => e.Impacto switch
        {
            ImpactoNoBoot.Alto => 3,
            ImpactoNoBoot.Medio => 1,
            _ => 0
        });

    private static long Tamanho(string? executavel)
    {
        if (string.IsNullOrWhiteSpace(executavel))
            return 0;

        try
        {
            var info = new FileInfo(executavel);
            return info.Exists ? info.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }

    private static bool EhExecutavel(string? caminho)
        => caminho is not null && caminho.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Assinatura e fabricante numa passada. Devolve <c>null</c> em Assinado
    /// quando o comando aponta para um arquivo que não pode ser lido — caso
    /// típico de app da Store em WindowsApps, cuja ACL barra até o
    /// administrador. Dizer "sem assinatura digital" ali seria acusação falsa.
    /// </summary>
    private static (bool? Assinado, string? Publisher) Assinatura(string? executavel)
    {
        if (string.IsNullOrWhiteSpace(executavel) || !File.Exists(executavel))
            return (null, null);

        return (Authenticode.Assinado(executavel), Authenticode.Fabricante(executavel));
    }

    internal static string? ExtrairExecutavel(string comando)
    {
        var texto = comando.Trim();

        if (texto.StartsWith('"'))
        {
            var fim = texto.IndexOf('"', 1);
            return fim > 0 ? texto[1..fim] : null;
        }

        var indice = texto.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return indice > 0 ? texto[..(indice + 4)] : null;
    }

    // ---------------- Desativar e reativar ----------------

    public Task<ApplyResult> ApplyAsync(IReadOnlyList<string> itemIds, bool dryRun, CancellationToken ct)
        => Task.Run(() => Alternar(itemIds, desativar: true, dryRun), ct);

    public Task<ApplyResult> RevertAsync(IReadOnlyList<string> changeIds, bool dryRun, CancellationToken ct)
        => Task.Run(() => Reverter(changeIds, dryRun), ct);

    private ApplyResult Alternar(IReadOnlyList<string> itemIds, bool desativar, bool dryRun)
    {
        var acoes = new List<AppliedAction>();

        foreach (var itemId in itemIds)
        {
            var id = itemId.StartsWith("startup:", StringComparison.Ordinal) ? itemId[8..] : itemId;
            var entrada = _ultimaLista.FirstOrDefault(e => e.Id == id);

            if (entrada is null)
            {
                acoes.Add(new AppliedAction(itemId, false, "Item não encontrado: varra de novo.", null));
                continue;
            }

            if (entrada.Protegido)
            {
                acoes.Add(new AppliedAction(itemId, false,
                    $"{entrada.Nome} está protegido: {entrada.MotivoDaProtecao}", null));
                continue;
            }

            if (dryRun)
            {
                acoes.Add(new AppliedAction(itemId, true,
                    $"{(desativar ? "Desativaria" : "Reativaria")} {entrada.Nome}.", null));
                continue;
            }

            acoes.Add(Gravar(entrada, desativar, itemId));
        }

        var mudados = acoes.Count(a => a.Sucesso);

        return new ApplyResult
        {
            ModuloId = ModuloId,
            Acoes = acoes,
            DryRun = dryRun,
            Resumo = dryRun
                ? "Simulação concluída. Nada foi alterado."
                : $"{mudados} itens {(desativar ? "desativados" : "reativados")}.",
            GanhoMedido = dryRun || mudados == 0
                ? string.Empty
                : "O efeito aparece no próximo boot. Os programas continuam instalados."
        };
    }

    private AppliedAction Gravar(StartupEntry entrada, bool desativar, string itemId)
    {
        var chave = entrada.Origem == StartupOrigem.Pasta ? AprovadoUsuarioPasta : AprovadoUsuarioRun;
        var nome = entrada.Origem == StartupOrigem.Pasta
            ? entrada.Id.Split(':').Last()
            : entrada.Nome;

        try
        {
            var anterior = _registro.GetValue(RegistryRoot.CurrentUser, chave, nome) as byte[];

            // Formato do Gerenciador de Tarefas: 12 bytes, primeiro byte com o
            // estado e os 8 últimos com o FILETIME da mudança.
            var valor = new byte[12];

            if (anterior is { Length: 12 })
                Array.Copy(anterior, valor, 12);

            valor[0] = desativar ? (byte)0x03 : (byte)0x02;
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(valor, 4);

            _backup.Registrar(new ChangeRecord
            {
                Modulo = ModuloId,
                Tipo = ChangeType.Startup,
                Alvo = chave,
                SubAlvo = nome,
                ValorAnterior = anterior is null ? null : Convert.ToHexString(anterior),
                ValorNovo = Convert.ToHexString(valor),
                ValorAnteriorExistia = anterior is not null,
                Extras =
                {
                    ["root"] = RegistryRoot.CurrentUser.ToString(),
                    ["kind"] = RegistryValueKindLite.Binary.ToString(),
                    ["nome"] = $"Inicialização: {entrada.Nome}"
                }
            });

            _registro.SetValue(RegistryRoot.CurrentUser, chave, nome, valor, RegistryValueKindLite.Binary);
            entrada.Ativo = !desativar;

            _log.Info(ModuloId, desativar ? "Desativar" : "Reativar", entrada.Nome, "gravado em StartupApproved");

            return new AppliedAction(itemId, true,
                $"{entrada.Nome} {(desativar ? "não abre mais" : "volta a abrir")} com o Windows.", null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _log.Error(ModuloId, "Gravar", entrada.Nome, ex.Message, ex);
            return new AppliedAction(itemId, false, $"{entrada.Nome}: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Desfaz pelo ChangeRecord, e não por nome.
    ///
    /// A primeira versão recebia os ids que a tela manda — que são **GUIDs de
    /// ChangeRecord** — e tentava casá-los com o nome do programa
    /// (`id.Contains(e.Nome)`). Um GUID nunca contém "WallpaperEngine", então a
    /// lista de alvos saía vazia, nada era desfeito, e o método **devolvia
    /// sucesso**: a tela dizia "Pronto" e o item continuava desativado.
    ///
    /// Além de errado, aquilo dependia de `_ultimaLista` estar preenchida —
    /// ou seja, de alguém ter varrido nesta sessão. Reabrir o app e clicar em
    /// Reverter não funcionaria nem com o casamento certo.
    ///
    /// O ChangeRecord já carrega tudo o que a reversão precisa: a chave, o nome
    /// do valor, o conteúdo anterior e se ele existia. É o que o
    /// <see cref="IRollbackEngine"/> consome, e é o mesmo caminho que os Tweaks
    /// usam.
    /// </summary>
    private ApplyResult Reverter(IReadOnlyList<string> changeIds, bool dryRun)
    {
        var pendentes = _backup.Pendentes.Where(r => r.Modulo == ModuloId).ToList();

        var alvos = changeIds.Count > 0
            ? pendentes.Where(r => changeIds.Contains(r.Id)).Select(r => r.Id).ToList()
            : pendentes.Select(r => r.Id).ToList();

        if (alvos.Count == 0)
        {
            return new ApplyResult
            {
                ModuloId = ModuloId,
                DryRun = dryRun,
                Resumo = "Não há item de inicialização para reativar."
            };
        }

        var resultados = _rollback.Reverter(alvos, dryRun);

        var acoes = resultados
            .Select(r => new AppliedAction(r.ChangeId, r.Sucesso, r.Detalhe, r.ChangeId))
            .ToList();

        var ok = acoes.Count(a => a.Sucesso);

        // A lista em memória tem que acompanhar, senão a tela continua
        // mostrando "já desativado" no item que acabou de voltar.
        if (!dryRun && ok > 0)
        {
            var nomes = pendentes
                .Where(r => alvos.Contains(r.Id))
                .Select(r => r.SubAlvo)
                .Where(n => n is not null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

            foreach (var entrada in _ultimaLista.Where(e => nomes.Contains(e.Nome)))
                entrada.Ativo = true;
        }

        _log.Info(ModuloId, dryRun ? "Reverter (dry-run)" : "Reverter", null,
            $"{ok} de {acoes.Count} itens reativados");

        return new ApplyResult
        {
            ModuloId = ModuloId,
            Acoes = acoes,
            DryRun = dryRun,
            Resumo = dryRun
                ? $"Simulação: {ok} de {acoes.Count} programas voltariam a abrir com o Windows."
                : $"{ok} de {acoes.Count} programas voltam a abrir com o Windows.",
            GanhoMedido = "O efeito aparece no próximo boot."
        };
    }

    /// <summary>Reativa um item específico, pelo botão da lista.</summary>
    public Task<ApplyResult> ReativarAsync(IReadOnlyList<string> itemIds, bool dryRun, CancellationToken ct)
        => Task.Run(() => Alternar(itemIds, desativar: false, dryRun), ct);
}
