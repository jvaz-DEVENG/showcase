using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBoost.Core;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Modules.DiskAnalyzer;
using GameBoost.Core.Modules.GameMode;
using GameBoost.Core.Modules.Profiles;

namespace GameBoost.App.ViewModels;

/// <summary>Um jogo da biblioteca, com ou sem perfil.</summary>
public sealed partial class JogoViewModel : ObservableObject
{
    public JogoViewModel(string nome, string? caminho, string launcher, long bytes, string quandoJogou, GameProfile? perfil)
    {
        Nome = nome;
        Caminho = caminho;
        Launcher = launcher;
        Bytes = bytes;
        QuandoJogou = quandoJogou;
        _perfil = perfil;
    }

    public string Nome { get; }
    public string? Caminho { get; }
    public string Launcher { get; }
    public long Bytes { get; }
    public string QuandoJogou { get; }

    [ObservableProperty] private GameProfile? _perfil;

    public bool TemPerfil => Perfil is not null;

    public string Tamanho => Bytes > 0 ? GameModeModule.Formatar(Bytes) : "tamanho desconhecido";

    public string Detalhe => Perfil is null
        ? $"{Launcher} · {Tamanho} · {QuandoJogou} · sem perfil"
        : $"{Launcher} · {Tamanho} · {QuandoJogou} · {Perfil.Resumo}";

    partial void OnPerfilChanged(GameProfile? value)
    {
        OnPropertyChanged(nameof(TemPerfil));
        OnPropertyChanged(nameof(Detalhe));
    }
}

/// <summary>
/// Página Jogos (seções 5.1 e 5.11).
///
/// Junta a biblioteca lida dos launchers com os perfis gravados. Um jogo sem
/// perfil aparece igual aos outros, com o botão de criar; o perfil não é
/// obrigatório para nada, e o Modo Game continua funcionando sem ele.
/// </summary>
public sealed partial class JogosPageViewModel : PageViewModelBase
{
    private readonly GameLibrary _biblioteca;
    private readonly ProfileStore _perfis;
    private readonly GameWatcher _vigia;
    private readonly ProfileRunner _executor;

    public JogosPageViewModel(
        GameLibrary biblioteca,
        ProfileStore perfis,
        GameWatcher vigia,
        ProfileRunner executor)
    {
        _biblioteca = biblioteca;
        _perfis = perfis;
        _vigia = vigia;
        _executor = executor;

        EhAdministrador = CoreServices.RodandoComoAdministrador();
    }

    public override string Nome => "Jogos";
    public override string Titulo => "Jogos e perfis";
    public override string Subtitulo => "Cada jogo com os seus ajustes, aplicados ao abrir e desfeitos ao fechar.";
    public override string Icone => "\uE7FC";

    public ObservableCollection<JogoViewModel> Jogos { get; } = new();

    [ObservableProperty] private string _resumo = "Clique em Varrer para ler a sua biblioteca.";
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private bool _ehAdministrador;
    [ObservableProperty] private bool _vigiaLigado;
    [ObservableProperty] private string _jogoEmExecucao = string.Empty;
    [ObservableProperty] private JogoViewModel? _selecionado;

    /// <summary>
    /// Lista vazia. A visibilidade do texto de estado vazio precisa depender
    /// disto, nao do Resumo: o Resumo nunca fica vazio, entao o texto ficava
    /// desenhado por cima da lista cheia.
    /// </summary>
    public bool ListaVazia => Jogos.Count == 0;

    public override void AoEntrar()
    {
        base.AoEntrar();
        VigiaLigado = _vigia.Ativo;
        AtualizarJogoEmExecucao();
    }

    private void AtualizarJogoEmExecucao()
    {
        var atual = _vigia.JogoAtual;

        JogoEmExecucao = atual is null
            ? string.Empty
            : _executor.EmSessao
                ? $"{atual.Process.Name} rodando, perfil aplicado."
                : $"{atual.Process.Name} rodando, sem perfil aplicado.";
    }

    [RelayCommand]
    public async Task VarrerAsync()
    {
        if (Ocupado)
            return;

        Ocupado = true;
        Jogos.Clear();
        Resumo = "Lendo Steam, Epic, GOG e Xbox...";

        try
        {
            var achados = await Task.Run(() => _biblioteca.Listar());

            var perfis = _perfis.Todos.ToList();

            foreach (var jogo in achados)
            {
                Jogos.Add(new JogoViewModel(
                    jogo.Nome, jogo.Pasta, jogo.Launcher, jogo.Bytes, jogo.QuandoJogou,
                    CasarPerfil(jogo, perfis)));
            }

            // Perfis de jogos que a biblioteca não achou: instalado fora de
            // launcher, ou launcher que o GameBoost ainda não lê. Some da tela
            // seria pior que aparecer sem tamanho.
            foreach (var perfil in perfis)
            {
                if (Jogos.Any(j => j.Perfil?.Executavel == perfil.Executavel))
                    continue;

                Jogos.Add(new JogoViewModel(
                    perfil.Nome, perfil.Caminho, perfil.Launcher ?? "fora de launcher",
                    0, perfil.UltimaVez is null ? "nunca usado pelo perfil" : $"perfil usado {perfil.VezesUsado}x",
                    perfil));
            }

            OnPropertyChanged(nameof(ListaVazia));

            var comPerfil = Jogos.Count(j => j.TemPerfil);
            var total = Jogos.Sum(j => j.Bytes);

            Resumo = $"{Jogos.Count} jogos, {GameModeModule.Formatar(total)} no total. "
                   + $"{comPerfil} com perfil.";

            Status = VigiaLigado
                ? "A detecção automática está ligada: ao abrir um jogo, o GameBoost pergunta."
                : "A detecção automática está desligada. Ligue em Configurações para o GameBoost "
                + "reconhecer o jogo sozinho.";
        }
        catch (OperationCanceledException)
        {
            Status = "Varredura cancelada.";
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Cria um perfil padrão para o jogo selecionado.</summary>
    [RelayCommand]
    private void CriarPerfil()
    {
        var jogo = Selecionado;

        if (jogo is null || jogo.TemPerfil)
            return;

        var executavel = ExecutavelProvavel(jogo);

        if (executavel is null)
        {
            Status = $"Não foi possível descobrir o executável de {jogo.Nome}. "
                   + "Abra o jogo uma vez com a detecção automática ligada: o GameBoost aprende sozinho.";
            return;
        }

        var perfil = GameProfile.Padrao(executavel, jogo.Nome, jogo.Caminho, jogo.Launcher);
        _perfis.Salvar(perfil);
        jogo.Perfil = perfil;

        Status = $"Perfil criado para {jogo.Nome}. Ele não ativa sozinho: "
               + "marque \"ativar sozinho\" se quiser que o GameBoost não pergunte.";
    }

    [RelayCommand]
    private void RemoverPerfil()
    {
        var jogo = Selecionado;

        if (jogo?.Perfil is null)
            return;

        _perfis.Remover(jogo.Perfil.Executavel);
        Status = $"Perfil de {jogo.Nome} removido. O jogo continua na biblioteca.";
        jogo.Perfil = null;
    }

    /// <summary>Alterna "ativa sozinho" do perfil selecionado.</summary>
    [RelayCommand]
    private void AlternarAutomatico()
    {
        var perfil = Selecionado?.Perfil;

        if (perfil is null)
            return;

        perfil.Automatico = !perfil.Automatico;
        _perfis.Salvar(perfil);

        // Forca a linha a redesenhar com o resumo novo.
        var jogo = Selecionado!;
        jogo.Perfil = null;
        jogo.Perfil = perfil;

        Status = perfil.Automatico
            ? $"{perfil.Nome} passa a ativar o Modo Game sozinho ao abrir."
            : $"{perfil.Nome} volta a perguntar antes de ativar.";
    }

    /// <summary>
    /// Tenta adivinhar o executável a partir da pasta do jogo.
    ///
    /// É palpite, e a tela diz isso: escolhe o maior .exe da raiz da pasta, que
    /// acerta na maioria dos jogos e erra em quem põe o executável num
    /// subdiretório `Binaries\Win64`. Quando erra, o caminho certo é abrir o
    /// jogo com a detecção ligada — aí o executável vem do processo de verdade.
    /// </summary>
    private static string? ExecutavelProvavel(JogoViewModel jogo)
    {
        if (jogo.Caminho is null || !Directory.Exists(jogo.Caminho))
            return null;

        try
        {
            var candidatos = Directory.EnumerateFiles(jogo.Caminho, "*.exe", SearchOption.TopDirectoryOnly)
                .Select(f => new FileInfo(f))
                .Where(f => !EhAuxiliar(f.Name))
                .OrderByDescending(f => f.Length)
                .ToList();

            return candidatos.Count == 0 ? null : Path.GetFileNameWithoutExtension(candidatos[0].Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool EhAuxiliar(string nome)
    {
        string[] marcadores = { "unins", "setup", "install", "launcher", "crash", "report", "redist", "vcredist", "directx" };
        return marcadores.Any(m => nome.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Acha o perfil do jogo, pela pasta antes do nome.
    ///
    /// A primeira versão casava pelo nome da pasta contra a chave do perfil,
    /// que é o **executável**. "Warframe" nunca casa com "warframe.x64": o jogo
    /// aparecia como "sem perfil" e o perfil entrava de novo na lista como
    /// órfão. A mesma linha duas vezes, uma dizendo que tem perfil e a outra
    /// que não tem.
    ///
    /// A pasta de instalação é o que os dois têm em comum e não muda, então é
    /// por ela que o casamento começa. O nome fica de reserva, para o perfil
    /// que foi gravado sem caminho.
    /// </summary>
    private static GameProfile? CasarPerfil(JogoInstalado jogo, IReadOnlyList<GameProfile> perfis)
    {
        if (!string.IsNullOrWhiteSpace(jogo.Pasta))
        {
            var pasta = jogo.Pasta.TrimEnd('\\', '/');

            var porCaminho = perfis.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p.Caminho)
                && string.Equals(p.Caminho!.TrimEnd('\\', '/'), pasta, StringComparison.OrdinalIgnoreCase));

            if (porCaminho is not null)
                return porCaminho;
        }

        return perfis.FirstOrDefault(p =>
            string.Equals(p.Nome, jogo.Nome, StringComparison.OrdinalIgnoreCase));
    }
}
