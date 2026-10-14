using BatteryTest.Core.Load;
using BatteryTest.Core.Testing;

namespace BatteryTest.Core.Tests;

public sealed class TestSummaryTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 13, 20, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void Uses_drop_in_remaining_capacity_over_time()
    {
        // 5.000 mWh en 30 min → 10 W de media; 56.000 mWh a 10 W → 5,6 h.
        var file = File(
            TestLogTests.Sample(Start, 50_000, full: 56_000),
            TestLogTests.Sample(Start.AddMinutes(15), 47_500, full: 56_000),
            TestLogTests.Sample(Start.AddMinutes(30), 45_000, full: 56_000));

        var summary = TestSummary.From(file);

        Assert.Equal(TimeSpan.FromMinutes(30), summary.Duration);
        Assert.Equal(5_000, summary.EnergyUsedMWh);
        Assert.Equal(10_000, summary.AveragePowerMW!.Value, precision: 6);
        Assert.Equal(5.6, summary.EstimatedFullRuntime!.Value.TotalHours, precision: 6);
        Assert.Equal(100.0 * 5_000 / 56_000, summary.PercentUsed!.Value, precision: 6);
        Assert.False(summary.IsShort);
    }

    [Fact]
    public void Missing_capacity_gives_no_estimate()
    {
        var file = File(TestLogTests.Sample(Start, null), TestLogTests.Sample(Start.AddMinutes(5), null));

        var summary = TestSummary.From(file);

        Assert.Null(summary.EnergyUsedMWh);
        Assert.Null(summary.AveragePowerMW);
        Assert.Null(summary.EstimatedFullRuntime);
        Assert.True(summary.IsShort);
    }

    [Fact]
    public void Profile_average_is_weighted_by_duration()
    {
        // Ligero: 2 s al 35 % y 8 s al 3 % → (70 + 24) / 10.
        Assert.Equal(9.4, LoadProfile.Light.AverageCpuPercent, precision: 6);
    }

    private static TestFile File(params TestSample[] samples) =>
        new("memoria", TestLogTests.Header(Start), samples, [], new TestEnd(samples[^1].Time, TestEndReason.UserStopped, null));
}
