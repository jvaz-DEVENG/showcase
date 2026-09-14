using System.Management;
using GameBoost.Core.Logging;

namespace GameBoost.Core.Safety;

/// <summary>
/// Descobre o antivirus ativo pelo SecurityCenter2 e converte o caminho do
/// executavel em nomes de processo protegidos. A lista estatica de
/// ProtectedProcesses.Seguranca cobre o que o WMI nao reportar.
/// </summary>
public sealed class AntivirusDetector
{
    private readonly IGameBoostLogger _log;

    public AntivirusDetector(IGameBoostLogger log)
    {
        _log = log;
    }

    public IReadOnlySet<string> Detectar()
    {
        var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var classe in new[] { "AntiVirusProduct", "AntiSpywareProduct", "FirewallProduct" })
        {
            try
            {
                using var consulta = new ManagementObjectSearcher(
                    @"root\SecurityCenter2", $"SELECT displayName, pathToSignedProductExe FROM {classe}");

                foreach (var item in consulta.Get())
                {
                    using var mo = (ManagementObject)item;
                    var caminho = mo["pathToSignedProductExe"] as string;
                    if (string.IsNullOrWhiteSpace(caminho))
                        continue;

                    var nome = Path.GetFileNameWithoutExtension(caminho);
                    if (!string.IsNullOrWhiteSpace(nome))
                        nomes.Add(nome);
                }
            }
            catch (ManagementException ex)
            {
                _log.Warn("Safety", "DetectarAntivirus", classe,
                    $"WMI indisponivel, usando apenas a lista estatica: {ex.Message}");
            }
            catch (UnauthorizedAccessException)
            {
                _log.Warn("Safety", "DetectarAntivirus", classe, "sem permissao para consultar o SecurityCenter2");
            }
        }

        return nomes;
    }
}
