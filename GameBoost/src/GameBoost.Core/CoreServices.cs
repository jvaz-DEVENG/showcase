using System.Diagnostics;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Modules;
using GameBoost.Core.Modules.Bottleneck;
using GameBoost.Core.Modules.Cleaner;
using GameBoost.Core.Modules.DiskAnalyzer;
using GameBoost.Core.Modules.Startup;
using GameBoost.Core.Modules.Drivers;
using GameBoost.Core.Modules.Network;
using GameBoost.Core.Modules.Tweaks;
using GameBoost.Core.Modules.Uninstaller;
using GameBoost.Core.Modules.Tools;
using GameBoost.Core.Modules.GameMode;
using GameBoost.Core.Modules.HealthReport;
using GameBoost.Core.Safety;
using GameBoost.Core.Services;
using GameBoost.Core.Settings;
using GameBoost.Core.State;
using Microsoft.Extensions.DependencyInjection;

namespace GameBoost.Core;

/// <summary>
/// Composicao unica do Core. App e CLI montam o mesmo grafo de servicos, entao
/// os dois se comportam igual (regra 4.2: nada de static).
/// </summary>
public static class CoreServices
{
    /// <param name="paths">
    /// Deixe nulo em producao para resolver %LOCALAPPDATA% (ou .\data no modo
    /// portatil). Os testes de integracao passam uma pasta temporaria para nao
    /// escrever nos dados reais do usuario.
    /// </param>
    public static IServiceCollection AddGameBoostCore(this IServiceCollection services, AppPaths? paths = null)
    {
        paths ??= AppPaths.Resolve();
        paths.EnsureDirectories();

        services.AddSingleton(paths);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IFileSystem, WindowsFileSystem>();
        services.AddSingleton<IGameBoostLogger, StructuredLogger>();

        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton(sp => sp.GetRequiredService<ISettingsStore>().Load());

        services.AddSingleton<IRegistryService, WindowsRegistryService>();
        services.AddSingleton<IProcessService, WindowsProcessService>();
        services.AddSingleton<IPowerService, WindowsPowerService>();
        services.AddSingleton<IMemoryService, WindowsMemoryService>();
        services.AddSingleton<IServiceControllerService, WindowsServiceControllerService>();

        services.AddSingleton<AntivirusDetector>();
        services.AddSingleton<ISafetyGuard>(sp =>
        {
            using var atual = Process.GetCurrentProcess();
            Native.NativeMethodsBridge.TryGetSessionId(atual.Id, out var sessao);

            return new SafetyGuard(
                sp.GetRequiredService<AppSettings>(),
                sessao,
                atual.Id,
                sp.GetRequiredService<AntivirusDetector>().Detectar());
        });

        services.AddSingleton<IStateBackup, StateBackup>();
        services.AddSingleton<IRollbackEngine, RollbackEngine>();
        services.AddSingleton<ISessionStore, SessionStore>();

        // Fase 1: diagnostico de gargalos e relatorio de saude.
        services.AddSingleton<ITemperatureProvider, WmiTemperatureProvider>();
        services.AddSingleton<IMetricsCollector, WindowsMetricsCollector>();
        services.AddSingleton<SystemFactsReader>();
        services.AddSingleton<FindingEngine>(sp => new FindingEngine(
            sp.GetRequiredService<IGameBoostLogger>(),
            sp.GetRequiredService<IClock>()));
        services.AddSingleton<BottleneckMonitor>();
        services.AddSingleton<HealthReportModule>();

        // Fase 2: limpeza.
        services.AddSingleton<CleanupHistory>();
        services.AddSingleton<CleanerModule>();
        services.AddSingleton<QuickToolsService>();

        // Fase 3: analisador de espaco.
        services.AddSingleton<DiskScanner>();
        services.AddSingleton<GameLibrary>();

        // Fase 4: desinstalador.
        services.AddSingleton<AppInventory>();
        services.AddSingleton<LeftoverScanner>();
        services.AddSingleton<UninstallerModule>();
        services.AddSingleton<StartupModule>();

        // Fase 5: tweaks e servicos.
        services.AddSingleton<TweaksModule>();
        services.AddSingleton<NetworkDiagnostics>();
        services.AddSingleton<NatDiagnostics>();
        services.AddSingleton<SpeedTest>();
        services.AddSingleton<NetworkModule>();
        services.AddSingleton<GpuDriverInfo>();

        services.AddSingleton<SystemSilencer>();
        services.AddSingleton<GameDetector>();
        services.AddSingleton<GameModeModule>();
        services.AddSingleton<IModule>(sp => sp.GetRequiredService<GameModeModule>());

        return services;
    }

    /// <summary>
    /// Varreduras funcionam sem admin; as acoes ficam desabilitadas com aviso
    /// (modo somente leitura da secao 4.2).
    /// </summary>
    public static bool RodandoComoAdministrador()
    {
        try
        {
            using var identidade = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identidade);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }
}
