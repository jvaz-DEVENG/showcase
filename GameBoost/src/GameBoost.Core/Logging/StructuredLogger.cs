using System.Text;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Settings;

namespace GameBoost.Core.Logging;

/// <summary>
/// Log texto legivel, rotacionado em 5 MB x 5 arquivos (secao 7 do spec).
/// Serializado por lock: as varreduras rodam em varias Tasks ao mesmo tempo.
/// </summary>
public sealed class StructuredLogger : IGameBoostLogger
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private const int MaxArquivos = 5;

    private readonly AppPaths _paths;
    private readonly IClock _clock;
    private readonly object _gate = new();

    public StructuredLogger(AppPaths paths, IClock clock)
    {
        _paths = paths;
        _clock = clock;
    }

    public void Log(LogLevel level, string modulo, string acao, string? alvo, string resultado, Exception? ex = null)
    {
        var linha = new StringBuilder()
            .Append(_clock.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" | ").Append(level.ToString().ToUpperInvariant().PadRight(5))
            .Append(" | ").Append(modulo)
            .Append(" | ").Append(acao)
            .Append(" | ").Append(string.IsNullOrEmpty(alvo) ? "-" : alvo)
            .Append(" | ").Append(resultado);

        if (ex is not null)
            linha.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_paths.RootDirectory);
                Rotacionar();
                File.AppendAllText(_paths.LogFile, linha.ToString() + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Log nunca derruba o app. Falha de disco e engolida de proposito.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void Rotacionar()
    {
        var atual = _paths.LogFile;
        if (!File.Exists(atual) || new FileInfo(atual).Length < MaxBytes)
            return;

        var maisAntigo = $"{atual}.{MaxArquivos}";
        if (File.Exists(maisAntigo))
            File.Delete(maisAntigo);

        for (var i = MaxArquivos - 1; i >= 1; i--)
        {
            var origem = $"{atual}.{i}";
            if (File.Exists(origem))
                File.Move(origem, $"{atual}.{i + 1}", overwrite: true);
        }

        File.Move(atual, $"{atual}.1", overwrite: true);
    }
}
