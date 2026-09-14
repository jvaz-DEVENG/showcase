using System.Diagnostics;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Safety;
using GameBoost.Core.State;

namespace GameBoost.Core.Modules.Tweaks;

/// <summary>
/// Tweaks de jogos (seção 5.8) e serviços do Windows (seção 5.7) na mesma tela.
///
/// São duas coisas diferentes com a mesma cara para o usuário: "um ajuste do
/// sistema que eu ligo ou desligo, e que dá para voltar atrás". O catálogo de
/// cada um vive separado (<see cref="TweakCatalog"/> e
/// <see cref="ServiceCatalog"/>); aqui eles viram uma lista só, agrupada por
/// categoria.
///
/// **Nada vem pré-marcado**, nem o que o próprio GameBoost recomenda. O selo
/// "recomendado" aparece no texto; a caixa continua vazia (regra 3).
/// </summary>
public sealed class TweaksModule : IModule
{
    public const string ModuloId = "tweaks";

    private const string PrefixoTweak = "tweak:";
    private const string PrefixoServico = "servico:";

    private readonly IRegistryService _registro;
    private readonly IServiceControllerService _servicos;
    private readonly ISafetyGuard _guarda;
    private readonly IStateBackup _backup;
    private readonly IRollbackEngine _rollback;
    private readonly IGameBoostLogger _log;
    private readonly IClock _relogio;

    public TweaksModule(
        IRegistryService registro,
        IServiceControllerService servicos,
        ISafetyGuard guarda,
        IStateBackup backup,
        IRollbackEngine rollback,
        IGameBoostLogger log,
        IClock relogio)
    {
        _registro = registro;
        _servicos = servicos;
        _guarda = guarda;
        _backup = backup;
        _rollback = rollback;
        _log = log;
        _relogio = relogio;
    }

    public string Id => ModuloId;
    public string Nome => "Tweaks";
    public string Descricao => "Ajustes do Windows para jogos e serviços que dá para desligar, cada um com o efeito real.";

    /// <summary>True quando algum tweak aplicado nesta sessão só vale após reiniciar.</summary>
    public bool PrecisaReiniciar { get; private set; }

    // ==================================================================
    // Varredura
    // ==================================================================

    public Task<ScanResult> ScanAsync(IProgress<ModuleProgress>? progress, CancellationToken ct)
        => Task.Run(() => Varrer(progress, ct), ct);

    private ScanResult Varrer(IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new ModuleProgress("Lendo os ajustes atuais", 20));

        var itens = new List<ActionItem>();

        foreach (var tweak in TweakCatalog.Todos)
        {
            ct.ThrowIfCancellationRequested();
            itens.Add(MontarTweak(tweak));
        }

        progress?.Report(new ModuleProgress("Lendo os serviços", 60));

        foreach (var servico in ServiceCatalog.Todos)
        {
            ct.ThrowIfCancellationRequested();

            var item = MontarServico(servico);
            if (item is not null)
                itens.Add(item);
        }

        progress?.Report(new ModuleProgress("Pronto", 100));

        var ligados = itens.Count(i => i.Payload is TweakEstado { Ligado: true });
        var disponiveis = itens.Count(i => !i.Bloqueado);

        var avisos = new List<string>();

        if (itens.Any(i => i.Payload is TweakEstado { Definicao.ExigeReboot: true, Ligado: false }))
            avisos.Add("Alguns ajustes desta lista só passam a valer depois de reiniciar. O aviso aparece na confirmação.");

        if (!CoreServices.RodandoComoAdministrador())
            avisos.Add("Sem privilégios de administrador, os ajustes que gravam em HKLM aparecem, mas não podem ser aplicados.");

        return new ScanResult
        {
            ModuloId = ModuloId,
            Itens = itens,
            Momento = _relogio.Now,
            Resumo = $"{itens.Count} ajustes no catálogo, {ligados} já ativos na sua máquina. "
                   + $"{disponiveis} podem ser alterados por aqui.",
            Avisos = avisos
        };
    }

    /// <summary>O que a UI guarda por trás de cada linha.</summary>
    public sealed record TweakEstado(TweakDefinition Definicao, bool Ligado, string EstadoAtual);

    /// <summary>Estado de um serviço por trás da linha.</summary>
    public sealed record ServicoEstado(ServiceTweak Catalogo, ServiceInfo Atual);

    private ActionItem MontarTweak(TweakDefinition tweak)
    {
        var (ligado, estado) = LerEstado(tweak);

        var bloqueado = tweak.Tipo == TweakKind.SomenteLeitura
                        || (tweak.ExigeAdmin && !CoreServices.RodandoComoAdministrador());

        var precisaDeAdmin = tweak.ExigeAdmin && !CoreServices.RodandoComoAdministrador();

        var motivo = tweak.Tipo == TweakKind.SomenteLeitura
            ? "O GameBoost não altera isto. O botão abre a tela do Windows onde a decisão é sua."
            : precisaDeAdmin
                ? "Precisa de privilégios de administrador. Reabra o GameBoost como administrador."
                : null;

        // Selo honesto: "Protegido" é o GameBoost se recusando a mexer;
        // precisar de elevação é outra coisa, e some ao reabrir elevado.
        var rotulo = tweak.Tipo == TweakKind.SomenteLeitura ? "Só informativo"
            : precisaDeAdmin ? "Precisa de admin"
            : "Protegido";

        var detalhes = new List<string> { estado };

        if (tweak.Recomendado)
            detalhes.Add("recomendado");

        if (tweak.ExigeReboot)
            detalhes.Add("só vale após reiniciar");

        return new ActionItem
        {
            Id = PrefixoTweak + tweak.Id,
            Categoria = tweak.Categoria,
            Titulo = tweak.Nome,
            Descricao = string.Join(" · ", detalhes) + " — " + tweak.EfeitoReal,
            Risco = tweak.Risco,
            GanhoEstimado = ligado ? "já ativo" : "desligado",

            // Regra 3. Nem o que o GameBoost recomenda vem marcado: recomendação
            // é informação, não consentimento.
            PreMarcado = false,

            Bloqueado = bloqueado,
            MotivoBloqueio = motivo,
            RotuloBloqueio = rotulo,
            ComoDesfazer = tweak.ComoDesfazer,
            Payload = new TweakEstado(tweak, ligado, estado)
        };
    }

    /// <summary>
    /// Lê o estado atual do tweak. Um tweak "desligado" quase nunca significa
    /// valor zero: na maioria das chaves ele significa **valor ausente**, e o
    /// Windows assume o padrão. Confundir os dois é o que faz ferramenta de
    /// tweak gravar lixo que nunca sai.
    /// </summary>
    private (bool Ligado, string Estado) LerEstado(TweakDefinition tweak)
    {
        if (tweak.Tipo == TweakKind.Powercfg)
            return LerHibernacao();

        if (tweak.Valores.Count == 0)
            return (false, "estado desconhecido");

        var ligados = 0;

        foreach (var valor in tweak.Valores)
        {
            var atual = _registro.GetValue(valor.Root, valor.SubKey, valor.ValueName);

            if (atual is not null && IgualAo(atual, valor.ValorLigado))
                ligados++;
        }

        if (ligados == tweak.Valores.Count)
            return (true, "ativo");

        if (ligados == 0)
            return (false, "desligado");

        return (false, $"aplicado pela metade ({ligados} de {tweak.Valores.Count})");
    }

    private const string ChavePower = @"SYSTEM\CurrentControlSet\Control\Power";

    /// <summary>
    /// Estado da hibernação, lido do registro.
    ///
    /// A tentação era procurar `C:\hiberfil.sys`: se o arquivo está lá, a
    /// hibernação está ligada. Só que `File.Exists` devolve **false** para ele
    /// mesmo com a hibernação ligada — o arquivo é de sistema e a ACL barra a
    /// consulta sem elevação. O resultado seria dizer "já desligada" para todo
    /// mundo que não abriu o app como administrador, que é o caminho padrão.
    ///
    /// `powercfg /a` daria a resposta certa, mas o texto dele é traduzido, e
    /// procurar a palavra "Hibernar" quebraria em qualquer outro idioma.
    /// </summary>
    private (bool, string) LerHibernacao()
    {
        var explicito = _registro.GetValue(RegistryRoot.LocalMachine, ChavePower, "HibernateEnabled");

        if (explicito is int valor)
        {
            return valor == 0
                ? (true, "hibernação já desligada")
                : (false, "hibernação ligada");
        }

        // Sem o valor explícito, vale o padrão da plataforma. É o caso de quem
        // nunca mexeu nisso.
        var padrao = _registro.GetValue(RegistryRoot.LocalMachine, ChavePower, "HibernateEnabledDefault");

        if (padrao is int p)
        {
            return p == 0
                ? (true, "hibernação já desligada")
                : (false, "hibernação ligada");
        }

        return (false, "estado da hibernação desconhecido");
    }

    private static bool IgualAo(object atual, object esperado)
    {
        if (atual is int a && esperado is int b)
            return a == b;

        return string.Equals(
            Convert.ToString(atual, System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToString(esperado, System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
    }

    private ActionItem? MontarServico(ServiceTweak catalogo)
    {
        var atual = _servicos.GetService(catalogo.Nome);

        // Serviço que não existe nesta instalação não vira linha na tela. O Fax
        // sumiu de boa parte das instalações do Windows 11, e listar um serviço
        // inexistente só gera dúvida.
        if (atual is null)
            return null;

        // A blindagem central manda mais que o catalogo: se o SafetyGuard diz
        // que o servico e intocavel, nem importa o que esta escrito aqui
        // (regra 6).
        var veredito = _guarda.CheckService(catalogo.Nome, apenasPausar: false);
        var protegido = catalogo.Protegido || veredito.Protegido;

        var jaNoAlvo = atual.StartMode == catalogo.Sugerido;

        var bloqueado = protegido
                        || catalogo.SomenteExplicar
                        || jaNoAlvo
                        || !CoreServices.RodandoComoAdministrador();

        var motivo = veredito.Protegido
            ? veredito.Explicacao
            : protegido
            ? catalogo.Explicacao
            : catalogo.SomenteExplicar
                ? "Está aqui para explicar por que não vale mexer."
                : jaNoAlvo
                    ? $"Já está em {ServiceCatalog.TextoDoModo(catalogo.Sugerido)}."
                    : !CoreServices.RodandoComoAdministrador()
                        ? "Precisa de privilégios de administrador."
                        : null;

        var rotulo = protegido ? "Protegido"
            : catalogo.SomenteExplicar ? "Só informativo"
            : jaNoAlvo ? "Já está assim"
            : "Precisa de admin";

        var acao = protegido || catalogo.SomenteExplicar
            ? "não mexer"
            : $"passar de {ServiceCatalog.TextoDoModo(atual.StartMode)} para {ServiceCatalog.TextoDoModo(catalogo.Sugerido)}";

        return new ActionItem
        {
            Id = PrefixoServico + catalogo.Nome,
            Categoria = ServiceCatalog.Categoria,
            Titulo = catalogo.Titulo,
            Descricao = $"{catalogo.Nome} · {ServiceCatalog.TextoDoModo(atual.StartMode)}"
                      + (atual.IsRunning ? ", rodando agora" : ", parado")
                      + $" · {acao} — {catalogo.Explicacao}",
            Risco = catalogo.Risco,
            GanhoEstimado = ServiceCatalog.TextoDoModo(atual.StartMode),
            PreMarcado = false,
            Bloqueado = bloqueado,
            MotivoBloqueio = motivo,
            RotuloBloqueio = rotulo,
            ComoDesfazer = "Voltar o tipo de inicialização anterior por aqui, ou em services.msc. "
                         + "O estado antigo fica guardado no histórico.",
            Payload = new ServicoEstado(catalogo, atual)
        };
    }

    // ==================================================================
    // Aplicação
    // ==================================================================

    public Task<ApplyResult> ApplyAsync(IReadOnlyList<string> itemIds, bool dryRun, CancellationToken ct)
        => Task.Run(() => Aplicar(itemIds, dryRun, ct), ct);

    private ApplyResult Aplicar(IReadOnlyList<string> itemIds, bool dryRun, CancellationToken ct)
    {
        var acoes = new List<AppliedAction>();
        var reiniciar = false;

        foreach (var id in itemIds)
        {
            ct.ThrowIfCancellationRequested();

            if (id.StartsWith(PrefixoTweak, StringComparison.Ordinal))
            {
                var tweak = TweakCatalog.PorId(id[PrefixoTweak.Length..]);

                if (tweak is null)
                {
                    acoes.Add(new AppliedAction(id, false, "Ajuste não está no catálogo.", null));
                    continue;
                }

                if (tweak.Tipo == TweakKind.SomenteLeitura)
                {
                    acoes.Add(new AppliedAction(id, false,
                        $"{tweak.Nome} é só informativo: o GameBoost não altera isto.", null));
                    continue;
                }

                var resultado = AplicarTweak(tweak, id, dryRun);
                acoes.Add(resultado);

                if (resultado.Sucesso && tweak.ExigeReboot)
                    reiniciar = true;
            }
            else if (id.StartsWith(PrefixoServico, StringComparison.Ordinal))
            {
                acoes.Add(AplicarServico(id[PrefixoServico.Length..], id, dryRun));
            }
            else
            {
                acoes.Add(new AppliedAction(id, false, "Item desconhecido.", null));
            }
        }

        if (!dryRun && reiniciar)
            PrecisaReiniciar = true;

        var ok = acoes.Count(a => a.Sucesso);

        return new ApplyResult
        {
            ModuloId = ModuloId,
            Acoes = acoes,
            DryRun = dryRun,
            Resumo = dryRun
                ? $"Simulação: {ok} de {acoes.Count} ajustes seriam aplicados."
                : $"{ok} de {acoes.Count} ajustes aplicados."
                  + (reiniciar ? " Alguns só passam a valer depois de reiniciar." : string.Empty),

            // Regra 4: tweak de registro não tem ganho mensurável na hora. Dizer
            // "+12 FPS" aqui seria invenção, então o campo fica honesto.
            GanhoMedido = reiniciar
                ? "O efeito só aparece depois de reiniciar."
                : "Ajustes de sistema não têm ganho mensurável no instante em que são aplicados."
        };
    }

    private AppliedAction AplicarTweak(TweakDefinition tweak, string itemId, bool dryRun)
    {
        if (tweak.Tipo == TweakKind.Powercfg)
            return AplicarPowercfg(tweak, itemId, dryRun);

        var gravados = new List<string>();

        foreach (var valor in tweak.Valores)
        {
            try
            {
                var anterior = _registro.GetValue(valor.Root, valor.SubKey, valor.ValueName);

                if (dryRun)
                {
                    gravados.Add($"{valor.ValueName}: {anterior ?? "(não existe)"} vira {valor.ValorLigado}");
                    continue;
                }

                // Regra 1: o ChangeRecord vem antes da escrita, sempre.
                _backup.Registrar(new ChangeRecord
                {
                    Modulo = ModuloId,
                    Tipo = ChangeType.Registry,
                    Alvo = valor.SubKey,
                    SubAlvo = valor.ValueName,
                    ValorAnterior = anterior?.ToString(),
                    ValorNovo = valor.ValorLigado.ToString(),
                    ValorAnteriorExistia = anterior is not null,
                    Extras =
                    {
                        ["root"] = valor.Root.ToString(),
                        ["kind"] = valor.Kind.ToString(),
                        ["nome"] = tweak.Nome,
                        ["tweak"] = tweak.Id
                    }
                });

                _registro.SetValue(valor.Root, valor.SubKey, valor.ValueName, valor.ValorLigado, valor.Kind);
                gravados.Add(valor.ValueName);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                _log.Error(ModuloId, "Aplicar", tweak.Id, ex.Message, ex);

                // Um valor falhou no meio de um tweak de vários. Os anteriores
                // já têm ChangeRecord, então a reversão os desfaz; o que não dá
                // é declarar sucesso.
                return new AppliedAction(itemId, false, $"{tweak.Nome}: {ex.Message}", null);
            }
        }

        _log.Info(ModuloId, dryRun ? "Aplicar (dry-run)" : "Aplicar", tweak.Id, string.Join(", ", gravados));

        return new AppliedAction(itemId, true,
            dryRun
                ? $"{tweak.Nome}: {string.Join("; ", gravados)}"
                : $"{tweak.Nome} aplicado."
                  + (tweak.ExigeReboot ? " Só vale depois de reiniciar." : string.Empty),
            null);
    }

    private AppliedAction AplicarPowercfg(TweakDefinition tweak, string itemId, bool dryRun)
    {
        if (tweak.ComandoLigar is null || tweak.ComandoDesligar is null)
            return new AppliedAction(itemId, false, $"{tweak.Nome}: catálogo sem comando.", null);

        if (dryRun)
            return new AppliedAction(itemId, true, $"Rodaria powercfg {tweak.ComandoLigar}.", null);

        if (!CoreServices.RodandoComoAdministrador())
            return new AppliedAction(itemId, false, $"{tweak.Nome} precisa de administrador.", null);

        try
        {
            _backup.Registrar(new ChangeRecord
            {
                Modulo = ModuloId,
                Tipo = ChangeType.Power,
                Alvo = "powercfg",
                SubAlvo = tweak.Id,
                ValorAnterior = tweak.ComandoDesligar,
                ValorNovo = tweak.ComandoLigar,
                Extras =
                {
                    // O engine de rollback lê isto e roda o comando inverso.
                    ["powercfg"] = tweak.ComandoDesligar,
                    ["nome"] = tweak.Nome
                }
            });

            using var processo = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = tweak.ComandoLigar,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (processo is null)
                return new AppliedAction(itemId, false, "Não foi possível iniciar o powercfg.", null);

            processo.WaitForExit(30_000);

            return processo.ExitCode == 0
                ? new AppliedAction(itemId, true, $"{tweak.Nome} aplicado.", null)
                : new AppliedAction(itemId, false, $"powercfg saiu com código {processo.ExitCode}.", null);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.Error(ModuloId, "Powercfg", tweak.Id, ex.Message, ex);
            return new AppliedAction(itemId, false, $"{tweak.Nome}: {ex.Message}", null);
        }
    }

    private AppliedAction AplicarServico(string nome, string itemId, bool dryRun)
    {
        var catalogo = ServiceCatalog.PorNome(nome);

        if (catalogo is null)
            return new AppliedAction(itemId, false, "Serviço não está no catálogo.", null);

        // Dupla checagem: a UI já bloqueia, mas um id pode chegar pela CLI.
        var veredito = _guarda.CheckService(nome, apenasPausar: false);

        if (veredito.Protegido)
            return new AppliedAction(itemId, false, $"{catalogo.Titulo} é protegido: {veredito.Explicacao}", null);

        if (catalogo.Protegido)
            return new AppliedAction(itemId, false, $"{catalogo.Titulo} é protegido: {catalogo.Explicacao}", null);

        if (catalogo.SomenteExplicar)
            return new AppliedAction(itemId, false, $"{catalogo.Titulo} está na lista só para explicar.", null);

        var atual = _servicos.GetService(nome);

        if (atual is null)
            return new AppliedAction(itemId, false, $"{nome} não existe nesta instalação.", null);

        if (atual.StartMode == catalogo.Sugerido)
            return new AppliedAction(itemId, true,
                $"{catalogo.Titulo} já está em {ServiceCatalog.TextoDoModo(catalogo.Sugerido)}.", null);

        if (dryRun)
            return new AppliedAction(itemId, true,
                $"{catalogo.Titulo}: {ServiceCatalog.TextoDoModo(atual.StartMode)} viraria "
              + $"{ServiceCatalog.TextoDoModo(catalogo.Sugerido)}.", null);

        if (!CoreServices.RodandoComoAdministrador())
            return new AppliedAction(itemId, false, $"{catalogo.Titulo} precisa de administrador.", null);

        var registro = _backup.Registrar(new ChangeRecord
        {
            Modulo = ModuloId,
            Tipo = ChangeType.Service,
            Alvo = nome,
            SubAlvo = null,
            ValorAnterior = atual.StartMode.ToString(),
            ValorNovo = catalogo.Sugerido.ToString(),
            Extras =
            {
                ["estavaRodando"] = atual.IsRunning.ToString(),
                ["nome"] = catalogo.Titulo
            }
        });

        var ok = _servicos.SetStartMode(nome, catalogo.Sugerido);

        if (!ok)
            return new AppliedAction(itemId, false, $"Não foi possível alterar {catalogo.Titulo}.", registro.Id);

        // Desativar o tipo de inicialização não para o serviço que já está
        // rodando: sem isto, o usuário aplica, não vê mudança nenhuma e conclui
        // que a ferramenta não fez nada.
        var detalhe = $"{catalogo.Titulo} agora é {ServiceCatalog.TextoDoModo(catalogo.Sugerido)}.";

        if (catalogo.Sugerido == ServiceStartMode.Disabled && atual.IsRunning)
        {
            var parou = _servicos.Stop(nome, TimeSpan.FromSeconds(30));
            detalhe += parou ? " Serviço parado." : " O serviço só para de vez no próximo boot.";
        }

        _log.Info(ModuloId, "Servico", nome, detalhe);

        return new AppliedAction(itemId, true, detalhe, registro.Id);
    }

    // ==================================================================
    // Reversão
    // ==================================================================

    public Task<ApplyResult> RevertAsync(IReadOnlyList<string> changeIds, bool dryRun, CancellationToken ct)
        => Task.Run(() => Reverter(changeIds, dryRun), ct);

    private ApplyResult Reverter(IReadOnlyList<string> changeIds, bool dryRun)
    {
        // Sem ids explícitos, reverte tudo que este módulo aplicou e ainda está
        // pendente. Não toca no que os outros módulos fizeram.
        var alvos = changeIds.Count > 0
            ? changeIds
            : _backup.Pendentes.Where(r => r.Modulo == ModuloId).Select(r => r.Id).ToList();

        if (alvos.Count == 0)
        {
            return new ApplyResult
            {
                ModuloId = ModuloId,
                DryRun = dryRun,
                Resumo = "Não há ajuste deste módulo para desfazer."
            };
        }

        var resultados = _rollback.Reverter(alvos, dryRun);

        var acoes = resultados
            .Select(r => new AppliedAction(r.ChangeId, r.Sucesso, r.Detalhe, r.ChangeId))
            .ToList();

        var ok = acoes.Count(a => a.Sucesso);

        return new ApplyResult
        {
            ModuloId = ModuloId,
            Acoes = acoes,
            DryRun = dryRun,
            Resumo = dryRun
                ? $"Simulação: {ok} de {acoes.Count} ajustes voltariam ao estado anterior."
                : $"{ok} de {acoes.Count} ajustes revertidos.",
            GanhoMedido = string.Empty
        };
    }
}
