namespace GameBoost.Core.Abstractions;

public enum ServiceStartMode
{
    Boot,
    System,
    Automatic,
    Manual,
    Disabled
}

public sealed record ServiceInfo(string Name, string DisplayName, bool IsRunning, ServiceStartMode StartMode);

public interface IServiceControllerService
{
    ServiceInfo? GetService(string name);
    IReadOnlyList<ServiceInfo> GetServices();
    bool Stop(string name, TimeSpan timeout);
    bool Start(string name, TimeSpan timeout);
    bool SetStartMode(string name, ServiceStartMode mode);
}
