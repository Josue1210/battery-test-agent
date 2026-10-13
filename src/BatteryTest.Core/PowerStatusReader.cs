using System.Runtime.InteropServices;
using BatteryTest.Core.Models;

namespace BatteryTest.Core;

/// <summary>
/// Estado de energía desde GetSystemPowerStatus. Es la fuente más confiable para saber
/// si hay cargador conectado y si el ahorro de batería está activo.
/// </summary>
public static partial class PowerStatusReader
{
    private const byte Unknown = 255;
    private const byte NoSystemBattery = 128;
    private const byte BatterySaverFlag = 1;

    public static PowerStatus Read()
    {
        if (!GetSystemPowerStatus(out var s))
            return new PowerStatus(null, null, null, false, HasBattery: true);

        return new PowerStatus(
            AcOnline: s.ACLineStatus switch { 0 => false, 1 => true, _ => null },
            BatteryPercent: s.BatteryLifePercent is <= 100 ? s.BatteryLifePercent : null,
            EstimatedRemaining: s.BatteryLifeTime > 0 ? TimeSpan.FromSeconds(s.BatteryLifeTime) : null,
            BatterySaverOn: (s.SystemStatusFlag & BatterySaverFlag) != 0,
            HasBattery: s.BatteryFlag == Unknown || (s.BatteryFlag & NoSystemBattery) == 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus status);
}
