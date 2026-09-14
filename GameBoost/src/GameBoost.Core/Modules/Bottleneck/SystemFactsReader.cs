using System.Management;
using System.Runtime.InteropServices;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Services;
using GameBoost.Core.Settings;

namespace GameBoost.Core.Modules.Bottleneck;

/// <summary>
/// Le os fatos que nao mudam a cada segundo: plano de energia, driver de video,
/// taxa do monitor, VBS, pentes de memoria, build do Windows.
///
/// Roda em cache com validade propria para nao pagar WMI a cada tique da coleta.
/// </summary>
public sealed class SystemFactsReader
{
    private static readonly TimeSpan Validade = TimeSpan.FromMinutes(2);

    private readonly IPowerService _power;
    private readonly IRegistryService _registro;
    private readonly ISettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly IGameBoostLogger _log;
    private readonly IClock _relogio;

    private SystemFacts? _cache;
    private DateTimeOffset _lidoEm;

    public SystemFactsReader(
        IPowerService power,
        IRegistryService registro,
        ISettingsStore settingsStore,
        AppSettings settings,
        IGameBoostLogger log,
        IClock relogio)
    {
        _power = power;
        _registro = registro;
        _settingsStore = settingsStore;
        _settings = settings;
        _log = log;
        _relogio = relogio;
    }

    public SystemFacts Ler(bool forcar = false)
    {
        if (!forcar && _cache is not null && _relogio.Now - _lidoEm < Validade)
            return _cache;

        var plano = Seguro(() => _power.GetActivePlan(), null);
        var (fabricante, versao, data) = LerDriverDeVideo();
        var (atual, maxima) = LerTaxaDeAtualizacao();
        var build = LerBuildDoWindows();

        var fatos = new SystemFacts
        {
            PlanoDeEnergiaAtivo = plano?.Id,
            NomeDoPlanoDeEnergia = plano?.Name,
            PlanoEhBalanceado = plano is not null && plano.Id == WindowsPowerService.Balanceado,

            BuildDoWindows = build,
            BuildAnteriormenteVisto = _settings.UltimaBuildDoWindows,
            TweaksDesfeitosPorUpdate = ContarTweaksDesfeitos(build),

            FabricanteDaGpu = fabricante,
            VersaoDoDriver = versao,
            DataDoDriver = data,

            TaxaDeAtualizacaoAtual = atual,
            TaxaDeAtualizacaoMaxima = maxima,

            VbsAtivo = LerVbs(),
            GameDvrAtivo = LerDword(RegistryRoot.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled") != 0,
            GameBarOverlayAtivo = LerDword(RegistryRoot.CurrentUser, @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled") != 0,

            ItensNaInicializacao = ContarItensDeInicializacao(),
            PentesDeMemoria = ContarPentesDeMemoria()
        };

        _cache = fatos;
        _lidoEm = _relogio.Now;

        // Guarda a build para a proxima execucao comparar.
        if (!string.IsNullOrEmpty(build) && _settings.UltimaBuildDoWindows != build)
        {
            _settings.UltimaBuildDoWindows = build;
            Seguro<object?>(() => { _settingsStore.Save(_settings); return null; }, null);
        }

        return fatos;
    }

    /// <summary>
    /// Quantos tweaks do GameBoost nao batem mais com o que ele aplicou. Uma
    /// atualizacao grande do Windows reseta Game Mode, Game DVR e Game Bar.
    /// </summary>
    private int ContarTweaksDesfeitos(string? buildAtual)
    {
        if (string.IsNullOrEmpty(buildAtual)
            || string.IsNullOrEmpty(_settings.UltimaBuildDoWindows)
            || _settings.UltimaBuildDoWindows == buildAtual)
        {
            return 0;
        }

        var desfeitos = 0;
        foreach (var tweak in GameMode.SystemSilencer.Tweaks)
        {
            var valor = _registro.GetValue(tweak.Root, tweak.SubKey, tweak.ValueName);
            if (valor is int atual && atual != tweak.ValorDesejado)
                desfeitos++;
        }

        return desfeitos;
    }

    private int LerDword(RegistryRoot root, string chave, string valor)
    {
        var lido = Seguro(() => _registro.GetValue(root, chave, valor), null);
        return lido is int i ? i : -1;
    }

    private bool LerVbs()
    {
        try
        {
            using var consulta = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\DeviceGuard",
                "SELECT SecurityServicesRunning FROM Win32_DeviceGuard");

            foreach (var item in consulta.Get())
            {
                using var mo = (ManagementObject)item;
                if (mo["SecurityServicesRunning"] is not uint[] servicos)
                    continue;

                // 1 = Credential Guard, 2 = HVCI (Memory Integrity).
                if (servicos.Contains(2u))
                    return true;
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException)
        {
            _log.Warn("Bottleneck", "VBS", null, $"Win32_DeviceGuard indisponivel: {ex.Message}");
        }

        return false;
    }

    private (string? Fabricante, string? Versao, DateTimeOffset? Data) LerDriverDeVideo()
    {
        try
        {
            using var consulta = new ManagementObjectSearcher(
                "SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController");

            foreach (var item in consulta.Get())
            {
                using var mo = (ManagementObject)item;
                var nome = mo["Name"] as string ?? string.Empty;

                // Ignora adaptadores virtuais (RDP, Parsec, Meta).
                if (nome.Contains("Remote", StringComparison.OrdinalIgnoreCase)
                    || nome.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fabricante = nome.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
                    : nome.Contains("AMD", StringComparison.OrdinalIgnoreCase) || nome.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "AMD"
                    : nome.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel"
                    : null;

                DateTimeOffset? data = null;
                if (mo["DriverDate"] is string bruto && bruto.Length >= 8)
                {
                    // WMI datetime: yyyyMMddHHmmss.ffffff+UUU
                    if (DateTime.TryParseExact(bruto[..8], "yyyyMMdd", null,
                            System.Globalization.DateTimeStyles.None, out var parseada))
                    {
                        data = new DateTimeOffset(parseada, TimeSpan.Zero);
                    }
                }

                return (fabricante, mo["DriverVersion"] as string, data);
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException)
        {
            _log.Warn("Bottleneck", "Driver", null, ex.Message);
        }

        return (null, null, null);
    }

    // ---------------- Monitor ----------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    private const int ENUM_CURRENT_SETTINGS = -1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string? deviceName, int modeNum, ref DEVMODE devMode);

    /// <summary>
    /// Taxa atual e a maior que o monitor aceita na resolucao em uso. O Windows
    /// volta para 60 Hz com frequencia depois de atualizar driver.
    /// </summary>
    private (int Atual, int Maxima) LerTaxaDeAtualizacao()
    {
        try
        {
            var atual = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref atual))
                return (0, 0);

            var taxaAtual = (int)atual.dmDisplayFrequency;
            var maxima = taxaAtual;

            for (var i = 0; ; i++)
            {
                var modo = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
                if (!EnumDisplaySettingsW(null, i, ref modo))
                    break;

                // So compara modos da resolucao em uso: 640x480 a 200 Hz nao conta.
                if (modo.dmPelsWidth == atual.dmPelsWidth && modo.dmPelsHeight == atual.dmPelsHeight)
                    maxima = Math.Max(maxima, (int)modo.dmDisplayFrequency);
            }

            return (taxaAtual, maxima);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return (0, 0);
        }
    }

    private int ContarItensDeInicializacao()
    {
        var total = 0;

        foreach (var (root, chave) in new[]
                 {
                     (RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run"),
                     (RegistryRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                     (RegistryRoot.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run")
                 })
        {
            total += Seguro(() => _registro.GetValueNames(root, chave).Count, 0);
        }

        foreach (var pasta in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
                 })
        {
            total += Seguro(() =>
            {
                if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta))
                    return 0;

                return Directory.EnumerateFiles(pasta)
                    .Count(f => !f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
            }, 0);
        }

        return total;
    }

    private int ContarPentesDeMemoria()
    {
        try
        {
            using var consulta = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
            return consulta.Get().Count;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private string? LerBuildDoWindows()
    {
        var chave = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var display = Seguro(() => _registro.GetValue(RegistryRoot.LocalMachine, chave, "DisplayVersion"), null) as string;
        var build = Seguro(() => _registro.GetValue(RegistryRoot.LocalMachine, chave, "CurrentBuildNumber"), null) as string;

        if (!string.IsNullOrEmpty(display))
            return display;

        return string.IsNullOrEmpty(build) ? Environment.OSVersion.Version.Build.ToString() : build;
    }

    private T Seguro<T>(Func<T> acao, T padrao)
    {
        try
        {
            return acao();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                      or ManagementException
                                      or IOException
                                      or InvalidOperationException
                                      or System.Security.SecurityException)
        {
            _log.Warn("Bottleneck", "Fatos", null, ex.Message);
            return padrao;
        }
    }
}
