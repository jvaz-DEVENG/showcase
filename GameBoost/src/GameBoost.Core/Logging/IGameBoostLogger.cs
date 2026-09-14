namespace GameBoost.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>
/// Regra 8: toda acao grava linha estruturada (timestamp, modulo, acao, alvo, resultado).
/// </summary>
public interface IGameBoostLogger
{
    void Log(LogLevel level, string modulo, string acao, string? alvo, string resultado, Exception? ex = null);
}

public static class LoggerExtensions
{
    public static void Info(this IGameBoostLogger log, string modulo, string acao, string? alvo, string resultado)
        => log.Log(LogLevel.Info, modulo, acao, alvo, resultado);

    public static void Warn(this IGameBoostLogger log, string modulo, string acao, string? alvo, string resultado)
        => log.Log(LogLevel.Warn, modulo, acao, alvo, resultado);

    public static void Error(this IGameBoostLogger log, string modulo, string acao, string? alvo, string resultado, Exception? ex = null)
        => log.Log(LogLevel.Error, modulo, acao, alvo, resultado, ex);
}
