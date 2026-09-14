namespace GameBoost.Core.Abstractions;

/// <summary>Instantaneo de um processo. Struct de dados pura, sem handle preso.</summary>
public sealed record ProcessInfo(
    int Pid,
    string Name,
    string? ExecutablePath,
    string? CommandLine,
    long WorkingSetBytes,
    string? WindowTitle,
    string? Company,
    int SessionId);

public interface IProcessService
{
    IReadOnlyList<ProcessInfo> GetProcesses();
    ProcessInfo? GetProcess(int pid);

    /// <summary>Pede o fechamento normal (WM_CLOSE). Retorna true se o processo saiu dentro do timeout.</summary>
    bool TryCloseGracefully(int pid, TimeSpan timeout);

    /// <summary>Finaliza a forca. So deve ser chamado depois de TryCloseGracefully falhar.</summary>
    bool Kill(int pid);

    bool SetPriority(int pid, ProcessPriority priority);
    ProcessPriority? GetPriority(int pid);

    /// <summary>Relanca um executavel na sessao do usuario. Usado para reabrir os apps essenciais.</summary>
    bool Start(string executablePath, string? arguments, string? workingDirectory);

    /// <summary>EmptyWorkingSet no processo. Retorna quantos bytes sairam do working set.</summary>
    long TrimWorkingSet(int pid);
}

/// <summary>Realtime nao existe de proposito: travaria o Windows (secao 1 do spec).</summary>
public enum ProcessPriority
{
    Idle,
    BelowNormal,
    Normal,
    AboveNormal,
    High
}
