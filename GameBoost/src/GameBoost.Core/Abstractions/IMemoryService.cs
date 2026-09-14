namespace GameBoost.Core.Abstractions;

public sealed record MemorySnapshot(long TotalBytes, long AvailableBytes, long StandbyBytes)
{
    public long UsedBytes => TotalBytes - AvailableBytes;
    public double UsedPercent => TotalBytes == 0 ? 0 : UsedBytes * 100.0 / TotalBytes;
}

public interface IMemoryService
{
    MemorySnapshot GetSnapshot();

    /// <summary>Purga a Standby List do kernel. Exige SeProfileSingleProcessPrivilege (admin).</summary>
    bool PurgeStandbyList();
}
