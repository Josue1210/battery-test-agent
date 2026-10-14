using System.Text.Json.Serialization;
using BatteryTest.Core.Load;
using BatteryTest.Core.Models;

namespace BatteryTest.Core.Testing;

/// <summary>Cada línea del archivo de registro es uno de estos registros, con su tipo en "type".</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TestHeader), "header")]
[JsonDerivedType(typeof(TestSample), "sample")]
[JsonDerivedType(typeof(TestEvent), "event")]
[JsonDerivedType(typeof(TestEnd), "end")]
public abstract record TestRecord(DateTimeOffset Time);

/// <summary>Primera línea: condiciones con las que empezó el test.</summary>
public sealed record TestHeader(
    DateTimeOffset Time,
    Guid Id,
    string AgentVersion,
    LoadProfile Profile,
    int MinBatteryPercent,
    int SampleIntervalSeconds,
    int? BrightnessTarget,
    int? BrightnessOriginal,
    string? PowerPlan,
    bool PowerSaverPlan,
    bool BatterySaverOn,
    HardwareInfo Hardware,
    IReadOnlyList<BatteryReading> Batteries,
    IReadOnlyList<string> Checklist) : TestRecord(Time);

/// <summary>Una lectura periódica. Las capacidades son la suma de todas las baterías.</summary>
public sealed record TestSample(
    DateTimeOffset Time,
    double ElapsedSeconds,
    int? RemainingMWh,
    int? FullChargedMWh,
    double? ChargePercent,
    int? WindowsPercent,
    int? DischargeRateMW,
    int? VoltageMV,
    bool? AcOnline,
    double? CpuUsagePercent,
    int? CpuFrequencyMHz,
    int? BrightnessPercent,
    string LoadPhase,
    double LoadTargetCpuPercent) : TestRecord(Time);

/// <summary>Algo que pasó durante el test y conviene dejar registrado.</summary>
public sealed record TestEvent(DateTimeOffset Time, string Message) : TestRecord(Time);

public enum TestEndReason
{
    UserStopped,
    ChargerConnected,
    MinBatteryReached,
    Error,
    /// <summary>El agente no llegó a cerrar el test: casi siempre, la laptop se apagó sin batería.</summary>
    Shutdown,
}

/// <summary>Última línea: por qué terminó el test.</summary>
public sealed record TestEnd(DateTimeOffset Time, TestEndReason Reason, string? Detail) : TestRecord(Time);
