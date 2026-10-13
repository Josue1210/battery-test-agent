using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BatteryTest.Core;

namespace BatteryTest.Agent;

public partial class MainWindow : Window
{
    private static readonly TimeSpan CpuInterval = TimeSpan.FromSeconds(2);
    private const int BatteryEveryNTicks = 5; // batería cada 10 s

    private readonly DispatcherTimer _timer = new() { Interval = CpuInterval };
    private CpuMonitor? _cpu;
    private bool _refreshingBattery;
    private int _tick;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
        _timer.Tick += OnTick;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Leyendo hardware…";
        // WMI y los contadores de rendimiento pueden tardar: se leen fuera del hilo de la interfaz.
        var hardware = await Task.Run(HardwareReader.Read);
        HardwareList.ItemsSource = InfoRows.Hardware(hardware);
        _cpu = await Task.Run(() => new CpuMonitor(hardware.Cpu?.BaseClockMHz));
        await RefreshBatteryAsync();
        _timer.Start();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_cpu is not null)
            CpuList.ItemsSource = InfoRows.Cpu(_cpu.Sample());
        if (++_tick % BatteryEveryNTicks == 0)
            await RefreshBatteryAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshBatteryAsync();

    private async void OnCopyReportClick(object sender, RoutedEventArgs e)
    {
        await RefreshBatteryAsync();
        var raw = await Task.Run(BatteryReader.ReadRaw);
        var report = ReportText.Build(
        [
            ("Batería", Rows(BatteryList)),
            ("Energía", Rows(PowerList)),
            ("CPU en vivo", Rows(CpuList)),
            ("Equipo", Rows(HardwareList)),
        ], raw);

        try
        {
            Clipboard.SetText(report);
            StatusText.Text = $"Reporte copiado al portapapeles ({DateTime.Now:T})";
        }
        catch (COMException)
        {
            // Otro programa tiene el portapapeles abierto.
            StatusText.Text = "No se pudo copiar: el portapapeles está ocupado. Intenta de nuevo.";
        }
    }

    private static IEnumerable<InfoRow> Rows(ItemsControl list) =>
        list.ItemsSource as IEnumerable<InfoRow> ?? [];

    private async Task RefreshBatteryAsync()
    {
        if (_refreshingBattery)
            return;
        _refreshingBattery = true;
        try
        {
            var (batteries, power) = await Task.Run(() => (BatteryReader.Read(), PowerStatusReader.Read()));
            BatteryList.ItemsSource = InfoRows.Batteries(batteries);
            PowerList.ItemsSource = InfoRows.Power(power);
            StatusText.Text = $"Actualizado a las {DateTime.Now:T}";
        }
        finally
        {
            _refreshingBattery = false;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _cpu?.Dispose();
    }
}
