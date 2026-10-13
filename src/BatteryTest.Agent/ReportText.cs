using System.Text;
using BatteryTest.Core.Models;

namespace BatteryTest.Agent;

/// <summary>Arma el reporte en texto plano para copiarlo y pegarlo donde se necesite.</summary>
public static class ReportText
{
    public static string Build(
        IEnumerable<(string Title, IEnumerable<InfoRow> Rows)> sections,
        IReadOnlyList<RawWmiObject> rawBattery)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Battery Test — reporte del agente (prototipo)");
        sb.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        foreach (var (title, rows) in sections)
        {
            sb.AppendLine().AppendLine($"## {title}");
            foreach (var row in rows)
                sb.AppendLine($"{row.Label}: {row.Value}");
        }

        sb.AppendLine().AppendLine("## Datos crudos de WMI (batería, sin número de serie)");
        if (rawBattery.Count == 0)
            sb.AppendLine("Ninguna clase de batería devolvió datos.");
        foreach (var obj in rawBattery)
        {
            sb.AppendLine($"{obj.ClassName}:");
            foreach (var (name, value) in obj.Properties)
                sb.AppendLine($"  {name} = {value}");
        }
        return sb.ToString();
    }
}
