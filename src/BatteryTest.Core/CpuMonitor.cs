using System.ComponentModel;
using System.Diagnostics;
using BatteryTest.Core.Models;

namespace BatteryTest.Core;

/// <summary>
/// Uso real de CPU y frecuencia actual mediante contadores de rendimiento.
/// Usa "% Processor Utility" (el mismo que el Administrador de tareas) y, si no existe,
/// "% Processor Time". La frecuencia sale de "% Processor Performance" × frecuencia base.
/// </summary>
public sealed class CpuMonitor : IDisposable
{
    private readonly PerformanceCounter? _usage;
    private readonly PerformanceCounter? _performance;
    private readonly int? _baseClockMHz;

    public CpuMonitor(int? baseClockMHz)
    {
        _baseClockMHz = baseClockMHz;
        _usage = TryCreate("Processor Information", "% Processor Utility")
                 ?? TryCreate("Processor", "% Processor Time");
        _performance = baseClockMHz is > 0 ? TryCreate("Processor Information", "% Processor Performance") : null;
    }

    /// <summary>Devuelve el promedio desde la llamada anterior.</summary>
    public CpuSample Sample()
    {
        var usage = TryRead(_usage) is { } u ? Math.Clamp(u, 0, 100) : (double?)null;
        var frequency = TryRead(_performance) is { } p && p > 0 && _baseClockMHz is { } baseMHz
            ? (int)Math.Round(baseMHz * p / 100)
            : (int?)null;
        return new CpuSample(usage, frequency);
    }

    public void Dispose()
    {
        _usage?.Dispose();
        _performance?.Dispose();
    }

    private static PerformanceCounter? TryCreate(string category, string counter)
    {
        try
        {
            var pc = new PerformanceCounter(category, counter, "_Total", readOnly: true);
            pc.NextValue(); // La primera lectura siempre es 0: sirve de referencia.
            return pc;
        }
        catch (Exception e) when (IsCounterError(e))
        {
            return null;
        }
    }

    private static double? TryRead(PerformanceCounter? counter)
    {
        if (counter is null)
            return null;
        try
        {
            return counter.NextValue();
        }
        catch (Exception e) when (IsCounterError(e))
        {
            return null;
        }
    }

    private static bool IsCounterError(Exception e) =>
        e is InvalidOperationException or UnauthorizedAccessException or Win32Exception or FormatException;
}
