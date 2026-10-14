using System.Diagnostics;

namespace BatteryTest.Core.Load;

/// <summary>
/// Genera la carga de un perfil:
/// - CPU: un hilo por núcleo lógico, cada uno trabaja una fracción de cada ciclo de 100 ms y descansa el resto.
///   Así el % de uso es el mismo en cualquier cantidad de núcleos.
/// - Control: mide el % de CPU ocupado real y corrige el ciclo de trabajo de cada tramo para acercarse al objetivo
///   (si hay actividad de fondo, el agente trabaja menos).
/// - Memoria: reserva un bloque y lo recorre durante los tramos activos.
/// - Disco: escribe un poco cada 10 s en un archivo temporal que se borra al terminar.
/// Las duraciones de los tramos varían ±20 % con una semilla fija: parecido al uso real, pero igual en cada test.
/// </summary>
public sealed class LoadGenerator : IDisposable
{
    private const int PeriodMs = 100;
    private const double Gain = 0.4;
    private const double MaxCorrection = 0.35;
    private const double JitterFraction = 0.2;
    private const int Seed = 20261013;
    private const int MemoryBlockBytes = 64 * 1024 * 1024;
    private const double MaxMemoryFraction = 0.25;
    private const long MaxDiskFileBytes = 64L * 1024 * 1024;
    private static readonly TimeSpan ControlInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DiskInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MemoryInterval = TimeSpan.FromMilliseconds(200);

    public static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "BatteryTest");

    private static double s_sink;

    private readonly LoadProfile _profile;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Thread> _threads = [];
    private readonly double[] _correction;
    private readonly Random _random = new(Seed);
    private int _dutyPermille;
    private volatile bool _memoryActive;
    private volatile LoadPhase _phase;
    private List<byte[]> _memory = [];
    private string? _diskFile;
    private long _diskOffset;

    public LoadGenerator(LoadProfile profile)
    {
        _profile = profile;
        _correction = new double[profile.Pattern.Count];
        _phase = profile.Pattern[0];
    }

    public LoadPhase CurrentPhase => _phase;

    public int WorkerCount { get; } = Environment.ProcessorCount;

    public void Start()
    {
        if (_threads.Count > 0)
            throw new InvalidOperationException("La carga ya está en marcha.");
        for (var i = 0; i < WorkerCount; i++)
            StartThread(CpuWorker, $"Carga CPU {i + 1}", ThreadPriority.BelowNormal);
        StartThread(Control, "Control de carga", ThreadPriority.Normal);
        StartThread(MemoryAndDisk, "Carga de memoria y disco", ThreadPriority.BelowNormal);
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var thread in _threads)
            thread.Join(TimeSpan.FromSeconds(3));
        _memory = [];
        if (_diskFile is not null)
            TryDelete(_diskFile);
        _cts.Dispose();
    }

    /// <summary>Borra archivos temporales de disco que quedaron de un test que no terminó bien.</summary>
    public static void CleanupLeftovers()
    {
        if (!Directory.Exists(TempDirectory))
            return;
        foreach (var file in Directory.EnumerateFiles(TempDirectory, "carga-*.tmp"))
            TryDelete(file);
    }

    private void StartThread(Action body, string name, ThreadPriority priority)
    {
        var thread = new Thread(() => body()) { IsBackground = true, Name = name, Priority = priority };
        _threads.Add(thread);
        thread.Start();
    }

    private void CpuWorker()
    {
        var token = _cts.Token;
        var periodTicks = Stopwatch.Frequency * PeriodMs / 1000;
        double sink = 0;
        while (!token.IsCancellationRequested)
        {
            var start = Stopwatch.GetTimestamp();
            var duty = Volatile.Read(ref _dutyPermille) / 1000.0;
            sink += Spin(start + (long)(periodTicks * duty));
            var restMs = PeriodMs - (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (restMs > 0)
                token.WaitHandle.WaitOne(restMs);
        }
        Volatile.Write(ref s_sink, sink); // evita que el compilador elimine el trabajo
    }

    /// <summary>Cálculo entero y de coma flotante sobre registros, hasta el instante indicado.</summary>
    private static double Spin(long untilTimestamp)
    {
        var x = 1.0001;
        var h = 0x9E3779B97F4A7C15UL;
        while (Stopwatch.GetTimestamp() < untilTimestamp)
        {
            for (var i = 0; i < 2000; i++)
            {
                x = x * 1.0000001 + 0.0000001;
                h ^= h << 13;
                h ^= h >> 7;
                h ^= h << 17;
            }
            if (x > 1e6)
                x = 1.0001;
        }
        return x + h;
    }

    private void Control()
    {
        var token = _cts.Token;
        using var cpu = new CpuMonitor(null);
        var index = 0;
        var now = DateTime.UtcNow;
        var phaseStarted = now;
        var phaseEnds = now + Jittered(_profile.Pattern[0].Duration);
        var lastControl = now;
        ApplyPhase(index);

        while (!token.WaitHandle.WaitOne(100))
        {
            now = DateTime.UtcNow;
            if (now - lastControl >= ControlInterval)
            {
                // Solo se corrige si todo el intervalo medido cayó dentro del tramo actual.
                if (cpu.Sample().BusyPercent is { } measured && phaseStarted <= lastControl)
                {
                    var error = (_profile.Pattern[index].CpuPercent - measured) / 100;
                    _correction[index] = Math.Clamp(_correction[index] + Gain * error, -MaxCorrection, MaxCorrection);
                    ApplyPhase(index);
                }
                lastControl = now;
            }
            if (now >= phaseEnds)
            {
                index = (index + 1) % _profile.Pattern.Count;
                phaseStarted = now;
                phaseEnds = now + Jittered(_profile.Pattern[index].Duration);
                ApplyPhase(index);
            }
        }
    }

    private void ApplyPhase(int index)
    {
        var phase = _profile.Pattern[index];
        _phase = phase;
        _memoryActive = phase.MemoryActive;
        var duty = Math.Clamp(phase.CpuPercent / 100 + _correction[index], 0, 1);
        Volatile.Write(ref _dutyPermille, (int)Math.Round(duty * 1000));
    }

    private TimeSpan Jittered(TimeSpan duration) =>
        TimeSpan.FromSeconds(duration.TotalSeconds * (1 + JitterFraction * (2 * _random.NextDouble() - 1)));

    private void MemoryAndDisk()
    {
        var token = _cts.Token;
        _memory = AllocateMemory();
        var totalBytes = _memory.Sum(b => (long)b.Length);
        var bytesPerTick = totalBytes / 20; // una pasada completa cada ~4 s
        var block = 0;
        var position = 0;
        var nextDisk = DateTime.UtcNow + DiskInterval;

        while (!token.WaitHandle.WaitOne(MemoryInterval))
        {
            if (_memoryActive && _memory.Count > 0)
            {
                // Copia la primera mitad de cada bloque sobre la segunda, por tandas.
                for (var budget = bytesPerTick; budget > 0;)
                {
                    var buffer = _memory[block];
                    var half = buffer.Length / 2;
                    var length = (int)Math.Min(budget, half - position);
                    Buffer.BlockCopy(buffer, position, buffer, half + position, length);
                    budget -= length;
                    position += length;
                    if (position >= half)
                    {
                        position = 0;
                        block = (block + 1) % _memory.Count;
                    }
                }
            }
            if (DateTime.UtcNow >= nextDisk)
            {
                WriteDisk();
                nextDisk += DiskInterval;
            }
        }
    }

    private List<byte[]> AllocateMemory()
    {
        var limit = (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes * MaxMemoryFraction);
        var target = Math.Min(_profile.MemoryMB * 1024L * 1024, limit);
        var blocks = new List<byte[]>();
        for (long allocated = 0; allocated < target; allocated += MemoryBlockBytes)
        {
            var block = new byte[(int)Math.Min(MemoryBlockBytes, target - allocated)];
            for (var i = 0; i < block.Length; i += 4096)
                block[i] = 1; // fuerza a Windows a asignar las páginas de verdad
            blocks.Add(block);
        }
        return blocks;
    }

    private void WriteDisk()
    {
        var bytes = _profile.DiskMBPerMinute * 1024L * 1024 * (long)DiskInterval.TotalSeconds / 60;
        if (bytes <= 0)
            return;
        try
        {
            Directory.CreateDirectory(TempDirectory);
            _diskFile ??= Path.Combine(TempDirectory, $"carga-{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(_diskFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough);
            if (_diskOffset + bytes > MaxDiskFileBytes)
                _diskOffset = 0;
            stream.Position = _diskOffset;
            var chunk = new byte[1024 * 1024];
            Random.Shared.NextBytes(chunk); // _random es solo del hilo de control
            for (var written = 0L; written < bytes; written += chunk.Length)
                stream.Write(chunk, 0, (int)Math.Min(chunk.Length, bytes - written));
            _diskOffset += bytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // El disco es una parte menor de la carga: si falla, el test sigue.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
