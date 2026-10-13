namespace BatteryTest.Core.Models;

/// <summary>
/// Datos del equipo. No incluye nombre del equipo, usuario de Windows ni números de serie.
/// </summary>
public sealed record HardwareInfo
{
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? SystemFamily { get; init; }
    public string? OperatingSystem { get; init; }
    public CpuInfo? Cpu { get; init; }
    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];
    public MemoryInfo? Memory { get; init; }
}

public sealed record CpuInfo(string Name, int? Cores, int? LogicalProcessors, int? BaseClockMHz);

public sealed record GpuInfo(string Name, string? DriverVersion);

public sealed record MemoryInfo(long TotalBytes, int Modules, int? SpeedMTs, string? Type);

public sealed record CpuSample(double? UsagePercent, int? FrequencyMHz);

public sealed record PowerStatus(bool? AcOnline, int? BatteryPercent, TimeSpan? EstimatedRemaining, bool BatterySaverOn, bool HasBattery);
