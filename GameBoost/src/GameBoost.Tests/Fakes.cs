using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;

namespace GameBoost.Tests;

/// <summary>
/// Sistema de arquivos em memoria que registra TODO caminho tocado.
/// E o que permite provar que nenhum modulo escreve fora da whitelist.
/// </summary>
public sealed class FakeFileSystem : IFileSystem
{
    public Dictionary<string, string> Arquivos { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Diretorios { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Escritas { get; } = new();
    public List<string> Remocoes { get; } = new();

    public bool FileExists(string path) => Arquivos.ContainsKey(path);

    public bool DirectoryExists(string path) => Diretorios.Contains(path);

    public string ReadAllText(string path) =>
        Arquivos.TryGetValue(path, out var conteudo)
            ? conteudo
            : throw new FileNotFoundException(path);

    public void WriteAllText(string path, string content)
    {
        Escritas.Add(path);
        Arquivos[path] = content;
    }

    public void CreateDirectory(string path) => Diretorios.Add(path);

    public void DeleteFile(string path)
    {
        Remocoes.Add(path);
        Arquivos.Remove(path);
    }

    public void MoveFile(string source, string destination, bool overwrite)
    {
        Escritas.Add(destination);
        Arquivos[destination] = Arquivos[source];
        Arquivos.Remove(source);
    }

    public long GetFileSize(string path) => Arquivos.TryGetValue(path, out var c) ? c.Length : 0;

    public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option)
        => Arquivos.Keys.Where(k => k.StartsWith(path, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<string> EnumerateDirectories(string path)
        => Diretorios.Where(d => d.StartsWith(path, StringComparison.OrdinalIgnoreCase) && !d.Equals(path, StringComparison.OrdinalIgnoreCase));
}

public sealed class FakeClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 13, 12, 0, 0, TimeSpan.FromHours(-3));
}

public sealed record LogLine(LogLevel Level, string Modulo, string Acao, string? Alvo, string Resultado);

public sealed class FakeLogger : IGameBoostLogger
{
    public List<LogLine> Linhas { get; } = new();

    public void Log(LogLevel level, string modulo, string acao, string? alvo, string resultado, Exception? ex = null)
        => Linhas.Add(new LogLine(level, modulo, acao, alvo, resultado));
}

/// <summary>Registro em memoria. Guarda tipo junto do valor para o rollback ser testavel de verdade.</summary>
public sealed class FakeRegistry : IRegistryService
{
    private readonly Dictionary<string, (object Valor, RegistryValueKindLite Kind)> _valores = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Escritas { get; } = new();
    public List<string> Remocoes { get; } = new();

    private static string Chave(RegistryRoot root, string subKey, string valueName) => $"{root}\\{subKey}\\{valueName}";

    public void Semear(RegistryRoot root, string subKey, string valueName, object valor, RegistryValueKindLite kind)
        => _valores[Chave(root, subKey, valueName)] = (valor, kind);

    public object? GetValue(RegistryRoot root, string subKey, string valueName)
        => _valores.TryGetValue(Chave(root, subKey, valueName), out var v) ? v.Valor : null;

    public void SetValue(RegistryRoot root, string subKey, string valueName, object value, RegistryValueKindLite kind)
    {
        Escritas.Add(Chave(root, subKey, valueName));
        _valores[Chave(root, subKey, valueName)] = (value, kind);
    }

    public void DeleteValue(RegistryRoot root, string subKey, string valueName)
    {
        Remocoes.Add(Chave(root, subKey, valueName));
        _valores.Remove(Chave(root, subKey, valueName));
    }

    public bool KeyExists(RegistryRoot root, string subKey)
        => _valores.Keys.Any(k => k.StartsWith($"{root}\\{subKey}\\", StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string subKey) => Array.Empty<string>();

    public IReadOnlyList<string> GetValueNames(RegistryRoot root, string subKey)
        => _valores.Keys
            .Where(k => k.StartsWith($"{root}\\{subKey}\\", StringComparison.OrdinalIgnoreCase))
            .Select(k => k[(k.LastIndexOf('\\') + 1)..])
            .ToList();
}

public sealed class FakeServices : IServiceControllerService
{
    private readonly Dictionary<string, ServiceInfo> _servicos = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Paradas { get; } = new();
    public List<string> Inicios { get; } = new();
    public List<(string Nome, ServiceStartMode Modo)> Modos { get; } = new();

    public void Semear(ServiceInfo info) => _servicos[info.Name] = info;

    public ServiceInfo? GetService(string name) => _servicos.TryGetValue(name, out var s) ? s : null;

    public IReadOnlyList<ServiceInfo> GetServices() => _servicos.Values.ToList();

    public bool Stop(string name, TimeSpan timeout)
    {
        Paradas.Add(name);
        if (!_servicos.TryGetValue(name, out var s))
            return false;

        _servicos[name] = s with { IsRunning = false };
        return true;
    }

    public bool Start(string name, TimeSpan timeout)
    {
        Inicios.Add(name);
        if (!_servicos.TryGetValue(name, out var s))
            return false;

        _servicos[name] = s with { IsRunning = true };
        return true;
    }

    public bool SetStartMode(string name, ServiceStartMode mode)
    {
        Modos.Add((name, mode));
        if (!_servicos.TryGetValue(name, out var s))
            return false;

        _servicos[name] = s with { StartMode = mode };
        return true;
    }
}

public sealed class FakePower : IPowerService
{
    public Guid Ativo { get; set; } = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public List<Guid> Trocas { get; } = new();

    public PowerPlan? GetActivePlan() => new(Ativo, "Plano " + Ativo.ToString()[..8]);

    public IReadOnlyList<PowerPlan> GetPlans() => new[]
    {
        new PowerPlan(new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"), "Balanceado"),
        new PowerPlan(new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), "Alto desempenho")
    };

    public bool SetActivePlan(Guid planId)
    {
        Trocas.Add(planId);
        Ativo = planId;
        return true;
    }
}
