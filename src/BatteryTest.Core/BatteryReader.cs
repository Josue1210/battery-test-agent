using System.Text;
using BatteryTest.Core.Models;
using BatteryTest.Core.Wmi;

namespace BatteryTest.Core;

/// <summary>
/// Lee las baterías desde root\WMI (BatteryStatus, BatteryFullChargedCapacity,
/// BatteryStaticData, BatteryCycleCount) y descarta valores vacíos o absurdos.
/// </summary>
public static class BatteryReader
{
    // Rangos plausibles para una laptop. Fuera de ellos el valor se considera inválido.
    private const int MinCapacityMWh = 1_000;      // 1 Wh
    private const int MaxCapacityMWh = 500_000;    // 500 Wh
    private const int MaxRateMW = 300_000;         // 300 W
    private const int MinVoltageMV = 3_000;
    private const int MaxVoltageMV = 30_000;
    private const int MaxCycleCount = 10_000;

    public static IReadOnlyList<BatteryReading> Read()
    {
        var status = ByInstance(WmiQuery.Run(WmiQuery.Wmi, "SELECT * FROM BatteryStatus"));
        var full = ByInstance(WmiQuery.Run(WmiQuery.Wmi, "SELECT * FROM BatteryFullChargedCapacity"));
        var stat = ByInstance(WmiQuery.Run(WmiQuery.Wmi, "SELECT * FROM BatteryStaticData"));
        var cycles = ByInstance(WmiQuery.Run(WmiQuery.Wmi, "SELECT * FROM BatteryCycleCount"));

        var instances = status.Keys.Union(full.Keys).Union(stat.Keys).Order(StringComparer.Ordinal).ToList();
        var readings = new List<BatteryReading>(instances.Count);
        foreach (var instance in instances)
        {
            var s = Find(status, instance, instances.Count);
            var f = Find(full, instance, instances.Count);
            var d = Find(stat, instance, instances.Count);
            var c = Find(cycles, instance, instances.Count);

            var remaining = InRange(s?.GetLong("RemainingCapacity"), 0, MaxCapacityMWh);
            readings.Add(new BatteryReading
            {
                InstanceName = instance,
                Manufacturer = d?.GetString("ManufactureName"),
                DeviceName = d?.GetString("DeviceName"),
                Chemistry = DecodeChemistry(d?.GetLong("Chemistry")),
                DesignedCapacityMWh = InRange(d?.GetLong("DesignedCapacity"), MinCapacityMWh, MaxCapacityMWh),
                FullChargedCapacityMWh = InRange(f?.GetLong("FullChargedCapacity"), MinCapacityMWh, MaxCapacityMWh),
                RemainingCapacityMWh = remaining,
                ChargeRateMW = Rate(s?.GetLong("ChargeRate")),
                DischargeRateMW = Rate(s?.GetLong("DischargeRate")),
                VoltageMV = InRange(s?.GetLong("Voltage"), MinVoltageMV, MaxVoltageMV),
                CycleCount = InRange(c?.GetLong("CycleCount"), 0, MaxCycleCount),
                PowerOnline = s?.GetBool("PowerOnline"),
                Charging = s?.GetBool("Charging"),
                Discharging = s?.GetBool("Discharging"),
            });
        }
        return readings;
    }

    private static Dictionary<string, WmiRow> ByInstance(IReadOnlyList<WmiRow> rows)
    {
        var map = new Dictionary<string, WmiRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row.GetString("InstanceName") is { } name)
                map.TryAdd(name, row);
        }
        return map;
    }

    /// <summary>
    /// Busca la fila de la misma batería. Si los InstanceName no coinciden entre clases
    /// (pasa en algunos firmwares) y solo hay una batería, usa la única fila disponible.
    /// </summary>
    private static WmiRow? Find(Dictionary<string, WmiRow> rows, string instance, int batteryCount)
    {
        if (rows.TryGetValue(instance, out var row))
            return row;
        return batteryCount == 1 && rows.Count == 1 ? rows.Values.First() : null;
    }

    private static int? InRange(long? value, int min, int max) =>
        value is { } v && v >= min && v <= max ? (int)v : null;

    /// <summary>
    /// Algunos firmwares reportan la tasa como entero con signo dentro de un uint32
    /// (p. ej. -15000 llega como 4294952296). Se reinterpreta y se toma el valor absoluto.
    /// </summary>
    private static int? Rate(long? value)
    {
        if (value is not { } v)
            return null;
        if (v > int.MaxValue && v <= uint.MaxValue)
            v = Math.Abs((long)unchecked((int)(uint)v));
        return InRange(v, 0, MaxRateMW);
    }

    /// <summary>La química viene como 4 caracteres ASCII empaquetados en un uint32 (p. ej. "LION").</summary>
    private static string? DecodeChemistry(long? value)
    {
        if (value is not { } v || v <= 0 || v > uint.MaxValue)
            return null;
        var text = Encoding.ASCII.GetString(BitConverter.GetBytes((uint)v)).Trim('\0', ' ');
        return text.Length > 0 && text.All(char.IsAsciiLetterOrDigit) ? text : null;
    }
}
