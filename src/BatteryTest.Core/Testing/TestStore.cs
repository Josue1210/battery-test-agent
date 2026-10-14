namespace BatteryTest.Core.Testing;

/// <summary>Carpeta donde se guardan los tests (por defecto, %LOCALAPPDATA%\BatteryTest\tests).</summary>
public sealed class TestStore(string folder)
{
    public static TestStore Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryTest", "tests"));

    public string Folder => folder;

    public string NewTestPath(DateTimeOffset start, Guid id)
    {
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"{start.LocalDateTime:yyyyMMdd-HHmmss}-{id.ToString("N")[..8]}.jsonl");
    }

    /// <summary>Tests que tienen cabecera pero no registro de fin: el agente no llegó a cerrarlos.</summary>
    public IReadOnlyList<TestFile> FindIncomplete() =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.jsonl").Order().Select(TestFile.Read).Where(f => f.IsIncomplete).ToList()
            : [];

    /// <summary>Cierra un test que quedó abierto. La hora de fin es la de la última lectura guardada.</summary>
    public static TestFile CloseIncomplete(TestFile file)
    {
        var lastTime = file.Samples.Count > 0 ? file.Samples[^1].Time : file.Header!.Time;
        var end = new TestEnd(lastTime, TestEndReason.Shutdown,
            "El agente no llegó a cerrar el test; lo más probable es que el equipo se apagara al agotarse la batería.");
        using (var writer = new TestLogWriter(file.Path))
            writer.Append(end);
        return file with { End = end };
    }
}
