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

        var colecao = new ServiceCollection();
        colecao.AddGameBoostCore();
        colecao.AddSingleton<InicioPageViewModel>();
        colecao.AddSingleton<ConfiguracoesPageViewModel>();
        colecao.AddSingleton<ShellViewModel>();
        _services = colecao.BuildServiceProvider();

        _services.GetRequiredService<IGameBoostLogger>().Info("App", "Startup", null,
            $"v{typeof(App).Assembly.GetName().Version} admin={CoreServices.RodandoComoAdministrador()}");

        var janela = new ShellWindow { DataContext = _services.GetRequiredService<ShellViewModel>() };
        MainWindow = janela;
        janela.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (_services as ServiceProvider)?.Dispose();
        base.OnExit(e);
    }
}
