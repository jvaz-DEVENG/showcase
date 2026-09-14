namespace GameBoost.Core.Safety;

/// <summary>Servicos que nenhum modulo pode parar ou desabilitar (secao 9 do spec).</summary>
public static class ProtectedServices
{
    public static readonly IReadOnlySet<string> Intocaveis = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Nucleo
        "DcomLaunch", "RpcSs", "RpcEptMapper", "Winmgmt", "EventLog", "Schedule",
        "TrustedInstaller", "CryptSvc", "PlugPlay", "Power", "ProfSvc", "SamSs",
        "LSM", "Themes", "UserManager", "gpsvc", "BrokerInfrastructure", "SystemEventsBroker",
        // Rede
        "Dhcp", "Dnscache", "nsi", "NlaSvc", "netprofm", "WlanSvc", "LanmanWorkstation",
        // Audio
        "Audiosrv", "AudioEndpointBuilder",
        // Seguranca
        "WinDefend", "SecurityHealthService", "wscsvc", "Sense", "mpssvc", "BFE",
        "wuauserv" // so pausa temporaria dentro do Modo Game, nunca desabilitar
    };

    /// <summary>wuauserv pode ser pausado pelo Modo Game, mas nunca ter o StartType alterado.</summary>
    public static readonly IReadOnlySet<string> PausaveisTemporariamente = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "wuauserv", "UsoSvc", "DoSvc", "BITS"
    };
}
