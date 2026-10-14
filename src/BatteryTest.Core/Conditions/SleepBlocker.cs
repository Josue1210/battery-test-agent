using System.Runtime.InteropServices;

namespace BatteryTest.Core.Conditions;

/// <summary>
/// Evita que Windows suspenda el equipo o apague la pantalla durante el test.
/// El efecto se asocia al hilo que llama: Block y Release deben llamarse desde el mismo hilo.
/// </summary>
public static partial class SleepBlocker
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    public static bool Block() => SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired) != 0;

    public static void Release() => SetThreadExecutionState(EsContinuous);

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint flags);
}
