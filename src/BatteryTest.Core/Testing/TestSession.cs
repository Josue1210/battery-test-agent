using BatteryTest.Core.Conditions;
using BatteryTest.Core.Load;
using BatteryTest.Core.Models;

namespace BatteryTest.Core.Testing;

public sealed record TestOptions(
    LoadProfile Profile,
    int MinBatteryPercent,
    TimeSpan SampleInterval,
    int? BrightnessPercent,
    IReadOnlyList<string> Checklist);

public sealed record TestStatus(
    TimeSpan Elapsed,
    string Phase,
    double TargetCpuPercent,
    int? BatteryPercent,
    double? AveragePowerMW,
    int SampleCount,
    TimeSpan? EstimatedRemaining);

public sealed record TestResult(TestFile? File, TestSummary? Summary, TestEndReason Reason, string? Detail);

/// <summary>
/// Un test en curso. Corre en un hilo propio: aplica las condiciones (brillo, sin suspensión),
/// genera la carga, guarda una lectura cada <see cref="TestOptions.SampleInterval"/> y se detiene
/// si el usuario lo pide, si se conecta el cargador o si la batería llega al mínimo.
/// Al terminar siempre restaura el brillo y deja que Windows vuelva a suspender.
/// </summary>
public sealed class TestSession(TestOptions options, HardwareInfo hardware, TestStore store)
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    private readonly ManualResetEventSlim _stopRequested = new();
    private Thread? _thread;

    /// <summary>Se dispara cada segundo desde el hilo del test.</summary>
    public event Action<TestStatus>? StatusChanged;

    /// <summary>Se dispara una vez, desde el hilo del test, cuando todo quedó restaurado.</summary>
    public event Action<TestResult>? Finished;

    public static string AgentVersion { get; } =
        typeof(TestSession).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public void Start()
    {
        if (_thread is not null)
            throw new InvalidOperationException("El test ya empezó.");
        _thread = new Thread(Run) { IsBackground = true, Name = "Test de batería" };
        _thread.Start();
    }

    public void Stop() => _stopRequested.Set();

    /// <summary>Espera a que el test termine de cerrarse (para cerrar la aplicación de forma ordenada).</summary>
    public bool WaitForExit(TimeSpan timeout) => _thread?.Join(timeout) ?? true;

    private void Run()
    {
        var start = DateTimeOffset.Now;
        var reason = TestEndReason.UserStopped;
        string? detail = null;
        string? logPath = null;
        TestLogWriter? log = null;
        LoadGenerator? load = null;
        CpuMonitor? cpu = null;
        int? originalBrightness = null;
        int? appliedBrightness = null;
        var samples = new List<TestSample>();

        try
        {
            var batteries = BatteryReader.Read();
            if (batteries.Count == 0)
                throw new InvalidOperationException("No se encontró ninguna batería.");
            var power = PowerStatusReader.Read();
            var plan = PowerPlanReader.Read();

            SleepBlocker.Block();
            originalBrightness = BrightnessController.Get();
            if (options.BrightnessPercent is { } target && originalBrightness is not null && BrightnessController.Set(target))
                appliedBrightness = BrightnessController.Get() ?? target;

            var id = Guid.NewGuid();
            logPath = store.NewTestPath(start, id);
            log = new TestLogWriter(logPath);
            log.Append(new TestHeader(
                start, id, AgentVersion, options.Profile, options.MinBatteryPercent,
                (int)options.SampleInterval.TotalSeconds, appliedBrightness, originalBrightness,
                plan?.Name, plan?.IsPowerSaver ?? false, power.BatterySaverOn,
                hardware, batteries, options.Checklist));
            if (options.BrightnessPercent is not null && appliedBrightness is null)
                log.Append(new TestEvent(DateTimeOffset.Now, "No se pudo fijar el brillo: el test sigue con el brillo actual."));

            cpu = new CpuMonitor(hardware.Cpu?.BaseClockMHz);
            load = new LoadGenerator(options.Profile);
            load.Start();

            var nextSample = DateTimeOffset.Now;
            while (!_stopRequested.IsSet)
            {
                power = PowerStatusReader.Read();
                if (power.AcOnline == true)
                {
                    reason = TestEndReason.ChargerConnected;
                    break;
                }
                if (power.BatteryPercent is { } percent && percent <= options.MinBatteryPercent)
                {
                    reason = TestEndReason.MinBatteryReached;
                    break;
                }

                if (DateTimeOffset.Now >= nextSample)
                {
                    var sample = TakeSample(start, cpu, load, power);
                    log.Append(sample);
                    samples.Add(sample);
                    nextSample += options.SampleInterval;

                    // Si Windows o el usuario cambian el brillo, se vuelve al valor del test.
                    if (appliedBrightness is { } applied && sample.BrightnessPercent is { } current && current != applied
                        && BrightnessController.Set(options.BrightnessPercent!.Value))
                        log.Append(new TestEvent(DateTimeOffset.Now, $"El brillo cambió a {current} %; se volvió a fijar."));
                }

                StatusChanged?.Invoke(Status(start, load, power, samples));
                _stopRequested.Wait(Tick);
            }
        }
        catch (Exception e)
        {
            // Cualquier fallo termina el test, pero el bloque finally siempre restaura el sistema.
            reason = TestEndReason.Error;
            detail = e.Message;
        }
        finally
        {
            load?.Dispose();
            if (log is not null)
            {
                try
                {
                    if (cpu is not null && load is not null)
                        log.Append(TakeSample(start, cpu, load, PowerStatusReader.Read()));
                    log.Append(new TestEnd(DateTimeOffset.Now, reason, detail));
                }
                catch (IOException e)
                {
                    detail ??= e.Message;
                }
                log.Dispose();
            }
            if (appliedBrightness is not null && originalBrightness is { } original)
                BrightnessController.Set(original);
            SleepBlocker.Release();
            cpu?.Dispose();

            var file = logPath is null ? null : TestFile.Read(logPath);
            Finished?.Invoke(new TestResult(file, file is null ? null : TestSummary.From(file), reason, detail));
        }
    }

    private static TestSample TakeSample(DateTimeOffset start, CpuMonitor cpu, LoadGenerator load, PowerStatus power)
    {
        var batteries = BatteryReader.Read();
        var remaining = SumOrNull(batteries.Select(b => b.RemainingCapacityMWh));
        var full = SumOrNull(batteries.Select(b => b.FullChargedCapacityMWh));
        var cpuSample = cpu.Sample();
        var now = DateTimeOffset.Now;
        return new TestSample(
            now,
            (now - start).TotalSeconds,
            remaining,
            full,
            remaining is { } r && full is > 0 ? Math.Clamp(100.0 * r / full.Value, 0, 100) : null,
            power.BatteryPercent,
            SumOrNull(batteries.Select(b => b.DischargeRateMW)),
            batteries.FirstOrDefault()?.VoltageMV,
            power.AcOnline,
            cpuSample.UsagePercent,
            cpuSample.FrequencyMHz,
            BrightnessController.Get(),
            load.CurrentPhase.Name,
            load.CurrentPhase.CpuPercent);
    }

    private static TestStatus Status(DateTimeOffset start, LoadGenerator load, PowerStatus power, List<TestSample> samples)
    {
        var withCapacity = samples.Where(s => s.RemainingMWh is not null).ToList();
        double? averagePower = null;
        TimeSpan? remaining = null;
        if (withCapacity.Count >= 2)
        {
            var (first, last) = (withCapacity[0], withCapacity[^1]);
            var hours = (last.Time - first.Time).TotalHours;
            var used = first.RemainingMWh!.Value - last.RemainingMWh!.Value;
            if (hours > 0 && used > 0)
            {
                averagePower = used / hours;
                remaining = TimeSpan.FromHours(last.RemainingMWh.Value / averagePower.Value);
            }
        }
        return new TestStatus(DateTimeOffset.Now - start, load.CurrentPhase.Name, load.CurrentPhase.CpuPercent,
            power.BatteryPercent, averagePower, samples.Count, remaining);
    }

    /// <summary>Suma de todas las baterías; null si no hay baterías o si alguna no reporta el valor.</summary>
    private static int? SumOrNull(IEnumerable<int?> values)
    {
        int? sum = null;
        foreach (var value in values)
        {
            if (value is not { } v)
                return null;
            sum = (sum ?? 0) + v;
        }
        return sum;
    }
}
