using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using GameBoost.App.ViewModels;
using GameBoost.App.Views;
using GameBoost.Cli;
using GameBoost.Core;
using GameBoost.Core.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace GameBoost.App;

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
        colecao.AddSingleton<MainViewModel>();
        _services = colecao.BuildServiceProvider();

        var log = _services.GetRequiredService<IGameBoostLogger>();
        log.Info("App", "Startup", null,
            $"v{typeof(App).Assembly.GetName().Version} admin={CoreServices.RodandoComoAdministrador()}");

        // Um unico GameBoost.exe atende UI e linha de comando, como na v1.
        var opcoes = CommandLineOptions.Parse(e.Args);
        if (opcoes.EhCli)
        {
            var codigo = ExecutarCli(opcoes);
            Shutdown(codigo);
            return;
        }

        var janela = new MainWindow { DataContext = _services.GetRequiredService<MainViewModel>() };
        MainWindow = janela;
        janela.Show();
    }

    private int ExecutarCli(CommandLineOptions opcoes)
    {
        // App WPF nao tem console proprio: anexa ao console do chamador para que
        // GameBoost.exe --scan continue imprimindo no terminal, como na v1.
        var anexou = AttachConsole(AttachParentProcess);

        try
        {
            using var saida = anexou
                ? new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true }
                : TextWriter.Null;

            var runner = new CommandRunner(_services!, saida);
            return runner.ExecutarAsync(opcoes).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _services!.GetRequiredService<IGameBoostLogger>()
                .Error("Cli", opcoes.Comando.ToString(), null, "falha nao tratada", ex);
            return 1;
        }
        finally
        {
            if (anexou)
                FreeConsole();
        }
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();
}
