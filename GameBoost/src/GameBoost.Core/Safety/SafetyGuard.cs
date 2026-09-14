using GameBoost.Core.Abstractions;
using GameBoost.Core.Settings;

namespace GameBoost.Core.Safety;

public sealed class SafetyGuard : ISafetyGuard
{
    private readonly AppSettings _settings;
    private readonly int _sessaoAtual;
    private readonly int _pidProprio;
    private readonly string _windowsDir;
    private readonly IReadOnlySet<string> _antivirusDetectados;

    public SafetyGuard(
        AppSettings settings,
        int sessaoAtual,
        int pidProprio,
        IReadOnlySet<string>? antivirusDetectados = null,
        string? windowsDir = null)
    {
        _settings = settings;
        _sessaoAtual = sessaoAtual;
        _pidProprio = pidProprio;
        _antivirusDetectados = antivirusDetectados ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _windowsDir = windowsDir ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    }

    public ProtectionVerdict CheckProcess(ProcessInfo process)
    {
        var nome = ProtectedProcesses.Normalizar(process.Name);

        if (process.Pid == _pidProprio || nome.Equals("GameBoost", StringComparison.OrdinalIgnoreCase))
            return new ProtectionVerdict(true, ProtectionReason.ProprioApp,
                "O GameBoost nao encerra a si mesmo.");

        if (process.Pid <= 4)
            return new ProtectionVerdict(true, ProtectionReason.ProcessoCritico,
                "Processo do nucleo do Windows.");

        if (ProtectedProcesses.Criticos.Contains(nome))
            return new ProtectionVerdict(true, ProtectionReason.ProcessoCritico,
                $"{nome} faz parte do nucleo do Windows. Encerrar derruba a sessao.");

        if (ProtectedProcesses.AntiCheats.Contains(nome))
            return new ProtectionVerdict(true, ProtectionReason.AntiCheat,
                $"{nome} e um anti-cheat. Fechar derruba o jogo e pode gerar banimento.");

        if (ProtectedProcesses.Seguranca.Contains(nome) || _antivirusDetectados.Contains(nome))
            return new ProtectionVerdict(true, ProtectionReason.Seguranca,
                $"{nome} pertence ao antivirus ou a protecao do sistema.");

        if (process.SessionId != _sessaoAtual)
            return new ProtectionVerdict(true, ProtectionReason.OutraSessao,
                "Processo de outro usuario conectado na maquina.");

        if (!string.IsNullOrEmpty(process.ExecutablePath) && EstaDentroDe(process.ExecutablePath, _windowsDir))
            return new ProtectionVerdict(true, ProtectionReason.DentroDoWindows,
                "Executavel dentro de C:\\Windows.");

        if (_settings.NuncaEncerrar.Any(n => ProtectedProcesses.Normalizar(n).Equals(nome, StringComparison.OrdinalIgnoreCase)))
            return new ProtectionVerdict(true, ProtectionReason.ListaDoUsuario,
                "Voce marcou este app na lista de nunca encerrar.");

        return ProtectionVerdict.Livre;
    }

    /// <summary>
    /// Regra 3: nada que possa causar perda vem marcado. Jogos, launchers, IDEs,
    /// gravadores e o que o proprio usuario protegeu ficam desmarcados.
    /// </summary>
    public bool PodePreMarcar(ProcessInfo process)
    {
        if (CheckProcess(process).Protegido)
            return false;

        var nome = ProtectedProcesses.Normalizar(process.Name);

        if (_settings.SempreEncerrar.Any(n => ProtectedProcesses.Normalizar(n).Equals(nome, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (ProtectedProcesses.NuncaPreMarcados.Contains(nome))
            return false;

        if (ProtectedProcesses.ParecerInstalador(nome))
            return false;

        if (!string.IsNullOrEmpty(process.ExecutablePath) && ParecerJogo(process.ExecutablePath))
            return false;

        return true;
    }

    public ProtectionVerdict CheckService(string serviceName, bool apenasPausar)
    {
        if (apenasPausar && ProtectedServices.PausaveisTemporariamente.Contains(serviceName))
            return ProtectionVerdict.Livre;

        if (ProtectedServices.Intocaveis.Contains(serviceName))
            return new ProtectionVerdict(true, ProtectionReason.ServicoProtegido,
                $"O servico {serviceName} e essencial para o Windows, a rede, o audio ou a seguranca.");

        return ProtectionVerdict.Livre;
    }

    /// <summary>
    /// Um caminho so e liberado se estiver dentro de algum item da whitelist do modulo
    /// e nao cair em nenhuma das listas proibidas. Whitelist vazia nao libera nada.
    /// </summary>
    public ProtectionVerdict CheckPath(string path, IReadOnlyList<string> whitelist)
    {
        string completo;
        try
        {
            completo = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new ProtectionVerdict(true, ProtectionReason.SegmentoProibido, "Caminho invalido.");
        }

        var segmentos = completo.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var proibido in ProtectedPaths.SegmentosProibidos)
        {
            if (segmentos.Any(s => s.Equals(proibido, StringComparison.OrdinalIgnoreCase)))
                return new ProtectionVerdict(true, ProtectionReason.SegmentoProibido,
                    $"Caminho contem o segmento {proibido}: pode guardar saves, codigo ou arquivos sincronizados.");
        }

        var naWhitelist = whitelist.Any(raiz => !string.IsNullOrWhiteSpace(raiz) && EstaDentroDe(completo, raiz));
        if (!naWhitelist)
            return new ProtectionVerdict(true, ProtectionReason.CaminhoDeSistema,
                "Caminho fora da whitelist do modulo.");

        foreach (var pessoal in ProtectedPaths.PastasDoUsuario())
        {
            if (EstaDentroDe(completo, pessoal) && !whitelist.Any(w => EstaDentroDe(w, pessoal)))
                return new ProtectionVerdict(true, ProtectionReason.CaminhoDoUsuario,
                    "Caminho dentro de uma pasta pessoal do usuario.");
        }

        return ProtectionVerdict.Livre;
    }

    private static bool ParecerJogo(string executablePath)
    {
        var p = executablePath.Replace('/', '\\');
        string[] marcadores =
        {
            @"\steamapps\common\", @"\epic games\", @"\riot games\",
            @"\battle.net\", @"\gog galaxy\games\", @"\gog games\",
            @"\xboxgames\", @"\ea games\", @"\origin games\", @"\ubisoft\"
        };
        return marcadores.Any(m => p.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Comparacao por segmento: C:\WindowsApps nao pode contar como dentro de C:\Windows.</summary>
    private static bool EstaDentroDe(string caminho, string raiz)
    {
        if (string.IsNullOrWhiteSpace(raiz))
            return false;

        try
        {
            var c = Path.GetFullPath(caminho).TrimEnd(Path.DirectorySeparatorChar);
            var r = Path.GetFullPath(raiz).TrimEnd(Path.DirectorySeparatorChar);

            if (c.Equals(r, StringComparison.OrdinalIgnoreCase))
                return true;

            return c.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
