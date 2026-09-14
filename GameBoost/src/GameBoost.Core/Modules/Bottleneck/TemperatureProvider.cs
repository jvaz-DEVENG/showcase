using System.Management;
using GameBoost.Core.Logging;

namespace GameBoost.Core.Modules.Bottleneck;

public interface ITemperatureProvider
{
    Medida LerCpu();
    Medida LerGpu();

    /// <summary>Explica por que a temperatura nao esta disponivel, para a UI ser honesta.</summary>
    string? MotivoIndisponivel { get; }
}

/// <summary>
/// Temperatura pelo WMI (MSAcpi_ThermalZoneTemperature).
///
/// Funciona em parte das maquinas e so para a zona termica da placa-mae, que
/// nem sempre acompanha o die da CPU. Placas de video nao aparecem aqui.
/// A leitura boa depende de LibreHardwareMonitorLib, que precisa carregar um
/// driver em modo kernel; fica para quando a Fase 1 estiver validada, e ate la
/// a UI diz "indisponivel" em vez de inventar numero (regra 4).
/// </summary>
public sealed class WmiTemperatureProvider : ITemperatureProvider
{
    private readonly IGameBoostLogger _log;
    private bool _tentou;

    public WmiTemperatureProvider(IGameBoostLogger log)
    {
        _log = log;
    }

    public string? MotivoIndisponivel { get; private set; } =
        "Temperatura exige um driver de sensores. O Windows nao expoe o valor do die da CPU sem ele.";

    public Medida LerCpu()
    {
        try
        {
            using var consulta = new ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            foreach (var item in consulta.Get())
            {
                using var mo = (ManagementObject)item;
                var decimosDeKelvin = Convert.ToDouble(mo["CurrentTemperature"]);

                // O WMI reporta em decimos de Kelvin.
                var celsius = decimosDeKelvin / 10.0 - 273.15;

                // Zona termica as vezes devolve valor absurdo; nao vale mentir.
                if (celsius is > 0 and < 125)
                {
                    MotivoIndisponivel = null;
                    return Medida.De(celsius);
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException)
        {
            if (!_tentou)
                _log.Warn("Bottleneck", "Temperatura", null, $"MSAcpi_ThermalZoneTemperature indisponivel: {ex.Message}");
        }
        finally
        {
            _tentou = true;
        }

        return Medida.Indisponivel;
    }

    /// <summary>Sem driver de sensores nao ha como ler a GPU. Honesto e dizer isso.</summary>
    public Medida LerGpu() => Medida.Indisponivel;
}
