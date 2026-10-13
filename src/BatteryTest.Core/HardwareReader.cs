using BatteryTest.Core.Models;
using BatteryTest.Core.Wmi;

namespace BatteryTest.Core;

/// <summary>
/// Lee el hardware desde root\CIMV2. Solo pide las propiedades que se necesitan:
/// nunca el nombre del equipo, el usuario ni números de serie.
/// </summary>
public static class HardwareReader
{
    // Textos de relleno que dejan algunos fabricantes en el firmware.
    private static readonly string[] Placeholders =
    [
        "To Be Filled By O.E.M.", "To be filled by O.E.M.", "System Product Name", "System manufacturer",
        "System Version", "Default string", "Not Applicable", "Not Specified", "None", "O.E.M.", "INVALID", "x.x",
    ];

    public static HardwareInfo Read()
    {
        var system = WmiQuery.Run(WmiQuery.Cimv2, "SELECT Manufacturer, Model, SystemFamily FROM Win32_ComputerSystem").FirstOrDefault();
        var product = WmiQuery.Run(WmiQuery.Cimv2, "SELECT Version FROM Win32_ComputerSystemProduct").FirstOrDefault();
        var os = WmiQuery.Run(WmiQuery.Cimv2, "SELECT Caption, BuildNumber, OSArchitecture FROM Win32_OperatingSystem").FirstOrDefault();

        var manufacturer = Clean(system?.GetString("Manufacturer"));
        var model = Clean(system?.GetString("Model"));
        // Lenovo pone un código en Model (p. ej. "20XW0055US") y el nombre comercial en Version.
        if (manufacturer?.Contains("LENOVO", StringComparison.OrdinalIgnoreCase) == true
            && Clean(product?.GetString("Version")) is { } lenovoName)
            model = lenovoName;

        return new HardwareInfo
        {
            Manufacturer = manufacturer,
            Model = model,
            SystemFamily = Clean(system?.GetString("SystemFamily")),
            OperatingSystem = os is null ? null : $"{os.GetString("Caption")} (build {os.GetString("BuildNumber")}, {os.GetString("OSArchitecture")})",
            Cpu = ReadCpu(),
            Gpus = ReadGpus(),
            Memory = ReadMemory(),
        };
    }

    private static CpuInfo? ReadCpu()
    {
        var rows = WmiQuery.Run(WmiQuery.Cimv2, "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
        if (rows.Count == 0 || rows[0].GetString("Name") is not { } name)
            return null;
        return new CpuInfo(
            name,
            Sum(rows, "NumberOfCores"),
            Sum(rows, "NumberOfLogicalProcessors"),
            (int?)rows[0].GetLong("MaxClockSpeed") is > 0 and var mhz ? mhz : null);
    }

    private static List<GpuInfo> ReadGpus()
    {
        var rows = WmiQuery.Run(WmiQuery.Cimv2, "SELECT Name, DriverVersion, PNPDeviceID FROM Win32_VideoController");
        var gpus = new List<GpuInfo>();
        foreach (var row in rows)
        {
            // Los adaptadores virtuales (escritorio remoto, pantallas virtuales) no son GPUs reales.
            var pnp = row.GetString("PNPDeviceID") ?? "";
            if (pnp.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase) || pnp.StartsWith(@"SWD\", StringComparison.OrdinalIgnoreCase))
                continue;
            if (row.GetString("Name") is { } name)
                gpus.Add(new GpuInfo(name, row.GetString("DriverVersion")));
        }
        return gpus;
    }

    private static MemoryInfo? ReadMemory()
    {
        var rows = WmiQuery.Run(WmiQuery.Cimv2, "SELECT Capacity, ConfiguredClockSpeed, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
        var total = rows.Sum(r => r.GetLong("Capacity") ?? 0);
        if (total <= 0)
            return null;
        var speed = rows.Select(r => r.GetLong("ConfiguredClockSpeed") is > 0 and var s ? s : r.GetLong("Speed"))
            .Where(s => s is > 0 and < 20_000).Max();
        var type = rows.Select(r => MemoryType(r.GetLong("SMBIOSMemoryType"))).FirstOrDefault(t => t is not null);
        return new MemoryInfo(total, rows.Count, (int?)speed, type);
    }

    // Códigos SMBIOS de tipo de memoria (especificación DMTF SMBIOS, tabla 17).
    private static string? MemoryType(long? code) => code switch
    {
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => null,
    };

    private static int? Sum(IReadOnlyList<WmiRow> rows, string property)
    {
        var values = rows.Select(r => r.GetLong(property)).Where(v => v is > 0).ToList();
        return values.Count == 0 ? null : (int)values.Sum()!;
    }

    private static string? Clean(string? value) =>
        value is null || Placeholders.Any(p => value.Equals(p, StringComparison.OrdinalIgnoreCase)) ? null : value;
}
