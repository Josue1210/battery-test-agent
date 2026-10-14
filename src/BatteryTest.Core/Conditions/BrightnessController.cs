using System.Management;
using System.Runtime.InteropServices;
using BatteryTest.Core.Wmi;

namespace BatteryTest.Core.Conditions;

/// <summary>
/// Brillo de la pantalla interna por WMI (WmiMonitorBrightness / WmiMonitorBrightnessMethods).
/// Solo funciona en pantallas integradas; en monitores externos devuelve null / false.
/// </summary>
public static class BrightnessController
{
    public static int? Get()
    {
        var row = WmiQuery.Run(WmiQuery.Wmi, "SELECT CurrentBrightness, Active FROM WmiMonitorBrightness")
            .FirstOrDefault(r => r.GetBool("Active") != false);
        return row?.GetLong("CurrentBrightness") is long value and >= 0 and <= 100 ? (int)value : null;
    }

    public static bool Set(int percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(WmiQuery.Wmi), new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods"));
            using var results = searcher.Get();
            var applied = false;
            foreach (var obj in results)
            {
                using (obj)
                {
                    // WmiSetBrightness(Timeout en segundos, Brillo 0-100)
                    ((ManagementObject)obj).InvokeMethod("WmiSetBrightness", [1u, (byte)Math.Clamp(percent, 0, 100)]);
                    applied = true;
                }
            }
            return applied;
        }
        catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
