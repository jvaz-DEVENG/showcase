using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBoost.Core;

namespace GameBoost.App.ViewModels;

/// <summary>
/// Shell da secao 6: navegacao lateral com as 11 entradas, uma pagina por vez.
///
/// As paginas dos modulos que ainda nao existem sao placeholders explicando o
/// que farao e em qual fase chegam. Elas ficam no menu de proposito: o usuario
/// ve o mapa inteiro do produto desde a primeira versao.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(
        InicioPageViewModel inicio,
        DiagnosticoPageViewModel diagnostico,
        LimpezaPageViewModel limpeza,
        EspacoPageViewModel espaco,
        AppsPageViewModel apps,
        InicializacaoPageViewModel inicializacao,
        JogosPageViewModel jogos,
        Core.Modules.Profiles.ProfileRunner executorDePerfil,
        Core.Modules.Profiles.ProfileStore perfis,
        Core.Modules.Profiles.GameWatcher vigia,
        TweaksPageViewModel tweaks,
        RedePageViewModel rede,
        FerramentasPageViewModel ferramentas,
        ConfiguracoesPageViewModel configuracoes)
    {
        Paginas = new ObservableCollection<PageViewModelBase>
        {
            inicio,

            diagnostico,

            limpeza,

            espaco,

            apps,

            inicializacao,

            jogos,

            tweaks,

            rede,

            ferramentas,

            configuracoes
        };

        _inicio = inicio;
        _jogos = jogos;
        _executorDePerfil = executorDePerfil;
        _perfis = perfis;
        _vigia = vigia;
        _paginaAtual = Paginas[0];
        EhAdministrador = CoreServices.RodandoComoAdministrador();
    }

    private readonly InicioPageViewModel _inicio;
    private readonly JogosPageViewModel _jogos;
    private readonly Core.Modules.Profiles.ProfileRunner _executorDePerfil;
    private readonly Core.Modules.Profiles.ProfileStore _perfis;
    private readonly Core.Modules.Profiles.GameWatcher _vigia;

    public ObservableCollection<PageViewModelBase> Paginas { get; }

    [ObservableProperty] private PageViewModelBase _paginaAtual;
    [ObservableProperty] private bool _ehAdministrador;

    public string AvisoDePrivilegio => EhAdministrador
        ? "Executando como administrador."
        : "Modo somente leitura: sem privilegios de administrador.";

    [RelayCommand]
    private void Navegar(PageViewModelBase? pagina)
    {
        if (pagina is not null)
            PaginaAtual = pagina;
    }

    /// <summary>
    /// AoEntrar tem que ficar aqui, e nao no comando Navegar: o menu lateral
    /// troca de pagina pelo binding de SelectedItem, que nunca passa pelo
    /// comando. Com o gancho no comando, o Diagnostico nao comecava a coletar
    /// e o Historico nao recarregava ao abrir a pagina.
    /// </summary>
    partial void OnPaginaAtualChanged(PageViewModelBase? oldValue, PageViewModelBase newValue)
    {
        oldValue?.AoSair();
        newValue?.AoEntrar();
    }

    // ==================================================================
    // Bandeja (secao 6)
    // ==================================================================

    /// <summary>
    /// Menu da bandeja: liga ou desliga o Modo Game.
    ///
    /// Ligar leva para a tela de Inicio antes de agir. A confirmacao do Modo
    /// Game lista o que vai ser fechado, e essa lista **precisa** ser vista:
    /// ativar direto da bandeja, sem a tela, seria fechar programas do usuario
    /// a partir de um menu de dois cliques.
    /// </summary>
    public void AlternarModoGamePelaBandeja()
    {
        PaginaAtual = _inicio;

        if (_inicio.ModoGameAtivo)
        {
            _inicio.ReverterCommand.Execute(null);
            return;
        }

        // Sem varredura previa nao ha o que confirmar.
        if (_inicio.Itens.Count == 0)
            _inicio.VarrerCommand.Execute(null);
    }

    /// <summary>Menu da bandeja: libera memoria, sem tocar em mais nada.</summary>
    public void LimparRamPelaBandeja()
    {
        PaginaAtual = _inicio;
        _inicio.LimparRamCommand.Execute(null);
    }

    public void AbrirRelatorioInicial()
    {
        PaginaAtual = _inicio;
        _inicio.AnalisarSaudeCommand.Execute(null);
    }

    // ==================================================================
    // Deteccao automatica de jogo (secao 5.1)
    // ==================================================================

    [ObservableProperty] private string? _convite;

    public event Action<string, string>? AoPedirAviso;

    /// <summary>
    /// O vigia achou um jogo. Perfil com "ativa sozinho" aplica direto; todo o
    /// resto **pergunta** (regra 3).
    /// </summary>
    public void JogoDetectado(Core.Modules.Profiles.JogoAbriu evento)
    {
        JogoDetectadoAgora = evento;

        var nome = evento.Perfil?.Nome ?? evento.Jogo.Process.Name;

        if (evento.Perfil is { Automatico: true })
        {
            Convite = null;
            AoPedirAviso?.Invoke("GameBoost",
                $"{nome} detectado. O perfil dele ativa sozinho, e tudo volta ao normal quando o jogo fechar.");
            return;
        }

        Convite = evento.Perfil is null
            ? $"{nome} está rodando. Ativar o Modo Game para ele?"
            : $"{nome} está rodando. Aplicar o perfil deste jogo?";

        AoPedirAviso?.Invoke("GameBoost", Convite);
    }

    /// <summary>Guarda o evento inteiro; Convite e so o texto da faixa.</summary>
    public Core.Modules.Profiles.JogoAbriu? JogoDetectadoAgora { get; private set; }

    /// <summary>
    /// O jogo saiu: desfazer o que o perfil aplicou.
    ///
    /// A primeira versão só limpava o convite e anunciava que "tudo foi
    /// desfeito" — sem desfazer nada. Prioridade, afinidade e tweaks ficariam
    /// de pé até alguém reverter à mão, e o aviso na tela estaria mentindo.
    /// </summary>
    public void JogoEncerrado(Core.Modules.Profiles.JogoFechou evento)
    {
        Convite = null;
        JogoDetectadoAgora = null;

        // Saber ANTES se havia sessao: depois do Desfazer nao da mais para
        // distinguir "nao havia perfil" de "havia, mas nada sobrou para
        // restaurar".
        var haviaPerfil = _executorDePerfil.EmSessao;
        var feitos = _executorDePerfil.Desfazer();

        var texto = $"{evento.Nome} fechou depois de {evento.Duracao.TotalMinutes:0} minutos.";

        // Só afirmar que desfez quando desfez mesmo, e nunca negar que havia
        // perfil quando havia.
        if (feitos.Count > 0)
        {
            texto += $" Desfeito: {string.Join(", ", feitos)}.";
        }
        else if (haviaPerfil)
        {
            // Prioridade e afinidade morrem junto com o processo: quando o jogo
            // fecha primeiro, nao sobra o que restaurar. Isso e o esperado, nao
            // uma falha.
            texto += " O perfil saiu junto com o jogo: prioridade e afinidade "
                   + "valem enquanto o processo existe.";
        }
        else
        {
            texto += " Não havia perfil aplicado.";
        }

        AoPedirAviso?.Invoke("GameBoost", texto);
    }

    /// <summary>
    /// Aceitar o convite aplica o perfil do jogo e leva para o Modo Game.
    ///
    /// A primeira versão só navegava para a tela Início. O perfil nunca era
    /// aplicado: prioridade, afinidade e tweaks ficavam parados no arquivo, e o
    /// `ProfileRunner` não era chamado por ninguém.
    /// </summary>
    [RelayCommand]
    private void AceitarConvite()
    {
        var evento = JogoDetectadoAgora;
        Convite = null;

        if (evento is null)
        {
            AlternarModoGamePelaBandeja();
            return;
        }

        AplicarPerfil(evento);
        AlternarModoGamePelaBandeja();
    }

    /// <summary>
    /// Aplica o perfil do jogo, criando um padrão quando ainda não existe.
    ///
    /// Criar aqui é o que faz a segunda vez ser diferente da primeira: o perfil
    /// nasce com o executável certo, vindo do processo de verdade, e não de um
    /// palpite sobre qual `.exe` da pasta é o jogo.
    /// </summary>
    private void AplicarPerfil(Core.Modules.Profiles.JogoAbriu evento)
    {
        var perfil = evento.Perfil;

        if (perfil is null)
        {
            perfil = Core.Modules.Profiles.GameProfile.Padrao(
                evento.Jogo.Process.Name,
                evento.Jogo.Process.Name,
                Path.GetDirectoryName(evento.Jogo.Process.ExecutablePath),
                null);
        }

        try
        {
            _executorDePerfil.Aplicar(perfil, evento.Jogo.Process.Pid);
            _perfis.Salvar(perfil);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            AoPedirAviso?.Invoke("GameBoost", $"Não foi possível aplicar o perfil: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RecusarConvite()
    {
        // Recusar tem que valer para a sessão: sem isto a pergunta voltaria
        // dois segundos depois, e de novo, e de novo.
        if (JogoDetectadoAgora is not null)
            _vigia.Recusar(JogoDetectadoAgora.Jogo.Process.Name);

        Convite = null;
    }
}
