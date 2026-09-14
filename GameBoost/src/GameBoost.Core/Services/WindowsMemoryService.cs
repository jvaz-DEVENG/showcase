using System.Runtime.InteropServices;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Native;

namespace GameBoost.Core.Services;

public sealed class WindowsMemoryService : IMemoryService
{
    private readonly IGameBoostLogger _log;
    private bool _privilegioHabilitado;

    public WindowsMemoryService(IGameBoostLogger log)
    {
        _log = log;
    }

    public MemorySnapshot GetSnapshot()
    {
        var status = new NativeMethods.MEMORYSTATUSEX();
        if (!NativeMethods.GlobalMemoryStatusEx(status))
            return new MemorySnapshot(0, 0, 0);

        return new MemorySnapshot(
            TotalBytes: (long)status.ullTotalPhys,
            AvailableBytes: (long)status.ullAvailPhys,
            StandbyBytes: 0);
    }

    /// <summary>
    /// Purga a Standby List: a memoria que o Windows guarda por precaucao com
    /// dados ja usados. Ajuda quando falta RAM; nao aumenta FPS por si so.
    /// </summary>
    public bool PurgeStandbyList()
    {
        if (!_privilegioHabilitado)
        {
            _privilegioHabilitado = NativeMethods.EnablePrivilege(NativeMethods.SE_PROFILE_SINGLE_PROCESS_NAME);
            if (!_privilegioHabilitado)
            {
                _log.Warn("Memory", "PurgeStandbyList", null,
                    "sem SeProfileSingleProcessPrivilege: rode como administrador");
                return false;
            }
        }

        var comando = NativeMethods.MemoryPurgeStandbyList;
        var buffer = Marshal.AllocHGlobal(sizeof(int));

        try
        {
            Marshal.WriteInt32(buffer, comando);
            var status = NativeMethods.NtSetSystemInformation(
                NativeMethods.SystemMemoryListInformation, buffer, sizeof(int));

            if (status != 0)
            {
                _log.Warn("Memory", "PurgeStandbyList", null, $"NtSetSystemInformation retornou 0x{status:X8}");
                return false;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
