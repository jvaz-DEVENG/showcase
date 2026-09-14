using System.Diagnostics;
using System.Net.NetworkInformation;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Native;
using GameBoost.Core.State;

namespace GameBoost.Core.Modules.Network;

/// <summary>Quem está falando com a internet agora, agrupado por processo.</summary>
public sealed record ConsumidorDeRede(int ProcessId, string Nome, int Conexoes, string PrincipalDestino);

/// <summary>
/// Rede (seção 5.9).
///
/// As ações daqui são de três naturezas bem diferentes, e a tela precisa deixar
/// isso claro:
///
/// - **Limpar o cache de DNS** é inofensivo e instantâneo.
/// - **Trocar o DNS do adaptador** é reversível de verdade: o valor anterior vai
///   para o ChangeRecord e volta com um clique.
/// - **Resetar o Winsock** derruba a rede até reiniciar e não tem desfazer. Vem
///   com risco Alto e o aviso por extenso.
/// </summary>
public sealed class NetworkModule : IModule
{
    public const string ModuloId = "network";

    private const string PrefixoDns = "dns:";
    private const string ItemFlush = "rede:flush-dns";
    private const string ItemWinsock = "rede:winsock";
    private const string ItemTeredo = "rede:teredo";

    private const string ChaveTcpip6 = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters";
    private const string ChaveTeredo = @"SYSTEM\CurrentControlSet\Services\iphlpsvc\Teredo";

    private readonly NetworkDiagnostics _diagnostico;
    private readonly NatDiagnostics _nat;
    private readonly SpeedTest _velocidade;
    private readonly IRegistryService _registro;
    private readonly IStateBackup _backup;
    private readonly IRollbackEngine _rollback;
    private readonly IProcessService _processos;
    private readonly IGameBoostLogger _log;
    private readonly IClock _relogio;

    public NetworkModule(
        NetworkDiagnostics diagnostico,
        NatDiagnostics nat,
        SpeedTest velocidade,
        IRegistryService registro,
        IStateBackup backup,
        IRollbackEngine rollback,
        IProcessService processos,
        IGameBoostLogger log,
        IClock relogio)
    {
        _diagnostico = diagnostico;
        _nat = nat;
        _velocidade = velocidade;
        _registro = registro;
        _backup = backup;
        _rollback = rollback;
        _processos = processos;
        _log = log;
        _relogio = relogio;
    }

    public string Id => ModuloId;
    public string Nome => "Rede";
    public string Descricao => "Latência, jitter, perda e DNS — com o que dá para mudar e o que não vale mexer.";

    /// <summary>Última medição, para a tela desenhar as tabelas.</summary>
    public IReadOnlyList<MedicaoPing> UltimoPing { get; private set; } = Array.Empty<MedicaoPing>();
    public IReadOnlyList<MedicaoDns> UltimoDns { get; private set; } = Array.Empty<MedicaoDns>();
    public IReadOnlyList<ConsumidorDeRede> UltimosConsumidores { get; private set; } = Array.Empty<ConsumidorDeRede>();
    public EstadoWifi? UltimoWifi { get; private set; }
    public DiagnosticoDeNat? UltimoNat { get; private set; }
    public ResultadoDeVelocidade? UltimaVelocidade { get; private set; }

    /// <summary>O teste de velocidade gasta banda, então é opt-in.</summary>
    public bool VelocidadePermitida => _velocidade.Permitido;

    // ==================================================================
    // Varredura
    // ==================================================================

    public async Task<ScanResult> ScanAsync(IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new ModuleProgress("Medindo latência", 10));
        UltimoPing = await _diagnostico.MedirLatenciaAsync(ct);

        progress?.Report(new ModuleProgress("Comparando servidores de DNS", 45));
        UltimoDns = await _diagnostico.CompararDnsAsync(ct);

        progress?.Report(new ModuleProgress("Descobrindo o tipo de NAT", 60));
        UltimoNat = await _nat.DiagnosticarAsync(ct);

        progress?.Report(new ModuleProgress("Vendo quem está usando a rede", 80));
        UltimosConsumidores = LerConsumidores();
        UltimoWifi = _diagnostico.LerWifi();

        progress?.Report(new ModuleProgress("Pronto", 100));

        var itens = MontarItens();

        return new ScanResult
        {
            ModuloId = ModuloId,
            Itens = itens,
            Momento = _relogio.Now,
            Resumo = MontarResumo(),
            Avisos = MontarAvisos()
        };
    }

    private string MontarResumo()
    {
        var internet = UltimoPing.FirstOrDefault(p => p.Rotulo.StartsWith("Internet", StringComparison.Ordinal) && p.Respondeu);

        if (internet is null)
            return "Nenhum dos alvos respondeu ao ping. Pode ser a rede, ou pode ser firewall bloqueando ICMP.";

        var texto = $"Latência até a internet: {internet.MediaMs} ms ({internet.Leitura}), "
                  + $"jitter {internet.JitterMs} ms, {internet.PerdaPercentual}% de perda.";

        if (UltimoNat is not null && UltimoNat.Tipo != TipoDeNat.NaoDetectado)
            texto += $" NAT {UltimoNat.NomeCurto}.";

        if (UltimaVelocidade is { Funcionou: true })
            texto += $" Download {UltimaVelocidade.DownloadMbps} Mbps, upload {UltimaVelocidade.UploadMbps} Mbps.";

        var melhor = UltimoDns.Where(d => d.Respondeu && !d.EmUso).OrderBy(d => d.MediaMs).FirstOrDefault();
        var atual = UltimoDns.FirstOrDefault(d => d.EmUso && d.Respondeu);

        if (melhor is not null && atual is not null && melhor.MediaMs < atual.MediaMs - 5)
            texto += $" O DNS da {melhor.Nome} responde {Math.Round(atual.MediaMs - melhor.MediaMs, 1)} ms mais rápido que o seu.";

        return texto;
    }

    private IReadOnlyList<string> MontarAvisos()
    {
        var avisos = new List<string>();

        var gateway = UltimoPing.FirstOrDefault(p => p.Rotulo == "Seu roteador");

        // Jitter alto até o próprio roteador é o diagnóstico mais útil desta
        // tela: o problema está dentro de casa, e trocar de DNS ou de provedor
        // não vai resolver.
        if (gateway is { Respondeu: true, JitterMs: > 5 })
        {
            avisos.Add($"O jitter até o seu próprio roteador é de {gateway.JitterMs} ms. "
                     + "Quando isso acontece o problema está na sua casa, não no provedor: "
                     + "costuma ser Wi-Fi congestionado ou cabo ruim.");
        }

        if (UltimoWifi is not null)
        {
            var texto = $"Você está no Wi-Fi ({UltimoWifi.Ssid}, {UltimoWifi.Padrao}, {UltimoWifi.Banda}, "
                      + $"sinal {UltimoWifi.SinalPercentual}%).";

            if (UltimoWifi.Banda.StartsWith("2,4", StringComparison.Ordinal))
                texto += " A faixa de 2,4 GHz divide espaço com micro-ondas e com o Wi-Fi dos vizinhos. Se houver rede de 5 GHz, ela é melhor para jogo.";

            texto += " Para jogo competitivo, cabo continua sendo melhor que qualquer Wi-Fi.";
            avisos.Add(texto);
        }

        if (UltimoNat is not null)
        {
            avisos.Add(UltimoNat.Explicacao);

            if (UltimoNat.Tipo is TipoDeNat.Estrito or TipoDeNat.Moderado && !UltimoNat.UpnpDisponivel)
            {
                avisos.Add("Seu roteador não respondeu à busca por UPnP. Com NAT estrito e sem "
                         + "UPnP, a única saída é abrir as portas do jogo à mão no roteador. "
                         + "As portas de cada jogo estão em docs/PORTAS.md.");
            }
        }

        var emUso = UltimoDns.FirstOrDefault(d => d.EmUso);

        if (emUso is not null && emUso.Endereco.StartsWith("192.168.", StringComparison.Ordinal))
        {
            avisos.Add($"O DNS em uso ({emUso.Endereco}) é o seu próprio roteador, que guarda "
                     + "respostas em cache. A comparação abaixo favorece ele de propósito ou não: "
                     + "um nome que ele já tem em cache responde em quase zero, um que não tem "
                     + "custa a ida até o provedor. Os números são reais, mas não são uma "
                     + "comparação limpa entre servidores.");
        }

        if (UltimosConsumidores.Count > 0)
        {
            avisos.Add("A lista de quem está usando a rede mostra número de conexões abertas, "
                     + "não velocidade. Medir banda por processo exigiria um coletor de eventos "
                     + "rodando o tempo todo, e isso custaria mais CPU do que o problema vale.");
        }

        return avisos;
    }

    private IReadOnlyList<ConsumidorDeRede> LerConsumidores()
    {
        try
        {
            var conexoes = TcpTable.Listar().Where(c => c.Estado == "conectada").ToList();

            if (conexoes.Count == 0)
                return Array.Empty<ConsumidorDeRede>();

            var nomes = _processos.GetProcesses()
                .GroupBy(p => p.Pid)
                .ToDictionary(g => g.Key, g => g.First().Name);

            // Agrupar por nome, nao por PID. Um editor de codigo sobe um
            // processo de servidor de linguagem por projeto aberto, e por PID
            // a lista virava a mesma linha repetida tres vezes, parecendo bug.
            return conexoes
                .Select(c => new
                {
                    Nome = nomes.TryGetValue(c.ProcessId, out var nome) ? nome : $"PID {c.ProcessId}",
                    c.ProcessId,
                    Destino = c.Remoto.ToString()
                })
                .GroupBy(c => c.Nome, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ConsumidorDeRede(
                    g.First().ProcessId,
                    g.Select(x => x.ProcessId).Distinct().Count() is var quantos && quantos > 1
                        ? $"{g.Key} ({quantos} processos)"
                        : g.Key,
                    g.Count(),
                    g.GroupBy(x => x.Destino)
                     .OrderByDescending(x => x.Count())
                     .First().Key))
                .OrderByDescending(c => c.Conexoes)
                .Take(10)
                .ToList();
        }
        catch (Exception ex) when (ex is InvalidOperationException or OutOfMemoryException)
        {
            _log.Warn(ModuloId, "Conexoes", null, ex.Message);
            return Array.Empty<ConsumidorDeRede>();
        }
    }

    private IReadOnlyList<ActionItem> MontarItens()
    {
        var itens = new List<ActionItem>
        {
            new()
            {
                Id = ItemFlush,
                Categoria = "Manutenção",
                Titulo = "Limpar o cache de DNS",
                Descricao = "Apaga os nomes que o Windows memorizou. Resolve o caso específico de "
                          + "um site ou servidor que mudou de endereço e continua abrindo no endereço "
                          + "velho. Não acelera a internet.",
                Risco = RiskLevel.Baixo,
                GanhoEstimado = "efeito imediato, sem risco",
                PreMarcado = false,
                ComoDesfazer = "Não precisa desfazer: o cache se refaz sozinho na próxima consulta."
            }
        };

        foreach (var dns in UltimoDns.Where(d => !d.EmUso && d.Respondeu))
        {
            var atual = UltimoDns.FirstOrDefault(d => d.EmUso && d.Respondeu);

            var comparacao = atual is null
                ? "não deu para medir o seu DNS atual para comparar"
                : dns.MediaMs < atual.MediaMs
                    ? $"responde {Math.Round(atual.MediaMs - dns.MediaMs, 1)} ms mais rápido que o seu ({atual.MediaMs} ms)"
                    : $"responde {Math.Round(dns.MediaMs - atual.MediaMs, 1)} ms mais devagar que o seu ({atual.MediaMs} ms)";

            itens.Add(new ActionItem
            {
                Id = PrefixoDns + dns.Endereco,
                Categoria = "Servidor de DNS",
                Titulo = $"Usar o DNS da {dns.Nome} ({dns.Endereco})",
                Descricao = $"Medido agora: {dns.MediaMs} ms, {comparacao}. "
                          + "DNS mais rápido faz página abrir mais rápido; **não muda o ping do jogo**, "
                          + "porque o jogo resolve o nome uma vez e depois fala direto com o servidor.",
                Risco = RiskLevel.Baixo,
                GanhoEstimado = $"{dns.MediaMs} ms",
                PreMarcado = false,
                ComoDesfazer = "Voltar ao DNS anterior por aqui. O valor antigo, inclusive o "
                             + "automático do provedor, fica guardado no histórico.",
                Payload = dns
            });
        }

        if (UltimoNat?.Teredo == EstadoTeredo.Desativado)
        {
            itens.Add(new ActionItem
            {
                Id = ItemTeredo,
                Categoria = "Conectividade",
                Titulo = "Reativar o Teredo",
                Descricao = "O Teredo está desligado nesta máquina. Ele é o túnel que o "
                          + "multiplayer de Xbox e de Game Pass no PC usa para achar outros "
                          + "jogadores; sem ele, esses jogos dão erro de rede e não entram em "
                          + "partida. Muito guia de otimização manda desativar, e é justamente "
                          + "isso que quebra o multiplayer de quem joga Game Pass. Se você não "
                          + "joga nada da Microsoft, deixar desligado não faz falta.",
                Risco = RiskLevel.Baixo,
                GanhoEstimado = "desligado agora",
                PreMarcado = false,
                ComoDesfazer = "Desligar de novo por aqui. O valor anterior das duas chaves "
                             + "fica no histórico."
            });
        }

        itens.Add(new ActionItem
        {
            Id = ItemWinsock,
            Categoria = "Manutenção",
            Titulo = "Resetar o Winsock",
            Descricao = "Devolve a pilha de rede do Windows ao estado de fábrica. Serve para "
                      + "máquina que ficou sem internet depois de desinstalar VPN ou antivírus. "
                      + "**Derruba a rede até você reiniciar**, apaga configurações de adaptador "
                      + "e não tem desfazer automático. Não use como manutenção de rotina.",
            Risco = RiskLevel.Alto,
            GanhoEstimado = "só para consertar rede quebrada",
            PreMarcado = false,
            ComoDesfazer = "Não há desfazer. Depois de reiniciar, reconfigure VPN e "
                         + "proxy que você usava."
        });

        return itens;
    }

    // ==================================================================
    // Aplicação
    // ==================================================================

    public Task<ApplyResult> ApplyAsync(IReadOnlyList<string> itemIds, bool dryRun, CancellationToken ct)
        => Task.Run(() => Aplicar(itemIds, dryRun, ct), ct);

    private ApplyResult Aplicar(IReadOnlyList<string> itemIds, bool dryRun, CancellationToken ct)
    {
        var acoes = new List<AppliedAction>();

        foreach (var id in itemIds)
        {
            ct.ThrowIfCancellationRequested();

            if (id == ItemFlush)
                acoes.Add(LimparDns(dryRun));
            else if (id == ItemWinsock)
                acoes.Add(ResetarWinsock(dryRun));
            else if (id == ItemTeredo)
                acoes.Add(ReativarTeredo(dryRun));
            else if (id.StartsWith(PrefixoDns, StringComparison.Ordinal))
                acoes.Add(TrocarDns(id[PrefixoDns.Length..], id, dryRun));
            else
                acoes.Add(new AppliedAction(id, false, "Item desconhecido.", null));
        }

        var ok = acoes.Count(a => a.Sucesso);

        return new ApplyResult
        {
            ModuloId = ModuloId,
            Acoes = acoes,
            DryRun = dryRun,
            Resumo = dryRun
                ? $"Simulação: {ok} de {acoes.Count} ações seriam executadas."
                : $"{ok} de {acoes.Count} ações executadas.",
            GanhoMedido = "Varra de novo para medir o efeito: a tela mede latência e DNS na hora."
        };
    }

    private AppliedAction LimparDns(bool dryRun)
    {
        if (dryRun)
            return new AppliedAction(ItemFlush, true, "Limparia o cache de DNS.", null);

        var ok = ToolsNative.LimparCacheDeDns();

        _log.Info(ModuloId, "FlushDns", null, ok ? "cache limpo" : "falhou");

        return new AppliedAction(ItemFlush, ok,
            ok ? "Cache de DNS limpo." : "Não foi possível limpar o cache de DNS.", null);
    }

    private AppliedAction ResetarWinsock(bool dryRun)
    {
        if (dryRun)
            return new AppliedAction(ItemWinsock, true,
                "Rodaria netsh winsock reset. A rede cairia até reiniciar.", null);

        if (!CoreServices.RodandoComoAdministrador())
            return new AppliedAction(ItemWinsock, false, "Resetar o Winsock precisa de administrador.", null);

        try
        {
            using var processo = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = "winsock reset",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (processo is null)
                return new AppliedAction(ItemWinsock, false, "Não foi possível iniciar o netsh.", null);

            processo.WaitForExit(60_000);

            // Nenhum ChangeRecord aqui, e de propósito: não existe estado
            // anterior para guardar. O texto do item avisa disso antes.
            _log.Warn(ModuloId, "Winsock", null, $"reset executado, código {processo.ExitCode}");

            return processo.ExitCode == 0
                ? new AppliedAction(ItemWinsock, true,
                    "Winsock resetado. **Reinicie o computador** para a rede voltar ao normal.", null)
                : new AppliedAction(ItemWinsock, false, $"netsh saiu com código {processo.ExitCode}.", null);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.Error(ModuloId, "Winsock", null, ex.Message, ex);
            return new AppliedAction(ItemWinsock, false, ex.Message, null);
        }
    }

    /// <summary>
    /// Reativa o Teredo apagando as duas chaves que o desligam. Apagar, e nao
    /// gravar zero: o padrao do Windows e o valor **ausente**, e deixar um zero
    /// escrito congelaria a configuracao num estado que o Windows nao
    /// escolheria sozinho.
    /// </summary>
    private AppliedAction ReativarTeredo(bool dryRun)
    {
        var componentes = _registro.GetValue(RegistryRoot.LocalMachine, ChaveTcpip6, "DisabledComponents");
        var tipo = _registro.GetValue(RegistryRoot.LocalMachine, ChaveTeredo, "Type");

        if (dryRun)
        {
            return new AppliedAction(ItemTeredo, true,
                $"Apagaria DisabledComponents (agora {componentes?.ToString() ?? "ausente"}) e "
              + $"Type do Teredo (agora {tipo?.ToString() ?? "ausente"}).", null);
        }

        if (!CoreServices.RodandoComoAdministrador())
            return new AppliedAction(ItemTeredo, false, "Reativar o Teredo precisa de administrador.", null);

        try
        {
            if (componentes is not null)
            {
                _backup.Registrar(new ChangeRecord
                {
                    Modulo = ModuloId,
                    Tipo = ChangeType.Registry,
                    Alvo = ChaveTcpip6,
                    SubAlvo = "DisabledComponents",
                    ValorAnterior = componentes.ToString(),
                    ValorNovo = null,
                    ValorAnteriorExistia = true,
                    Extras =
                    {
                        ["root"] = RegistryRoot.LocalMachine.ToString(),
                        ["kind"] = RegistryValueKindLite.DWord.ToString(),
                        ["nome"] = "Tuneis IPv6 (Teredo)"
                    }
                });

                _registro.DeleteValue(RegistryRoot.LocalMachine, ChaveTcpip6, "DisabledComponents");
            }

            if (tipo is not null)
            {
                _backup.Registrar(new ChangeRecord
                {
                    Modulo = ModuloId,
                    Tipo = ChangeType.Registry,
                    Alvo = ChaveTeredo,
                    SubAlvo = "Type",
                    ValorAnterior = tipo.ToString(),
                    ValorNovo = null,
                    ValorAnteriorExistia = true,
                    Extras =
                    {
                        ["root"] = RegistryRoot.LocalMachine.ToString(),
                        ["kind"] = RegistryValueKindLite.DWord.ToString(),
                        ["nome"] = "Estado do Teredo"
                    }
                });

                _registro.DeleteValue(RegistryRoot.LocalMachine, ChaveTeredo, "Type");
            }

            _log.Info(ModuloId, "Teredo", null, "reativado");

            return new AppliedAction(ItemTeredo, true,
                "Teredo reativado. Reinicie o computador para o tunel subir.", null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _log.Error(ModuloId, "Teredo", null, ex.Message, ex);
            return new AppliedAction(ItemTeredo, false, ex.Message, null);
        }
    }

    /// <summary>
    /// Teste de velocidade, separado da varredura porque gasta 33 MB de banda.
    /// Os outros testes desta tela (ping, DNS, STUN) mandam pacotes de dezenas
    /// de bytes; este transfere um arquivo, e num plano com franquia isso e
    /// dinheiro do usuario. Por isso ele e um botao a parte e depende da
    /// permissao explicita em Configuracoes.
    /// </summary>
    public async Task<ResultadoDeVelocidade> MedirVelocidadeAsync(IProgress<string>? progresso, CancellationToken ct)
    {
        UltimaVelocidade = await _velocidade.MedirAsync(progresso, ct);
        return UltimaVelocidade;
    }

    private AppliedAction TrocarDns(string servidor, string itemId, bool dryRun)
    {
        var adaptador = AdaptadorPrincipal();

        if (adaptador is null)
            return new AppliedAction(itemId, false, "Nenhum adaptador de rede ativo com gateway.", null);

        var anteriores = adaptador.GetIPProperties().DnsAddresses
            .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(a => a.ToString())
            .ToList();

        var texto = anteriores.Count == 0 ? "automático (DHCP)" : string.Join(", ", anteriores);

        if (dryRun)
            return new AppliedAction(itemId, true,
                $"{adaptador.Name}: DNS passaria de {texto} para {servidor}.", null);

        if (!CoreServices.RodandoComoAdministrador())
            return new AppliedAction(itemId, false, "Trocar o DNS precisa de administrador.", null);

        // Regra 1: o estado anterior vai para o histórico antes de qualquer
        // escrita — inclusive o caso "estava no automático", que é o mais fácil
        // de perder e o mais chato de reconstruir depois.
        var registro = _backup.Registrar(new ChangeRecord
        {
            Modulo = ModuloId,
            Tipo = ChangeType.Registry,
            Alvo = adaptador.Id,
            SubAlvo = "dns",
            ValorAnterior = anteriores.Count == 0 ? null : string.Join(",", anteriores),
            ValorNovo = servidor,
            ValorAnteriorExistia = anteriores.Count > 0,
            Extras =
            {
                ["adaptador"] = adaptador.Name,
                ["nome"] = $"DNS de {adaptador.Name}",
                ["netsh"] = "dns"
            }
        });

        var ok = DefinirDns(adaptador.Name, servidor);

        if (!ok)
            return new AppliedAction(itemId, false, $"Não foi possível trocar o DNS de {adaptador.Name}.", registro.Id);

        // Sem limpar o cache, o usuário troca de DNS e continua vendo as
        // respostas do antigo por horas.
        ToolsNative.LimparCacheDeDns();

        _log.Info(ModuloId, "TrocarDns", adaptador.Name, $"{texto} -> {servidor}");

        return new AppliedAction(itemId, true,
            $"DNS de {adaptador.Name} agora é {servidor}. O anterior era {texto} e está no histórico.",
            registro.Id);
    }

    private static NetworkInterface? AdaptadorPrincipal()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                     && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                     && n.GetIPProperties().GatewayAddresses.Count > 0)
            .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1)
            .FirstOrDefault();

    /// <summary>
    /// Troca o DNS pelo netsh. O spec sugeria o WMI
    /// (`SetDNSServerSearchOrder`), mas aquele método é da classe
    /// `Win32_NetworkAdapterConfiguration`, que está congelada desde o Windows 8
    /// e falha em adaptador configurado por DHCP em algumas máquinas. O netsh é
    /// a via que o próprio Windows usa.
    /// </summary>
    internal static bool DefinirDns(string nomeDoAdaptador, string? servidor)
    {
        var argumentos = servidor is null
            ? $"interface ipv4 set dnsservers name=\"{nomeDoAdaptador}\" source=dhcp"
            : $"interface ipv4 set dnsservers name=\"{nomeDoAdaptador}\" static {servidor} primary";

        try
        {
            using var processo = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = argumentos,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (processo is null)
                return false;

            processo.WaitForExit(30_000);
            return processo.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    // ==================================================================
    // Reversão
    // ==================================================================

    public Task<ApplyResult> RevertAsync(IReadOnlyList<string> changeIds, bool dryRun, CancellationToken ct)
        => Task.Run(() => Reverter(changeIds, dryRun), ct);

    private ApplyResult Reverter(IReadOnlyList<string> changeIds, bool dryRun)
    {
        var pendentes = _backup.Pendentes.Where(r => r.Modulo == ModuloId).ToList();

        var alvos = changeIds.Count > 0
            ? pendentes.Where(r => changeIds.Contains(r.Id)).ToList()
            : pendentes;

        if (alvos.Count == 0)
        {
            return new ApplyResult
            {
                ModuloId = ModuloId,
                DryRun = dryRun,
                Resumo = "Não há alteração de rede para desfazer."
            };
        }

        var acoes = new List<AppliedAction>();

        foreach (var registro in Enumerable.Reverse(alvos))
        {
            // A troca de DNS não volta pelo RollbackEngine: ele restaura valor
            // de registro, e aqui o que precisa voltar é a configuração do
            // adaptador, inclusive o caso "voltar para automático".
            if (registro.Extras.TryGetValue("netsh", out var tipo) && tipo == "dns")
            {
                acoes.Add(ReverterDns(registro, dryRun));
                continue;
            }

            var resultado = _rollback.Reverter(new[] { registro.Id }, dryRun).FirstOrDefault();

            if (resultado is not null)
                acoes.Add(new AppliedAction(resultado.ChangeId, resultado.Sucesso, resultado.Detalhe, resultado.ChangeId));
        }

        var ok = acoes.Count(a => a.Sucesso);

        return new ApplyResult
        {
            ModuloId = ModuloId,
            Acoes = acoes,
            DryRun = dryRun,
            Resumo = dryRun
                ? $"Simulação: {ok} de {acoes.Count} alterações voltariam."
                : $"{ok} de {acoes.Count} alterações revertidas."
        };
    }

    private AppliedAction ReverterDns(ChangeRecord registro, bool dryRun)
    {
        var adaptador = registro.Extras.TryGetValue("adaptador", out var nome) ? nome : null;

        if (adaptador is null)
            return new AppliedAction(registro.Id, false, "Histórico sem o nome do adaptador.", registro.Id);

        var voltarPara = registro.ValorAnteriorExistia
            ? registro.ValorAnterior?.Split(',').FirstOrDefault()
            : null;

        var texto = voltarPara ?? "automático (DHCP)";

        if (dryRun)
            return new AppliedAction(registro.Id, true, $"DNS de {adaptador} voltaria para {texto}.", registro.Id);

        if (!CoreServices.RodandoComoAdministrador())
            return new AppliedAction(registro.Id, false, "Reverter o DNS precisa de administrador.", registro.Id);

        var ok = DefinirDns(adaptador, voltarPara);

        if (ok)
        {
            ToolsNative.LimparCacheDeDns();
            _backup.MarcarRevertido(registro.Id, _relogio.Now);
        }

        return new AppliedAction(registro.Id, ok,
            ok ? $"DNS de {adaptador} voltou para {texto}." : $"Não foi possível reverter o DNS de {adaptador}.",
            registro.Id);
    }
}
