using System.Management;
using System.ServiceProcess;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;

// O enum do Core e o do System.ServiceProcess tem o mesmo nome: o alias fixa qual vale aqui.
using ServiceStartMode = GameBoost.Core.Abstractions.ServiceStartMode;

namespace GameBoost.Core.Services;

public sealed class WindowsServiceControllerService : IServiceControllerService
{
    private readonly IGameBoostLogger _log;

    public WindowsServiceControllerService(IGameBoostLogger log)
    {
        _log = log;
    }

    public ServiceInfo? GetService(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            return new ServiceInfo(
                sc.ServiceName,
                sc.DisplayName,
                sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending,
                LerStartMode(name));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public IReadOnlyList<ServiceInfo> GetServices()
    {
        var modos = LerTodosOsStartModes();
        var lista = new List<ServiceInfo>();

        foreach (var sc in ServiceController.GetServices())
        {
            try
            {
                lista.Add(new ServiceInfo(
                    sc.ServiceName,
                    sc.DisplayName,
                    sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending,
                    modos.TryGetValue(sc.ServiceName, out var modo) ? modo : ServiceStartMode.Manual));
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                sc.Dispose();
            }
        }

        return lista;
    }

    public bool Stop(string name, TimeSpan timeout)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status == ServiceControllerStatus.Stopped)
                return true;

            if (!sc.CanStop)
            {
                _log.Warn("Services", "Stop", name, "servico nao aceita parada");
                return false;
            }

            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
            return sc.Status == ServiceControllerStatus.Stopped;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            _log.Warn("Services", "Stop", name, $"falhou: {ex.Message}");
            return false;
        }
    }

    public bool Start(string name, TimeSpan timeout)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status == ServiceControllerStatus.Running)
                return true;

            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, timeout);
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            _log.Warn("Services", "Start", name, $"falhou: {ex.Message}");
            return false;
        }
    }

    public bool SetStartMode(string name, ServiceStartMode mode)
    {
        try
        {
            using var servico = new ManagementObject($"Win32_Service.Name='{name}'");
            var resultado = servico.InvokeMethod("ChangeStartMode", new object[] { Traduzir(mode) });
            var codigo = Convert.ToUInt32(resultado);

            if (codigo != 0)
                _log.Warn("Services", "SetStartMode", name, $"ChangeStartMode retornou {codigo}");

            return codigo == 0;
        }
        catch (ManagementException ex)
        {
            _log.Warn("Services", "SetStartMode", name, $"falhou: {ex.Message}");
            return false;
        }
    }

    private static string Traduzir(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.Boot => "Boot",
        ServiceStartMode.System => "System",
        ServiceStartMode.Automatic => "Automatic",
        ServiceStartMode.Manual => "Manual",
        ServiceStartMode.Disabled => "Disabled",
        _ => "Manual"
    };

    private static ServiceStartMode LerStartMode(string name)
    {
        var modos = LerTodosOsStartModes();
        return modos.TryGetValue(name, out var modo) ? modo : ServiceStartMode.Manual;
    }

    /// <summary>Uma consulta WMI so: ler servico a servico custaria segundos.</summary>
    private static Dictionary<string, ServiceStartMode> LerTodosOsStartModes()
    {
        var mapa = new Dictionary<string, ServiceStartMode>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var consulta = new ManagementObjectSearcher("SELECT Name, StartMode FROM Win32_Service");
            foreach (var item in consulta.Get())
            {
                using var mo = (ManagementObject)item;
                var nome = mo["Name"] as string;
                var modo = mo["StartMode"] as string;
                if (nome is null || modo is null)
                    continue;

                mapa[nome] = modo switch
                {
                    "Boot" => ServiceStartMode.Boot,
                    "System" => ServiceStartMode.System,
                    "Auto" => ServiceStartMode.Automatic,
                    "Manual" => ServiceStartMode.Manual,
                    "Disabled" => ServiceStartMode.Disabled,
                    _ => ServiceStartMode.Manual
                };
            }
        }
        catch (ManagementException)
        {
        }

        return mapa;
    }
}
