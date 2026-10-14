namespace BatteryTest.Core.Testing;

/// <summary>
/// Resultado de un test, calculado con la caída de la carga restante (mWh) en el tiempo.
/// No usa la tasa de descarga instantánea, que solo captura un momento y algunos firmwares reportan mal.
/// </summary>
public sealed record TestSummary(
    TimeSpan Duration,
    int? StartRemainingMWh,
    int? EndRemainingMWh,
    int? FullChargedMWh,
    int? EnergyUsedMWh,
    double? AveragePowerMW,
    double? PercentUsed,
    TimeSpan? EstimatedFullRuntime,
    int SampleCount,
    TestEndReason? EndReason)
{
    /// <summary>Por debajo de esto la estimación de duración total es poco precisa.</summary>
    public static readonly TimeSpan MinReliableDuration = TimeSpan.FromMinutes(10);

    public bool IsShort => Duration < MinReliableDuration;

    public static TestSummary From(TestFile file)
    {
        var withCapacity = file.Samples.Where(s => s.RemainingMWh is not null).ToList();
        var first = withCapacity.FirstOrDefault();
        var last = withCapacity.LastOrDefault();
        var start = file.Header?.Time ?? first?.Time ?? DateTimeOffset.MinValue;
        var endTime = file.Samples.Count > 0 ? file.Samples[^1].Time : start;

        int? energy = first is not null && last is not null ? first.RemainingMWh - last.RemainingMWh : null;
        var hours = first is not null && last is not null ? (last.Time - first.Time).TotalHours : 0;
        double? averagePower = energy is > 0 && hours > 0 ? energy.Value / hours : null;
        var full = last?.FullChargedMWh ?? first?.FullChargedMWh;

        return new TestSummary(
            Duration: endTime - start,
            StartRemainingMWh: first?.RemainingMWh,
            EndRemainingMWh: last?.RemainingMWh,
            FullChargedMWh: full,
            EnergyUsedMWh: energy,
            AveragePowerMW: averagePower,
            PercentUsed: energy is not null && full is > 0 ? 100.0 * energy.Value / full.Value : null,
            EstimatedFullRuntime: averagePower is > 0 && full is > 0 ? TimeSpan.FromHours(full.Value / averagePower.Value) : null,
            SampleCount: file.Samples.Count,
            EndReason: file.End?.Reason);
    }
}
