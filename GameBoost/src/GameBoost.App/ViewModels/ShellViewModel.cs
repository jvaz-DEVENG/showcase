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

            new PlaceholderPageViewModel(
                "Jogos", "Perfis por jogo",
                "Cada jogo com seu proprio conjunto de ajustes, aplicado e revertido sozinho.",
                "\uE7FC", fase: 6, secaoDoSpec: "5.11",
                new[]
                {
                    "Deteccao automatica do jogo ao abrir, por pasta de launcher e processo",
                    "Biblioteca lida de Steam, Epic, GOG e Xbox, com tamanho e ultimo jogado",
                    "Perfil por executavel: energia, prioridade, afinidade, apps a fechar e reabrir",
                    "Tudo revertido quando o jogo fecha"
                }),

            new PlaceholderPageViewModel(
                "Tweaks", "Tweaks de jogos",
                "Catalogo de ajustes com efeito real, risco e reversao, todos desligados por padrao.",
                "\uE90F", fase: 5, secaoDoSpec: "5.8",
                new[]
                {
                    "HAGS com teste antes e depois, porque ajuda em uns e atrapalha em outros",
                    "Game Mode, Game DVR, Fullscreen Optimizations e MPO",
                    "Cada tweak diz o ganho honesto: marginal continua escrito marginal",
                    "Isolamento do nucleo e mitigacoes de CPU sao informados, nunca alterados"
                }),

            new PlaceholderPageViewModel(
                "Rede", "Diagnostico e ajustes de rede",
                "Mede latencia, jitter e perda, e mostra quem esta consumindo a banda agora.",
                "\uE839", fase: 5, secaoDoSpec: "5.9",
                new[]
                {
                    "Ping, jitter e perda para o gateway e para servidores publicos",
                    "Comparacao de DNS, com a escolha final sendo sua",
                    "Consumo de banda por processo",
                    "Wi-Fi: banda, sinal e canal, com a recomendacao honesta de usar cabo"
                }),

            ferramentas,

            configuracoes
        };

        _paginaAtual = Paginas[0];
        EhAdministrador = CoreServices.RodandoComoAdministrador();
    }

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
}
