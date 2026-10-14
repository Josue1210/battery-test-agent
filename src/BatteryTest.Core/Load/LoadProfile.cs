namespace BatteryTest.Core.Load;

/// <summary>Un tramo del patrón de carga: cuánto dura y qué uso total de CPU busca.</summary>
/// <param name="CpuPercent">Uso de CPU objetivo, en % del total de núcleos lógicos.</param>
/// <param name="MemoryActive">Si durante el tramo se recorre la memoria reservada.</param>
public sealed record LoadPhase(string Name, TimeSpan Duration, double CpuPercent, bool MemoryActive);

/// <summary>
/// Perfil de carga: un patrón de ráfagas y reposos que se repite durante todo el test,
/// más la memoria reservada y la escritura a disco por minuto.
/// </summary>
public sealed record LoadProfile(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<LoadPhase> Pattern,
    int MemoryMB,
    int DiskMBPerMinute)
{
    /// <summary>Uso medio de CPU del patrón, ponderado por la duración de cada tramo.</summary>
    public double AverageCpuPercent =>
        Pattern.Sum(p => p.CpuPercent * p.Duration.TotalSeconds) / Pattern.Sum(p => p.Duration.TotalSeconds);

    public static readonly LoadProfile Light = new(
        "ligero", "Ligero", "Navegación y ofimática: ráfagas cortas y mucho reposo.",
        [Burst(2, 35), Rest(8, 3)], MemoryMB: 256, DiskMBPerMinute: 1);

    public static readonly LoadProfile Medium = new(
        "medio", "Medio", "Multitarea: varias apps abiertas, ráfagas frecuentes.",
        [Burst(5, 55), Rest(10, 12)], MemoryMB: 512, DiskMBPerMinute: 5);

    public static readonly LoadProfile High = new(
        "alto", "Alto", "Edición de fotos o compilación: ráfagas largas e intensas.",
        [Burst(20, 85), Rest(10, 30, memoryActive: true)], MemoryMB: 1024, DiskMBPerMinute: 20);

    public static readonly LoadProfile Extreme = new(
        "extremo", "Extremo", "Carga casi máxima con pausas breves.",
        [Burst(55, 100), Rest(5, 40, memoryActive: true)], MemoryMB: 2048, DiskMBPerMinute: 50);

    public static IReadOnlyList<LoadProfile> BuiltIn { get; } = [Light, Medium, High, Extreme];

    public static LoadProfile Custom(double burstCpu, double restCpu, int burstSeconds, int restSeconds, int memoryMB, int diskMBPerMinute) => new(
        "personalizado", "Personalizado",
        $"Ráfagas de {burstSeconds} s al {burstCpu:0} % y reposos de {restSeconds} s al {restCpu:0} %.",
        [Burst(burstSeconds, burstCpu), Rest(restSeconds, restCpu)], memoryMB, diskMBPerMinute);

    private static LoadPhase Burst(int seconds, double cpu) => new("Ráfaga", TimeSpan.FromSeconds(seconds), cpu, MemoryActive: true);

    private static LoadPhase Rest(int seconds, double cpu, bool memoryActive = false) =>
        new("Reposo", TimeSpan.FromSeconds(seconds), cpu, memoryActive);
}
