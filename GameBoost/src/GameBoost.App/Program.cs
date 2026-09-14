using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GameBoost.Cli;
using GameBoost.Core;
using GameBoost.Core.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace GameBoost.App;

/// <summary>
/// Ponto de entrada do GameBoost.exe. Decide antes de qualquer coisa se a
/// execucao e de linha de comando ou grafica.
///
/// O caminho CLI **nao** inicializa o WPF. A primeira tentativa chamava
/// Application.Shutdown() dentro de OnStartup e o processo ficava pendurado:
/// um app WPF que nunca abriu janela mantem o loop de mensagens vivo, e
/// Environment.Exit no meio da inicializacao arrisca travar. Separar o Main
/// resolve na origem: em `--scan` nem existe Application.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var opcoes = CommandLineOptions.Parse(args);

        if (!opcoes.EhCli)
        {
            App.Main();
            return 0;
        }

        return ExecutarCli(opcoes);
    }

    private static int ExecutarCli(CommandLineOptions opcoes)
    {
        // Sendo WinExe, o processo nao tem console proprio: anexa ao console de
        // quem chamou para que `GameBoost.exe --scan` imprima no terminal, como na v1.
        var anexou = AttachConsole(AttachParentProcess);

        ServiceProvider? provider = null;

        try
        {
            var colecao = new ServiceCollection();
            colecao.AddGameBoostCore();
            provider = colecao.BuildServiceProvider();

            provider.GetRequiredService<IGameBoostLogger>()
                .Info("Cli", opcoes.Comando.ToString(), null,
                    $"admin={CoreServices.RodandoComoAdministrador()} dryRun={opcoes.DryRun}");

            using var saida = anexou
                ? new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true }
                : TextWriter.Null;

            return new CommandRunner(provider, saida).ExecutarAsync(opcoes).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            try
            {
                provider?.GetRequiredService<IGameBoostLogger>()
                    .Error("Cli", opcoes.Comando.ToString(), null, "falha nao tratada", ex);
            }
            catch (InvalidOperationException)
            {
                // Container nao chegou a subir: nao ha onde registrar.
            }

            if (anexou)
                Console.Error.WriteLine($"Erro: {ex.Message}");

            return 1;
        }
        finally
        {
            provider?.Dispose();

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
