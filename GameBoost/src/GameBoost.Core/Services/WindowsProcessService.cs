using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Native;

namespace GameBoost.Core.Services;

public sealed class WindowsProcessService : IProcessService
{
    private readonly IGameBoostLogger _log;

    public WindowsProcessService(IGameBoostLogger log)
    {
        _log = log;
    }

    public IReadOnlyList<ProcessInfo> GetProcesses()
    {
        var titulos = MapearTitulosDeJanela();
        var linhasDeComando = MapearLinhasDeComando();
        var resultado = new List<ProcessInfo>(256);

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                resultado.Add(Montar(p, titulos, linhasDeComando));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
            {
                // Processo morreu entre a listagem e a leitura. Normal, ignora.
            }
            finally
            {
                p.Dispose();
            }
        }

        return resultado;
    }

    public ProcessInfo? GetProcess(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return Montar(p, MapearTitulosDeJanela(), MapearLinhasDeComando());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static ProcessInfo Montar(
        Process p,
        IReadOnlyDictionary<int, string> titulos,
        IReadOnlyDictionary<int, (string? CommandLine, string? Path)> detalhes)
    {
        detalhes.TryGetValue(p.Id, out var extra);
        titulos.TryGetValue(p.Id, out var titulo);

        NativeMethods.ProcessIdToSessionId(p.Id, out var sessionId);

        return new ProcessInfo(
            Pid: p.Id,
            Name: p.ProcessName,
            ExecutablePath: extra.Path ?? CaminhoSeguro(p),
            CommandLine: extra.CommandLine,
            WorkingSetBytes: SafeWorkingSet(p),
            WindowTitle: string.IsNullOrWhiteSpace(titulo) ? null : titulo,
            Company: null,
            SessionId: sessionId);
    }

    private static long SafeWorkingSet(Process p)
    {
        try
        {
            return p.WorkingSet64;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return 0;
        }
    }

    private static string? CaminhoSeguro(Process p)
    {
        try
        {
            return p.MainModule?.FileName;
        }
        catch (Exception)
        {
            // Processo de outra sessao, protegido ou de bitness diferente.
            return null;
        }
    }

    /// <summary>
    /// Uma unica consulta WMI traz caminho e linha de comando de todos os processos.
    /// Consultar um a um custaria dezenas de segundos numa maquina cheia.
    /// </summary>
    private static Dictionary<int, (string? CommandLine, string? Path)> MapearLinhasDeComando()
    {
        var mapa = new Dictionary<int, (string?, string?)>();
        try
        {
            using var consulta = new ManagementObjectSearcher(
                "SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process");

            foreach (var item in consulta.Get())
            {
                using var mo = (ManagementObject)item;
                var pid = Convert.ToInt32(mo["ProcessId"]);
                mapa[pid] = (mo["CommandLine"] as string, mo["ExecutablePath"] as string);
            }
        }
        catch (ManagementException)
        {
            // WMI indisponivel: segue sem linha de comando. A reabertura de essenciais
            // fica sem argumentos, mas nada quebra.
        }

        return mapa;
    }

    private static Dictionary<int, string> MapearTitulosDeJanela()
    {
        var mapa = new Dictionary<int, string>();
        var buffer = new StringBuilder(512);

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            var tamanho = NativeMethods.GetWindowTextW(hWnd, buffer, buffer.Capacity);
            if (tamanho == 0)
                return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            if (pid != 0 && !mapa.ContainsKey(pid))
                mapa[pid] = buffer.ToString();

            return true;
        }, IntPtr.Zero);

        return mapa;
    }

    /// <summary>
    /// Pede o fechamento normal primeiro. Apps com trabalho aberto mostram o
    /// proprio dialogo de salvar e o usuario decide -- e por isso que o Kill so
    /// vem depois (secao 1 do spec).
    /// </summary>
    public bool TryCloseGracefully(int pid, TimeSpan timeout)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited)
                return true;

            var pediu = false;
            if (p.MainWindowHandle != IntPtr.Zero)
                pediu = p.CloseMainWindow();

            if (!pediu)
                pediu = PostarWmCloseEmTodasAsJanelas(pid);

            if (!pediu)
                return false;

            return p.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            return true; // ja nao existe
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static bool PostarWmCloseEmTodasAsJanelas(int pid)
    {
        var enviou = false;
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out var donoPid);
            if (donoPid == pid && NativeMethods.IsWindowVisible(hWnd))
            {
                NativeMethods.PostMessageW(hWnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                enviou = true;
            }

            return true;
        }, IntPtr.Zero);

        return enviou;
    }

    public bool Kill(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            return p.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _log.Warn("Process", "Kill", pid.ToString(), $"falhou: {ex.Message}");
            return false;
        }
    }

    public bool SetPriority(int pid, ProcessPriority priority)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.PriorityClass = Converter(priority);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _log.Warn("Process", "SetPriority", pid.ToString(), $"falhou: {ex.Message}");
            return false;
        }
    }

    public bool SetAffinity(int pid, IReadOnlyList<int> nucleos)
    {
        try
        {
            using var p = Process.GetProcessById(pid);

            // Lista vazia = devolver a maquina inteira. E assim que o perfil
            // reverte a afinidade ao fim do jogo.
            if (nucleos.Count == 0)
            {
                p.ProcessorAffinity = (IntPtr)MascaraCompleta();
                return true;
            }

            var total = Environment.ProcessorCount;

            // Indice fora da maquina atual derrubaria o processo. Um perfil
            // gravado num PC de 16 nucleos vai parar num de 4 mais cedo ou
            // mais tarde.
            if (nucleos.Any(n => n < 0 || n >= total))
            {
                _log.Warn("Process", "SetAffinity", pid.ToString(),
                    $"perfil pede nucleo fora dos {total} desta maquina");
                return false;
            }

            long mascara = 0;

            foreach (var n in nucleos)
                mascara |= 1L << n;

            p.ProcessorAffinity = (IntPtr)mascara;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _log.Warn("Process", "SetAffinity", pid.ToString(), $"falhou: {ex.Message}");
            return false;
        }
    }

    public IReadOnlyList<int> GetAffinity(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            var mascara = (long)p.ProcessorAffinity;
            var nucleos = new List<int>();

            for (var i = 0; i < Environment.ProcessorCount; i++)
            {
                if ((mascara & (1L << i)) != 0)
                    nucleos.Add(i);
            }

            return nucleos;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Array.Empty<int>();
        }
    }

    private static long MascaraCompleta()
    {
        long mascara = 0;

        for (var i = 0; i < Environment.ProcessorCount; i++)
            mascara |= 1L << i;

        return mascara;
    }

    public ProcessPriority? GetPriority(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return Converter(p.PriorityClass);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public bool Start(string executablePath, string? arguments, string? workingDirectory)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true,
                WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(executablePath) ?? string.Empty
            };

            if (!string.IsNullOrWhiteSpace(arguments))
                info.Arguments = arguments;

            using var iniciado = Process.Start(info);
            return iniciado is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            _log.Warn("Process", "Start", executablePath, $"falhou: {ex.Message}");
            return false;
        }
    }

    public long TrimWorkingSet(int pid)
    {
        var handle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION | NativeMethods.PROCESS_SET_QUOTA,
            false, pid);

        if (handle == IntPtr.Zero)
            return 0;

        try
        {
            var tamanho = (uint)Marshal.SizeOf<NativeMethods.PROCESS_MEMORY_COUNTERS>();

            if (!NativeMethods.GetProcessMemoryInfo(handle, out var antes, tamanho))
                return 0;

            if (!NativeMethods.EmptyWorkingSet(handle))
                return 0;

            if (!NativeMethods.GetProcessMemoryInfo(handle, out var depois, tamanho))
                return 0;

            var liberado = (long)antes.WorkingSetSize.ToUInt64() - (long)depois.WorkingSetSize.ToUInt64();
            return liberado > 0 ? liberado : 0;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static ProcessPriorityClass Converter(ProcessPriority p) => p switch
    {
        ProcessPriority.Idle => ProcessPriorityClass.Idle,
        ProcessPriority.BelowNormal => ProcessPriorityClass.BelowNormal,
        ProcessPriority.Normal => ProcessPriorityClass.Normal,
        ProcessPriority.AboveNormal => ProcessPriorityClass.AboveNormal,
        ProcessPriority.High => ProcessPriorityClass.High,
        _ => ProcessPriorityClass.Normal
    };

    private static ProcessPriority Converter(ProcessPriorityClass p) => p switch
    {
        ProcessPriorityClass.Idle => ProcessPriority.Idle,
        ProcessPriorityClass.BelowNormal => ProcessPriority.BelowNormal,
        ProcessPriorityClass.AboveNormal => ProcessPriority.AboveNormal,
        ProcessPriorityClass.High => ProcessPriority.High,
        ProcessPriorityClass.RealTime => ProcessPriority.High,
        _ => ProcessPriority.Normal
    };
}
