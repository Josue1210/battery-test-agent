using System.Windows;
using BatteryTest.Core.Load;

namespace BatteryTest.Agent;

public partial class App : Application
{
    // Dos agentes a la vez duplicarían la carga y uno tomaría el test del otro como "no terminado".
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\BatteryTest.Agent", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Battery Test ya está abierto.", "Battery Test", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        LoadGenerator.CleanupLeftovers();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
