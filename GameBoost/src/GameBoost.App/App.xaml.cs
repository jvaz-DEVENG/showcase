using System.Windows;
using GameBoost.App.ViewModels;
using GameBoost.App.Views;
using GameBoost.Core;
using GameBoost.Core.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace GameBoost.App;

/// <summary>
/// Caminho grafico. Os comandos de linha nunca chegam aqui: Program os trata
/// antes de o WPF existir.
/// </summary>
public partial class App : Application
{
    private IServiceProvider? _services;

    public static IServiceProvider Services =>
        ((App)Current)._services ?? throw new InvalidOperationException("Container ainda nao inicializado.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Sem isto o app morre com APPCRASH e nao deixa rastro nenhum: o log
        // para na ultima acao bem-sucedida e nao ha stack trace em lugar algum.
        DispatcherUnhandledException += AoFalharNaUi;
        AppDomain.CurrentDomain.UnhandledException += AoFalharForaDaUi;
        TaskScheduler.UnobservedTaskException += AoFalharEmTask;

        var colecao = new ServiceCollection();
        colecao.AddGameBoostCore();
        colecao.AddSingleton<InicioPageViewModel>();
        colecao.AddSingleton<DiagnosticoPageViewModel>();
        colecao.AddSingleton<LimpezaPageViewModel>();
        colecao.AddSingleton<EspacoPageViewModel>();
        colecao.AddSingleton<AppsPageViewModel>();
        colecao.AddSingleton<InicializacaoPageViewModel>();
        colecao.AddSingleton<TweaksPageViewModel>();
        colecao.AddSingleton<RedePageViewModel>();
        colecao.AddSingleton<FerramentasPageViewModel>();
        colecao.AddSingleton<JogosPageViewModel>();
        colecao.AddSingleton<ConfiguracoesPageViewModel>();
        colecao.AddSingleton<Services.BandejaDoSistema>();
        colecao.AddSingleton<ShellViewModel>();
        _services = colecao.BuildServiceProvider();

        _services.GetRequiredService<IGameBoostLogger>().Info("App", "Startup", null,
            $"v{typeof(App).Assembly.GetName().Version} admin={CoreServices.RodandoComoAdministrador()}");

        var janela = new ShellWindow { DataContext = _services.GetRequiredService<ShellViewModel>() };
        MainWindow = janela;
        janela.Show();

        // A bandeja vem primeiro: o vigia liga o convite nela.
        LigarBandeja(janela);
        LigarVigiaDeJogos();
        MostrarOnboarding(janela);
    }

    // ==================================================================
    // Bandeja (secao 6)
    // ==================================================================

    private Services.BandejaDoSistema? _bandeja;

    private void LigarBandeja(ShellWindow janela)
    {
        var settings = _services!.GetRequiredService<Core.Settings.ISettingsStore>().Load();

        if (!settings.MostrarNaBandeja)
            return;

        _bandeja = _services!.GetRequiredService<Services.BandejaDoSistema>();
        var shell = _services!.GetRequiredService<ShellViewModel>();

        _bandeja.AoAbrir += () => Dispatcher.Invoke(() =>
        {
            janela.Show();
            janela.WindowState = WindowState.Normal;
            janela.Activate();
        });

        _bandeja.AoSair += () => Dispatcher.Invoke(() =>
        {
            // Sair pela bandeja sai de verdade. Um programa que so finge
            // fechar e continua rodando escondido e queixa legitima.
            _saindoDeVez = true;
            Shutdown();
        });

        _bandeja.AoAlternarModoGame += () => Dispatcher.Invoke(shell.AlternarModoGamePelaBandeja);
        _bandeja.AoLimparRam += () => Dispatcher.Invoke(shell.LimparRamPelaBandeja);

        _bandeja.Mostrar();
        janela.Bandeja = _bandeja;
    }

    private bool _saindoDeVez;

    /// <summary>True enquanto fechar a janela deve so esconder.</summary>
    public bool EsconderEmVezDeFechar => _bandeja is { Visivel: true } && !_saindoDeVez;

    // ==================================================================
    // Deteccao automatica de jogo (secao 5.1)
    // ==================================================================

    private void LigarVigiaDeJogos()
    {
        var settings = _services!.GetRequiredService<Core.Settings.ISettingsStore>().Load();

        if (!settings.DetectarJogosAutomaticamente)
            return;

        var vigia = _services!.GetRequiredService<Core.Modules.Profiles.GameWatcher>();
        var shell = _services!.GetRequiredService<ShellViewModel>();

        vigia.AoAbrir += evento => Dispatcher.Invoke(() => shell.JogoDetectado(evento));
        vigia.AoFechar += evento => Dispatcher.Invoke(() => shell.JogoEncerrado(evento));

        // Sem isto o convite morre no lugar onde nasce: o shell dispara o
        // evento e ninguem escuta. O jogo era detectado, o log registrava a
        // deteccao, e o usuario nao via nada em lugar nenhum.
        //
        // Balao E faixa, nao um ou outro: com o jogo em tela cheia o Windows
        // engole o balao, e a faixa fica esperando na janela. Sem tela cheia,
        // o balao chega primeiro.
        shell.AoPedirAviso += (titulo, texto) => Dispatcher.Invoke(() =>
            _bandeja?.Avisar(titulo, texto));

        vigia.Iniciar();
    }

    // ==================================================================
    // Onboarding (secao 6)
    // ==================================================================

    private void MostrarOnboarding(ShellWindow janela)
    {
        var store = _services!.GetRequiredService<Core.Settings.ISettingsStore>();
        var settings = store.Load();

        if (settings.OnboardingConcluido)
            return;

        var vm = new OnboardingViewModel();
        var dialogo = new OnboardingWindow { DataContext = vm, Owner = janela };

        vm.AoTerminar += abrirRelatorio =>
        {
            settings.OnboardingConcluido = true;
            store.Save(settings);

            dialogo.Close();

            if (abrirRelatorio)
                _services!.GetRequiredService<ShellViewModel>().AbrirRelatorioInicial();
        };

        dialogo.ShowDialog();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Sair sem desfazer o perfil aplicado deixaria prioridade e tweaks de
        // pe depois que o app sumiu. Isso e o oposto da regra 1.
        //
        // O GameWatcher NAO e descartado aqui: ele e singleton IDisposable, e o
        // proprio container o descarta logo abaixo. Chamar nos dois lugares e o
        // que fazia a excecao aparecer em toda saida.
        try
        {
            _services?.GetService<Core.Modules.Profiles.ProfileRunner>()?.Desfazer();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            Registrar("Exit", ex);
        }

        try
        {
            _bandeja?.Dispose();
            (_services as ServiceProvider)?.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // Falhar ao fechar nao pode virar caixa de erro: o app ja esta
            // saindo e nao ha nada que o usuario possa fazer a respeito.
            Registrar("Exit", ex);
        }

        base.OnExit(e);
    }

    private void AoFalharNaUi(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Registrar("UI", e.Exception);

        MessageBox.Show(
            "O GameBoost encontrou um erro inesperado e registrou os detalhes no log."
            + Environment.NewLine + Environment.NewLine
            + e.Exception.Message,
            "GameBoost", MessageBoxButton.OK, MessageBoxImage.Error);

        // A janela continua de pe: um erro numa tela nao precisa derrubar o app.
        e.Handled = true;
    }

    private void AoFalharForaDaUi(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Registrar("Dominio", ex);
    }

    private void AoFalharEmTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Registrar("Task", e.Exception);
        e.SetObserved();
    }

    private void Registrar(string origem, Exception ex)
    {
        try
        {
            _services?.GetRequiredService<IGameBoostLogger>()
                .Error("App", $"Excecao nao tratada ({origem})", ex.GetType().Name,
                    ex.ToString().Replace(Environment.NewLine, " | "), ex);
        }
        catch (Exception)
        {
            // Falhar ao registrar a falha nao pode virar uma segunda falha.
        }
    }

}
