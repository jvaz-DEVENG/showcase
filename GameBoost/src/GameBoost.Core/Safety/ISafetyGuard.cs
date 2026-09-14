namespace GameBoost.Core.Safety;

public enum ProtectionReason
{
    None,
    ProcessoCritico,
    AntiCheat,
    Seguranca,
    DentroDoWindows,
    OutraSessao,
    ProprioApp,
    ServicoProtegido,
    CaminhoDeSistema,
    CaminhoDoUsuario,
    SegmentoProibido,
    ListaDoUsuario
}

public sealed record ProtectionVerdict(bool Protegido, ProtectionReason Motivo, string Explicacao)
{
    public static ProtectionVerdict Livre { get; } = new(false, ProtectionReason.None, string.Empty);
}

/// <summary>
/// Regra 6: a blindagem e aplicada por todos os modulos, nao so pelo Modo Game.
/// Todo modulo consulta este guarda antes de tocar em qualquer coisa.
/// </summary>
public interface ISafetyGuard
{
    ProtectionVerdict CheckProcess(Abstractions.ProcessInfo process);
    ProtectionVerdict CheckService(string serviceName, bool apenasPausar);
    ProtectionVerdict CheckPath(string path, IReadOnlyList<string> whitelist);
    bool PodePreMarcar(Abstractions.ProcessInfo process);
}
