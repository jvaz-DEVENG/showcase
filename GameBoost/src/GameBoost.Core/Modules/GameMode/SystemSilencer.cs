using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Safety;
using GameBoost.Core.Services;
using GameBoost.Core.State;

namespace GameBoost.Core.Modules.GameMode;

/// <summary>Um ajuste de registro com valor alvo. O anterior vira ChangeRecord antes de aplicar.</summary>
public sealed record RegistryTweak(
    string Nome,
    RegistryRoot Root,
    string SubKey,
    string ValueName,
    int ValorDesejado,
    string Explicacao);

/// <summary>
/// Silencia o que atrapalha durante o jogo: notificacoes, gravacao em segundo
/// plano, overlay da Game Bar e Windows Update. Tudo reversivel (regra 1).
/// </summary>
public sealed class SystemSilencer
{
    public const string ModuloId = "gamemode";

    public static readonly IReadOnlyList<RegistryTweak> Tweaks = new[]
    {
        new RegistryTweak(
            "Notificacoes do Windows",
            RegistryRoot.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\PushNotifications",
            "ToastEnabled", 0,
            "Impede que notificacoes apareçam sobre o jogo em tela cheia."),

        new RegistryTweak(
            "Gravacao em segundo plano (Game DVR)",
            RegistryRoot.CurrentUser,
            @"System\GameConfigStore",
            "GameDVR_Enabled", 0,
            "Desliga a gravacao continua da Game Bar, que consome CPU e GPU o tempo todo."),

        new RegistryTweak(
            "Overlay da Xbox Game Bar",
            RegistryRoot.CurrentUser,
            @"Software\Microsoft\GameBar",
            "UseNexusForGameBarEnabled", 0,
            "Desliga o overlay da Game Bar, que injeta codigo em todo jogo aberto."),

        new RegistryTweak(
            "Dica de abertura da Game Bar",
            RegistryRoot.CurrentUser,
            @"Software\Microsoft\GameBar",
            "ShowStartupPanel", 0,
            "Evita o painel da Game Bar aparecer ao abrir o jogo.")
    };

    private readonly IRegistryService _registry;
    private readonly IServiceControllerService _services;
    private readonly IStateBackup _backup;
    private readonly ISafetyGuard _guard;
    private readonly IGameBoostLogger _log;

    public SystemSilencer(
        IRegistryService registry,
        IServiceControllerService services,
        IStateBackup backup,
        ISafetyGuard guard,
        IGameBoostLogger log)
    {
        _registry = registry;
        _services = services;
        _backup = backup;
        _guard = guard;
        _log = log;
    }

    /// <summary>Aplica um tweak gravando antes o valor anterior. Retorna o id do ChangeRecord.</summary>
    public string? Aplicar(RegistryTweak tweak, bool dryRun)
    {
        var anterior = _registry.GetValue(tweak.Root, tweak.SubKey, tweak.ValueName);
        var existia = anterior is not null;

        if (existia && anterior is int atual && atual == tweak.ValorDesejado)
        {
            _log.Info(ModuloId, "Tweak", tweak.Nome, "ja estava no valor desejado, nada a fazer");
            return null;
        }

        if (dryRun)
        {
            _log.Info(ModuloId, "Tweak (dry-run)", tweak.Nome,
                $"mudaria de {anterior?.ToString() ?? "(inexistente)"} para {tweak.ValorDesejado}");
            return null;
        }

        var record = _backup.Registrar(new ChangeRecord
        {
            Modulo = ModuloId,
            Tipo = ChangeType.Registry,
            Alvo = tweak.SubKey,
            SubAlvo = tweak.ValueName,
            ValorAnterior = WindowsRegistryService.Serializar(anterior),
            ValorNovo = tweak.ValorDesejado.ToString(),
            ValorAnteriorExistia = existia,
            Extras =
            {
                ["root"] = tweak.Root.ToString(),
                ["kind"] = RegistryValueKindLite.DWord.ToString(),
                ["nome"] = tweak.Nome
            }
        });

        _registry.SetValue(tweak.Root, tweak.SubKey, tweak.ValueName, tweak.ValorDesejado, RegistryValueKindLite.DWord);
        _log.Info(ModuloId, "Tweak", tweak.Nome, $"aplicado: {tweak.ValueName}={tweak.ValorDesejado}");
        return record.Id;
    }

    /// <summary>
    /// Pausa o Windows Update parando wuauserv, sem mexer no StartType. Se o app
    /// morrer aqui, o ChangeRecord garante que o servico volta.
    /// </summary>
    public string? PausarWindowsUpdate(bool dryRun)
    {
        const string servico = "wuauserv";

        var veredito = _guard.CheckService(servico, apenasPausar: true);
        if (veredito.Protegido)
        {
            _log.Warn(ModuloId, "PausarUpdate", servico, veredito.Explicacao);
            return null;
        }

        var atual = _services.GetService(servico);
        if (atual is null)
        {
            _log.Warn(ModuloId, "PausarUpdate", servico, "servico nao encontrado");
            return null;
        }

        if (!atual.IsRunning)
        {
            _log.Info(ModuloId, "PausarUpdate", servico, "ja estava parado");
            return null;
        }

        if (dryRun)
        {
            _log.Info(ModuloId, "PausarUpdate (dry-run)", servico, "pararia o servico");
            return null;
        }

        var record = _backup.Registrar(new ChangeRecord
        {
            Modulo = ModuloId,
            Tipo = ChangeType.Service,
            Alvo = servico,
            ValorAnterior = atual.StartMode.ToString(),
            ValorNovo = atual.StartMode.ToString(),
            Extras =
            {
                ["estavaRodando"] = "true",
                ["nome"] = "Windows Update pausado durante o jogo"
            }
        });

        var ok = _services.Stop(servico, TimeSpan.FromSeconds(20));
        _log.Log(ok ? LogLevel.Info : LogLevel.Warn, ModuloId, "PausarUpdate", servico,
            ok ? "servico parado" : "nao foi possivel parar");

        return ok ? record.Id : null;
    }
}
