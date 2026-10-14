using BatteryTest.Core.Conditions;
using BatteryTest.Core.Models;
using BatteryTest.Core.Testing;

namespace BatteryTest.Agent;

public sealed record InfoRow(string Label, string Value);

/// <summary>Convierte las lecturas en filas "etiqueta: valor" para mostrar en pantalla.</summary>
public static class InfoRows
{
    private const string NotAvailable = "No disponible";

    public static List<InfoRow> Batteries(IReadOnlyList<BatteryReading> batteries)
    {
        if (batteries.Count == 0)
            return [new InfoRow("Batería", "No se encontró ninguna batería (¿es un equipo de escritorio?)")];

        var rows = new List<InfoRow>();
        for (var i = 0; i < batteries.Count; i++)
        {
            var b = batteries[i];
            var prefix = batteries.Count > 1 ? $"Batería {i + 1} · " : "";
            rows.Add(new($"{prefix}Fabricante", Text(b.Manufacturer)));
            rows.Add(new($"{prefix}Modelo", Text(b.DeviceName)));
            rows.Add(new($"{prefix}Química", Text(b.Chemistry)));
            rows.Add(new($"{prefix}Capacidad de diseño", Energy(b.DesignedCapacityMWh)));
            rows.Add(new($"{prefix}Capacidad actual", Energy(b.FullChargedCapacityMWh)));
            rows.Add(new($"{prefix}Salud", b.HealthPercent is not { } h ? NotAvailable
                : b.WearPossiblyUnreported ? $"{h:0.0} % (sin desgaste: batería nueva, o el firmware no reporta el desgaste)"
                : $"{h:0.0} %"));
            rows.Add(new($"{prefix}Carga restante", b.RemainingCapacityMWh is { } r
                ? $"{Energy(r)}" + (b.ChargePercent is { } p ? $" ({p:0} %)" : "")
                : NotAvailable));
            rows.Add(new($"{prefix}Ciclos", b.CycleCount?.ToString() ?? NotAvailable));
            rows.Add(new($"{prefix}Voltaje", b.VoltageMV is { } v ? $"{v / 1000.0:0.00} V" : NotAvailable));
            rows.Add(new($"{prefix}Descarga", Watts(b.DischargeRateMW)));
            rows.Add(new($"{prefix}Carga", Watts(b.ChargeRateMW)));
            rows.Add(new($"{prefix}Estado", State(b)));
        }
        return rows;
    }

    public static List<InfoRow> Power(PowerStatus s, PowerPlan? plan) =>
    [
        new("Cargador", s.AcOnline switch { true => "Conectado", false => "Desconectado", null => "Desconocido" }),
        new("Batería (Windows)", s.BatteryPercent is { } p ? $"{p} %" : NotAvailable),
        new("Tiempo estimado (Windows)", s.EstimatedRemaining is { } t ? Span(t) : NotAvailable),
        new("Ahorro de batería", s.BatterySaverOn ? "Activado ⚠ (afecta el resultado del test)" : "Desactivado"),
        new("Plan de energía", plan is null ? NotAvailable : Text(plan.Name) + (plan.IsPowerSaver ? " ⚠ (afecta el resultado del test)" : "")),
    ];

    public static List<InfoRow> TestStatus(TestStatus s, string profileName) =>
    [
        new("Perfil", profileName),
        new("Tiempo transcurrido", Span(s.Elapsed)),
        new("Fase actual", $"{s.Phase} ({s.TargetCpuPercent:0} % de CPU)"),
        new("Batería", s.BatteryPercent is { } p ? $"{p} %" : NotAvailable),
        new("Consumo promedio", s.AveragePowerMW is { } w ? $"{w / 1000:0.0} W" : "Calculando… (necesita al menos dos lecturas)"),
        new("Tiempo restante estimado", s.EstimatedRemaining is { } r ? Span(r) : "Calculando…"),
        new("Lecturas guardadas", s.SampleCount.ToString()),
    ];

    public static List<InfoRow> Result(TestFile? file, TestSummary? summary, TestEndReason reason, string? detail)
    {
        var rows = new List<InfoRow> { new("Cómo terminó", EndReason(reason)) };
        if (detail is not null && reason == TestEndReason.Error)
            rows.Add(new("Detalle del error", detail));
        if (file?.Header is { } header)
            rows.Add(new("Perfil", header.Profile.Name));
        if (summary is not null)
        {
            rows.Add(new("Duración del test", Span(summary.Duration)));
            rows.Add(new("Energía consumida", summary.EnergyUsedMWh is { } used
                ? Energy(used) + (summary.PercentUsed is { } pct ? $" · {pct:0.0} % de la batería" : "")
                : NotAvailable));
            rows.Add(new("Consumo promedio", summary.AveragePowerMW is { } w ? $"{w / 1000:0.0} W" : NotAvailable));
            rows.Add(new("Duración estimada con batería completa", summary.EstimatedFullRuntime is { } full
                ? Span(full) + (summary.IsShort ? " (test corto: estimación poco precisa)" : "")
                : NotAvailable));
            rows.Add(new("Lecturas guardadas", summary.SampleCount.ToString()));
        }
        if (file is not null)
            rows.Add(new("Archivo", file.Path));
        return rows;
    }

    private static string EndReason(TestEndReason reason) => reason switch
    {
        TestEndReason.UserStopped => "Lo detuviste tú",
        TestEndReason.ChargerConnected => "Se conectó el cargador",
        TestEndReason.MinBatteryReached => "La batería llegó al mínimo configurado",
        TestEndReason.Shutdown => "El equipo se apagó durante el test (batería agotada)",
        TestEndReason.Error => "Error",
        _ => reason.ToString(),
    };

    private static string Span(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : $"{t.Minutes} min {t.Seconds:00} s";

    public static List<InfoRow> Cpu(CpuSample sample) =>
    [
        new("Uso de CPU", sample.UsagePercent is { } u ? $"{u:0} %" : NotAvailable),
        new("Frecuencia actual", sample.FrequencyMHz is { } f ? $"{f / 1000.0:0.00} GHz" : NotAvailable),
    ];

    public static List<InfoRow> Hardware(HardwareInfo hw)
    {
        var rows = new List<InfoRow>
        {
            new("Fabricante", Text(hw.Manufacturer)),
            new("Modelo", Text(hw.Model)),
            new("Familia", Text(hw.SystemFamily)),
            new("Sistema operativo", Text(hw.OperatingSystem)),
            new("CPU", Text(hw.Cpu?.Name)),
            new("Núcleos / hilos", hw.Cpu is { } c ? $"{Text(c.Cores?.ToString())} / {Text(c.LogicalProcessors?.ToString())}" : NotAvailable),
            new("Frecuencia nominal", hw.Cpu?.BaseClockMHz is { } mhz ? $"{mhz / 1000.0:0.00} GHz" : NotAvailable),
        };

        if (hw.Gpus.Count == 0)
            rows.Add(new("GPU", NotAvailable));
        for (var i = 0; i < hw.Gpus.Count; i++)
        {
            var g = hw.Gpus[i];
            rows.Add(new(hw.Gpus.Count > 1 ? $"GPU {i + 1}" : "GPU",
                g.DriverVersion is null ? g.Name : $"{g.Name} (driver {g.DriverVersion})"));
        }

        rows.Add(new("RAM", hw.Memory is { } m
            ? $"{m.TotalBytes / (1024.0 * 1024 * 1024):0.#} GB" +
              $" · {m.Modules} módulo(s)" +
              (m.Type is null ? "" : $" · {m.Type}") +
              (m.SpeedMTs is { } s ? $" · {s} MT/s" : "")
            : NotAvailable));
        return rows;
    }

    private static string Text(string? value) => value ?? NotAvailable;

    private static string Energy(int? mWh) => mWh is { } v ? $"{v:N0} mWh ({v / 1000.0:0.0} Wh)" : NotAvailable;

    private static string Watts(int? mW) => mW is { } v ? $"{v / 1000.0:0.0} W" : NotAvailable;

    private static string State(BatteryReading b) => (b.Charging, b.Discharging, b.PowerOnline) switch
    {
        (true, _, _) => "Cargando",
        (_, true, _) => "Descargando",
        (_, _, true) => "Conectada, sin cargar",
        _ => "Desconocido",
    };
}
