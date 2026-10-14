using System.Runtime.InteropServices;

namespace BatteryTest.Core.Conditions;

public sealed record PowerPlan(Guid Id, string? Name, bool IsPowerSaver);

/// <summary>Plan de energía activo, leído con la API de Windows (sin ejecutar powercfg).</summary>
public static partial class PowerPlanReader
{
    private static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    public static PowerPlan? Read()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var guidPointer) != 0 || guidPointer == IntPtr.Zero)
            return null;
        Guid id;
        try
        {
            id = Marshal.PtrToStructure<Guid>(guidPointer);
        }
        finally
        {
            LocalFree(guidPointer);
        }
        return new PowerPlan(id, ReadName(id), id == PowerSaver);
    }

    private static string? ReadName(Guid id)
    {
        uint size = 0;
        if (PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size) != 0 || size == 0)
            return null;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            return PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
                ? Marshal.PtrToStringUni(buffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadFriendlyName(
        IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupGuid, IntPtr settingGuid, IntPtr buffer, ref uint bufferSize);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
}
