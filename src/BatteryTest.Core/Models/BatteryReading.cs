namespace BatteryTest.Core.Models;

/// <summary>
/// Una lectura de una batería. Los campos son null cuando el fabricante no los reporta
/// o reporta valores absurdos. El número de serie no se lee a propósito.
/// </summary>
public sealed record BatteryReading
{
    /// <summary>Identificador WMI de la batería (solo uso local, no se envía).</summary>
    public required string InstanceName { get; init; }
    public string? Manufacturer { get; init; }
    public string? DeviceName { get; init; }
    public string? Chemistry { get; init; }

    public int? DesignedCapacityMWh { get; init; }
    public int? FullChargedCapacityMWh { get; init; }
    public int? RemainingCapacityMWh { get; init; }
    public int? ChargeRateMW { get; init; }
    public int? DischargeRateMW { get; init; }
    public int? VoltageMV { get; init; }
    public int? CycleCount { get; init; }

    public bool? PowerOnline { get; init; }
    public bool? Charging { get; init; }
    public bool? Discharging { get; init; }

    /// <summary>Salud = capacidad actual / capacidad de diseño, en %.</summary>
    public double? HealthPercent =>
        Ratio(FullChargedCapacityMWh, DesignedCapacityMWh) is { } r && r is >= 0.1 and <= 1.2 ? r * 100 : null;

    /// <summary>
    /// Capacidad actual igual a la de diseño y 0 ciclos. Es lo normal en una batería nueva,
    /// pero también lo que devuelven los firmwares que no reportan el desgaste: con una sola
    /// lectura no se pueden distinguir.
    /// </summary>
    public bool WearPossiblyUnreported =>
        FullChargedCapacityMWh is { } full && full == DesignedCapacityMWh && CycleCount is null or 0;

    /// <summary>Carga restante respecto a la capacidad actual, en %.</summary>
    public double? ChargePercent =>
        Ratio(RemainingCapacityMWh, FullChargedCapacityMWh) is { } r ? Math.Clamp(r, 0, 1) * 100 : null;

    private static double? Ratio(int? numerator, int? denominator) =>
        numerator is { } n && denominator is > 0 and { } d ? (double)n / d : null;
}
