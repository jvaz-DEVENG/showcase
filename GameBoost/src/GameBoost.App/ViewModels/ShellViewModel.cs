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
        ConfiguracoesPageViewModel configuracoes)
    {
        Paginas = new ObservableCollection<PageViewModelBase>
        {
            inicio,

            diagnostico,

            limpeza,

            new PlaceholderPageViewModel(
                "Espaco", "Analisador de espaco em disco",
                "Responde onde foram parar seus GB em segundos, lendo a MFT do disco.",
                "\uE8B7", fase: 3, secaoDoSpec: "5.4",
                new[]
                {
                    "Treemap navegavel e lista dos 100 maiores arquivos",
                    "Categorias inteligentes: jogos, videos, instaladores, caches, Windows.old",
                    "Explica hiberfil.sys e pagefile.sys em vez de so mostrar o tamanho",
                    "So mostra e abre no Explorer: nao apaga nada sozinho"
                }),

            new PlaceholderPageViewModel(
                "Apps", "Desinstalador de aplicativos",
                "Lista tudo que esta instalado, inclusive apps da Store, e limpa os restos.",
                "\uE71D", fase: 4, secaoDoSpec: "5.3",
                new[]
                {
                    "Win32, Store e portateis detectados, com tamanho real e ultimo uso",
                    "Desinstalacao silenciosa em lote, com varredura de restos depois",
                    "Sugestao de bloatware conhecido, nunca pre-marcada",
                    "Redistribuiveis, runtimes e servicos do Xbox ficam protegidos",
                    "Aba \"Restos de apps antigos\": pastas em AppData e ProgramData que nao pertencem a nenhum app instalado"
                }),

            new PlaceholderPageViewModel(
                "Inicializacao", "Gerenciador de inicializacao",
                "Mostra o que abre junto com o Windows e o quanto cada item pesa no boot.",
                "\uE7E8", fase: 4, secaoDoSpec: "5.6",
                new[]
                {
                    "Chaves Run, pasta Inicializar, tarefas agendadas e servicos de terceiros",
                    "Publisher e assinatura digital de cada entrada",
                    "Desativar de forma coerente com o Gerenciador de Tarefas, ou apenas atrasar",
                    "Antivirus, audio, video e OneDrive protegidos ou com aviso"
                }),

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

            new PlaceholderPageViewModel(
                "Ferramentas", "Ferramentas rapidas",
                "Atalhos para o que costuma exigir prompt de comando ou caca no Painel de Controle.",
                "\uE912", fase: 2, secaoDoSpec: "5.13",
                new[]
                {
                    "Reiniciar o Explorer e o driver de video, limpar cache DNS, esvaziar a Lixeira",
                    "Criar ponto de restauracao e verificar integridade do sistema",
                    "Teste de velocidade do disco",
                    "Informacoes do sistema com botao de copiar, para mandar a quem da suporte"
                }),

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
