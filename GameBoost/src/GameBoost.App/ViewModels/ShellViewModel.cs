using System.Collections.ObjectModel;
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
        _paginaAtual = Paginas[0];
        EhAdministrador = CoreServices.RodandoComoAdministrador();
    }

    private readonly InicioPageViewModel _inicio;
    private readonly JogosPageViewModel _jogos;

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

    public void JogoEncerrado(Core.Modules.Profiles.JogoFechou evento)
    {
        Convite = null;
        JogoDetectadoAgora = null;

        AoPedirAviso?.Invoke("GameBoost",
            $"{evento.Nome} fechou depois de {evento.Duracao.TotalMinutes:0} minutos. "
          + "Tudo o que o GameBoost alterou foi desfeito.");
    }

    [RelayCommand]
    private void AceitarConvite()
    {
        Convite = null;
        AlternarModoGamePelaBandeja();
    }

    [RelayCommand]
    private void RecusarConvite() => Convite = null;
}
