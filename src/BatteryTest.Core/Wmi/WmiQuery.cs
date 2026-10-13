using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;

namespace BatteryTest.Core.Wmi;

/// <summary>
/// Ejecuta consultas WMI y copia los resultados a filas simples. Nunca lanza excepción:
/// si la clase no existe, no hay permisos o el fabricante no la implementa, devuelve una lista vacía.
/// </summary>
internal static class WmiQuery
{
    public const string Cimv2 = @"root\CIMV2";
    public const string Wmi = @"root\WMI";

    public static IReadOnlyList<WmiRow> Run(string scope, string wql)
    {
        var rows = new List<WmiRow>();
        try
        {
            var options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(10), ReturnImmediately = false };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), options);
            using var results = searcher.Get();
            foreach (var obj in results)
            {
                using (obj)
                {
                    var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in obj.Properties)
                        values[property.Name] = property.Value;
                    rows.Add(new WmiRow(values));
                }
            }
        }
        catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException)
        {
            // La clase no está disponible en este equipo: se trata como "sin datos".
        }
        return rows;
    }
}

internal sealed class WmiRow(IReadOnlyDictionary<string, object?> values)
{
    public IReadOnlyDictionary<string, object?> Values => values;

    public string? GetString(string name)
    {
        if (!values.TryGetValue(name, out var value) || value is null)
            return null;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public long? GetLong(string name)
    {
        if (!values.TryGetValue(name, out var value) || value is null)
            return null;
        try
        {
            return value is ulong big ? (big > long.MaxValue ? null : (long)big) : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    public bool? GetBool(string name) =>
        values.TryGetValue(name, out var value) && value is bool b ? b : null;
}
