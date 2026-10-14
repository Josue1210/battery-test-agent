using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryTest.Core.Testing;

/// <summary>
/// Registro del test en formato JSON Lines: una línea por registro.
/// Cada línea se escribe y se fuerza al disco antes de seguir, porque la laptop se apaga al final del test.
/// Si el apagado corta la última línea, al leer se ignora.
/// </summary>
public sealed class TestLogWriter : IDisposable
{
    private readonly FileStream _stream;

    public TestLogWriter(string path)
    {
        var needsNewLine = File.Exists(path) && !EndsWithNewLine(path);
        _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        if (needsNewLine)
            _stream.WriteByte((byte)'\n');
    }

    public void Append(TestRecord record)
    {
        _stream.Write(JsonSerializer.SerializeToUtf8Bytes(record, TestLogJson.Options));
        _stream.WriteByte((byte)'\n');
        _stream.Flush(flushToDisk: true);
    }

    public void Dispose() => _stream.Dispose();

    private static bool EndsWithNewLine(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0)
            return true;
        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() == '\n';
    }
}

/// <summary>Un archivo de test ya leído.</summary>
public sealed record TestFile(
    string Path,
    TestHeader? Header,
    IReadOnlyList<TestSample> Samples,
    IReadOnlyList<TestEvent> Events,
    TestEnd? End)
{
    public bool IsIncomplete => Header is not null && End is null;

    public static TestFile Read(string path)
    {
        string text;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            text = reader.ReadToEnd();

        TestHeader? header = null;
        TestEnd? end = null;
        var samples = new List<TestSample>();
        var events = new List<TestEvent>();
        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            TestRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<TestRecord>(line, TestLogJson.Options);
            }
            catch (JsonException)
            {
                continue; // línea cortada por un apagado
            }
            switch (record)
            {
                case TestHeader h: header ??= h; break;
                case TestSample s: samples.Add(s); break;
                case TestEvent e: events.Add(e); break;
                case TestEnd e: end = e; break;
            }
        }
        return new TestFile(path, header, samples, events, end);
    }
}

internal static class TestLogJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        AllowOutOfOrderMetadataProperties = true,
    };
}
