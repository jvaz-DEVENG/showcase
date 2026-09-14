using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBoost.Core.Modules;
using GameBoost.Core.Modules.Network;
using GameBoost.Core.State;

namespace GameBoost.App.ViewModels;

/// <summary>Uma linha da tabela de latência.</summary>
public sealed record LinhaDePing(string Alvo, string Latencia, string Jitter, string Perda, string Leitura);

/// <summary>Uma linha da tabela de DNS.</summary>
public sealed record LinhaDeDns(string Nome, string Endereco, string Tempo, bool EmUso, string Comparacao);

/// <summary>Uma linha de quem está conectado agora.</summary>
public sealed record LinhaDeConexao(string Processo, string Conexoes, string Destino);

/// <summary>
/// Página de rede (seção 5.9).
///
/// Diferente das outras páginas de módulo, esta tem conteúdo acima da lista: as
/// medições. A lista de ações sozinha não resolveria — "trocar de DNS" só faz
/// sentido depois de ver a tabela que mostra quanto cada um demora.
/// </summary>
public sealed partial class RedePageViewModel : ModulePageViewModel
{
    private readonly NetworkModule _modulo;

    public RedePageViewModel(NetworkModule modulo, IStateBackup backup)
        : base(modulo, backup)
    {
        _modulo = modulo;
    }

    public override string Nome => "Rede";
    public override string Titulo => "Rede";
    public override string Subtitulo => "Latência, jitter, perda e DNS medidos agora, na sua conexão.";
    public override string Icone => "\uE839";

    public ObservableCollection<LinhaDePing> Pings { get; } = new();
    public ObservableCollection<LinhaDeDns> Dns { get; } = new();
    public ObservableCollection<LinhaDeConexao> Conexoes { get; } = new();

    [ObservableProperty] private string? _wifi;
    [ObservableProperty] private bool _temMedicao;

    // ---- Bloco "Posso jogar online?" ----
    [ObservableProperty] private string _nat = string.Empty;
    [ObservableProperty] private string _natExplicacao = string.Empty;
    [ObservableProperty] private string _natSemaforo = "cinza";
    [ObservableProperty] private bool _temNat;
    public ObservableCollection<string> Causas { get; } = new();

    // ---- Bloco "Velocidade" ----
    [ObservableProperty] private string _velocidade = string.Empty;
    [ObservableProperty] private bool _medindoVelocidade;
    [ObservableProperty] private bool _velocidadePermitida;

    public string TextoDoBotaoDeVelocidade => VelocidadePermitida
        ? "Testar velocidade"
        : "Testar velocidade (precisa de permissão)";

    /// <summary>
    /// O teste de velocidade é um botão à parte, e não parte de Medir, porque
    /// ele transfere quase 100 MB. Quem está com franquia contada não pode ter
    /// isso acontecendo sem pedir.
    /// </summary>
    [RelayCommand]
    public async Task TestarVelocidadeAsync()
    {
        if (MedindoVelocidade)
            return;

        MedindoVelocidade = true;
        Velocidade = "Medindo...";

        try
        {
            var progresso = new Progress<string>(texto => Velocidade = texto);
            var resultado = await _modulo.MedirVelocidadeAsync(progresso, CancellationToken.None);

            Velocidade = resultado.Funcionou
                ? $"Download {resultado.DownloadMbps} Mbps · upload {resultado.UploadMbps} Mbps "
                + $"· {resultado.BytesBaixados / 1024 / 1024} MB em {resultado.Duracao.TotalSeconds:0} s"
                : resultado.Erro ?? "Não foi possível medir.";
        }
        catch (OperationCanceledException)
        {
            Velocidade = "Teste cancelado.";
        }
        finally
        {
            MedindoVelocidade = false;
            VelocidadePermitida = _modulo.VelocidadePermitida;
        }
    }

    public override void AoEntrar()
    {
        base.AoEntrar();
        VelocidadePermitida = _modulo.VelocidadePermitida;

        if (Velocidade.Length == 0)
        {
            Velocidade = VelocidadePermitida
                ? "Não medido ainda. O teste baixa e envia cerca de 100 MB."
                : "O teste de velocidade usa a internet e está desligado por padrão. "
                + "Ligue em Configurações, se quiser.";
        }
    }

    protected override void AposVarrer(ScanResult resultado)
    {
        Pings.Clear();
        Dns.Clear();
        Conexoes.Clear();

        foreach (var p in _modulo.UltimoPing)
        {
            Pings.Add(new LinhaDePing(
                $"{p.Rotulo} ({p.Alvo})",
                p.Respondeu ? $"{p.MediaMs} ms" : "sem resposta",
                p.Respondeu ? $"{p.JitterMs} ms" : "—",
                p.Respondeu ? $"{p.PerdaPercentual}%" : "—",
                p.Leitura));
        }

        var atual = _modulo.UltimoDns.FirstOrDefault(d => d.EmUso && d.Respondeu);

        foreach (var d in _modulo.UltimoDns)
        {
            var comparacao = !d.Respondeu
                ? "não respondeu"
                : d.EmUso || atual is null
                    ? string.Empty
                    : d.MediaMs < atual.MediaMs
                        ? $"{Math.Round(atual.MediaMs - d.MediaMs, 1)} ms mais rápido"
                        : $"{Math.Round(d.MediaMs - atual.MediaMs, 1)} ms mais lento";

            Dns.Add(new LinhaDeDns(
                d.Nome, d.Endereco,
                d.Respondeu ? $"{d.MediaMs} ms" : "—",
                d.EmUso, comparacao));
        }

        foreach (var c in _modulo.UltimosConsumidores)
            Conexoes.Add(new LinhaDeConexao(c.Nome, c.Conexoes.ToString(), c.PrincipalDestino));

        var w = _modulo.UltimoWifi;

        Wifi = w is null
            ? NetworkDiagnostics.ConectadoPorCabo()
                ? "Conectado por cabo. Para jogo competitivo é o melhor que existe."
                : null
            : $"Wi-Fi: {w.Ssid} · {w.Padrao} · {w.Banda} · sinal {w.SinalPercentual}%";

        TemMedicao = Pings.Count > 0;

        var n = _modulo.UltimoNat;
        Causas.Clear();

        if (n is null)
        {
            TemNat = false;
        }
        else
        {
            TemNat = true;
            Nat = n.NomeCurto;
            NatExplicacao = n.Explicacao;
            NatSemaforo = n.Semaforo;

            // As causas só aparecem quando **são** causa. Listar "UPnP: ligado"
            // num NAT aberto seria encher a tela de verde inútil e esconder a
            // linha que importa quando houver uma.
            if (n.Tipo == TipoDeNat.Cgnat)
                Causas.Add("CGNAT da operadora: o NAT que atrapalha não está na sua casa.");

            if (n.SaltosPrivados >= 2)
                Causas.Add($"{n.SaltosPrivados} saltos privados antes da internet: há dois roteadores em série.");

            if (!n.UpnpDisponivel && n.Tipo is TipoDeNat.Estrito or TipoDeNat.Moderado)
                Causas.Add("O roteador não respondeu por UPnP: os jogos não conseguem abrir porta sozinhos.");

            if (n.Teredo == EstadoTeredo.Desativado)
                Causas.Add("Teredo desativado: multiplayer de Xbox e Game Pass no PC não conecta.");

            if (n.PerfisDeFirewallAtivos.Count == 0)
                Causas.Add("Nenhum perfil do firewall do Windows está ligado. Isso não é otimização, é risco.");

            foreach (var ruim in _modulo.UltimoPing.Where(p => p.EhProblema))
            {
                Causas.Add($"{ruim.Rotulo}: jitter de {ruim.JitterMs} ms e {ruim.PerdaPercentual}% de perda. "
                         + "Acima de 15 ms de jitter ou 1% de perda o jogo engasga.");
            }

            if (Causas.Count == 0 && n.Tipo is TipoDeNat.Aberto or TipoDeNat.SemNat)
                Causas.Add("Nenhum problema de conectividade encontrado. Dá para hospedar partida e entrar em festa.");
        }

        // A classe base joga todos os avisos na barra de status. Aqui isso
        // duplicaria a explicação do NAT, que já tem bloco próprio no topo, e o
        // texto ainda sairia cortado no meio da frase por falta de espaço.
        Status = resultado.Itens.Count == 0
            ? "Medição concluída. Nada a ajustar."
            : $"Medição concluída. {resultado.Itens.Count} ajuste(s) disponíveis abaixo.";
    }

    protected override string MontarConfirmacao(IReadOnlyList<ActionItemViewModel> selecionados)
    {
        var nomes = string.Join("\n  ", selecionados.Select(i => i.Titulo));
        var winsock = selecionados.Any(i => i.Item.Id == "rede:winsock");
        var dns = selecionados.Count(i => i.Item.Id.StartsWith("dns:", StringComparison.Ordinal));

        var texto = $"Aplicar {selecionados.Count} ação(ões) de rede?\n\n  {nomes}\n\n";

        if (dns > 1)
        {
            texto += "Você marcou mais de um servidor de DNS. Só um pode ser o primário: "
                   + "o último da lista é o que vai valer.\n\n";
        }

        if (dns > 0)
        {
            texto += "Trocar o DNS não muda o ping do jogo. O jogo resolve o nome do servidor "
                   + "uma vez e depois fala direto com ele por IP. O que melhora é o tempo de "
                   + "abrir páginas e de o launcher encontrar os servidores.\n\n";
        }

        if (winsock)
        {
            texto += "ATENÇÃO: resetar o Winsock derruba a rede até você reiniciar o "
                   + "computador, apaga configurações de VPN e proxy, e **não tem desfazer "
                   + "automático**. Só faz sentido se a máquina já está sem internet.\n\n";
        }

        texto += "Continuar?";
        return texto;
    }
}
