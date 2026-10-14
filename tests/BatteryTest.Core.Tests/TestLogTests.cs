using System.Text;
using BatteryTest.Core.Load;
using BatteryTest.Core.Models;
using BatteryTest.Core.Testing;

namespace BatteryTest.Core.Tests;

public sealed class TestLogTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 13, 20, 0, 0, TimeSpan.FromHours(-4));

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "BatteryTest.Tests", Guid.NewGuid().ToString("N"));
    private readonly TestStore _store;

    public TestLogTests() => _store = new TestStore(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Roundtrip_keeps_header_samples_and_end()
    {
        var path = WriteTest(samples: 3, end: new TestEnd(Start.AddMinutes(2), TestEndReason.UserStopped, null));

        var file = TestFile.Read(path);

        Assert.NotNull(file.Header);
        Assert.Equal("medio", file.Header.Profile.Id);
        Assert.Equal(2, file.Header.Profile.Pattern.Count);
        Assert.Equal(55997, file.Header.Batteries[0].DesignedCapacityMWh);
        Assert.Equal("Latitude 3380", file.Header.Hardware.Model);
        Assert.Equal(3, file.Samples.Count);
        Assert.Equal(TestEndReason.UserStopped, file.End?.Reason);
        Assert.False(file.IsIncomplete);
    }

    [Fact]
    public void Cut_last_line_is_ignored_and_test_can_be_closed()
    {
        var path = WriteTest(samples: 4, end: null);
        // Simula un apagado a mitad de escritura: queda una línea incompleta y sin salto final.
        File.AppendAllText(path, "{\"type\":\"sample\",\"time\":\"2026-10-13T2", Encoding.UTF8);

        var open = Assert.Single(_store.FindIncomplete());
        Assert.Equal(4, open.Samples.Count);

        var closed = TestStore.CloseIncomplete(open);
        var reread = TestFile.Read(path);

        Assert.Equal(TestEndReason.Shutdown, reread.End?.Reason);
        Assert.Equal(open.Samples[^1].Time, reread.End?.Time);
        Assert.Equal(4, reread.Samples.Count);
        Assert.False(closed.IsIncomplete);
        Assert.Empty(_store.FindIncomplete());
    }

    [Fact]
    public void FindIncomplete_ignores_finished_tests()
    {
        WriteTest(samples: 2, end: new TestEnd(Start.AddMinutes(1), TestEndReason.ChargerConnected, null));
        var open = WriteTest(samples: 2, end: null, id: Guid.NewGuid(), start: Start.AddHours(1));

        var incomplete = Assert.Single(_store.FindIncomplete());
        Assert.Equal(open, incomplete.Path);
    }

    private string WriteTest(int samples, TestEnd? end, Guid? id = null, DateTimeOffset? start = null)
    {
        var begin = start ?? Start;
        var path = _store.NewTestPath(begin, id ?? Guid.NewGuid());
        using var writer = new TestLogWriter(path);
        writer.Append(Header(begin));
        for (var i = 0; i < samples; i++)
            writer.Append(Sample(begin.AddSeconds(30 * i), 52_000 - 100 * i));
        if (end is not null)
            writer.Append(end);
        return path;
    }

    internal static TestHeader Header(DateTimeOffset time) => new(
        time, Guid.NewGuid(), "0.1.0", LoadProfile.Medium, 5, 30, 50, 80, "Equilibrado", false, false,
        new HardwareInfo { Manufacturer = "Dell Inc.", Model = "Latitude 3380" },
        [new BatteryReading { InstanceName = @"ACPI\PNP0C0A\1_0", DesignedCapacityMWh = 55997, FullChargedCapacityMWh = 55997 }],
        ["Cerré los programas que no necesito"]);

    internal static TestSample Sample(DateTimeOffset time, int? remaining, int? full = 55997) => new(
        time, 0, remaining, full, null, null, null, null, false, null, null, 50, "Ráfaga", 55);
}
