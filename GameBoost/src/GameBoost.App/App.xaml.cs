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
        colecao.AddSingleton<FerramentasPageViewModel>();
        colecao.AddSingleton<ConfiguracoesPageViewModel>();
        colecao.AddSingleton<ShellViewModel>();
        _services = colecao.BuildServiceProvider();

        _services.GetRequiredService<IGameBoostLogger>().Info("App", "Startup", null,
            $"v{typeof(App).Assembly.GetName().Version} admin={CoreServices.RodandoComoAdministrador()}");

        var janela = new ShellWindow { DataContext = _services.GetRequiredService<ShellViewModel>() };
        MainWindow = janela;
        janela.Show();
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

    protected override void OnExit(ExitEventArgs e)
    {
        (_services as ServiceProvider)?.Dispose();
        base.OnExit(e);
    }
}
