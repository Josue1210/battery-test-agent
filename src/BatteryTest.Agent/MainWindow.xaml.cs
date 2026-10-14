using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BatteryTest.Core;
using BatteryTest.Core.Conditions;
using BatteryTest.Core.Load;
using BatteryTest.Core.Models;
using BatteryTest.Core.Testing;

namespace BatteryTest.Agent;

public partial class MainWindow : Window
{
    private static readonly TimeSpan CpuInterval = TimeSpan.FromSeconds(2);
    private const int BatteryEveryNTicks = 5; // batería cada 10 s
    private const int BackgroundSamples = 5;  // promedio de CPU de los últimos 10 s
    private const double BusyBackgroundPercent = 15;
    private const int TestBrightnessPercent = 50;
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(30);

    private readonly DispatcherTimer _timer = new() { Interval = CpuInterval };
    private readonly Queue<double> _recentCpu = new();
    private CpuMonitor? _cpu;
    private HardwareInfo? _hardware;
    private IReadOnlyList<BatteryReading> _batteries = [];
    private PowerStatus? _power;
    private PowerPlan? _plan;
    private bool _brightnessControllable;
    private TestSession? _session;
    private string _runningProfileName = "";
    private bool _refreshingBattery;
    private int _tick;

    private sealed record ProfileChoice(string Name, LoadProfile? Profile);

    public MainWindow()
    {
        InitializeComponent();
        ProfileBox.ItemsSource = LoadProfile.BuiltIn.Select(p => new ProfileChoice(p.Name, p))
            .Append(new ProfileChoice("Personalizado", null)).ToList();
        ProfileBox.SelectedIndex = 1; // Medio
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        _timer.Tick += OnTick;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Leyendo hardware…";
        // WMI y los contadores de rendimiento pueden tardar: se leen fuera del hilo de la interfaz.
        _hardware = await Task.Run(HardwareReader.Read);
        HardwareList.ItemsSource = InfoRows.Hardware(_hardware);
        _cpu = await Task.Run(() => new CpuMonitor(_hardware.Cpu?.BaseClockMHz));
        await RecoverIncompleteTestsAsync();
        await RefreshBatteryAsync();
        _timer.Start();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_cpu is not null)
        {
            var sample = _cpu.Sample();
            CpuList.ItemsSource = InfoRows.Cpu(sample);
            if (_session is null && sample.UsagePercent is { } usage)
            {
                _recentCpu.Enqueue(usage);
                while (_recentCpu.Count > BackgroundSamples)
                    _recentCpu.Dequeue();
            }
        }
        if (++_tick % BatteryEveryNTicks == 0)
            await RefreshBatteryAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshBatteryAsync();

    private async Task RefreshBatteryAsync()
    {
        if (_refreshingBattery)
            return;
        _refreshingBattery = true;
        try
        {
            var (batteries, power, plan, brightness) = await Task.Run(() =>
                (BatteryReader.Read(), PowerStatusReader.Read(), PowerPlanReader.Read(), BrightnessController.Get()));
            _batteries = batteries;
            _power = power;
            _plan = plan;
            _brightnessControllable = brightness is not null;
            BatteryList.ItemsSource = InfoRows.Batteries(batteries);
            PowerList.ItemsSource = InfoRows.Power(power, plan);
            StatusText.Text = $"Actualizado a las {DateTime.Now:T}";
            UpdateReadiness();
        }
        finally
        {
            _refreshingBattery = false;
        }
    }

    // ---------- Configuración del test ----------

    private void OnSetupChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
            UpdateReadiness();
    }

    private CheckBox[] Checklist => [CheckApps, CheckRadios, CheckPeripherals, CheckRisk];

    /// <summary>Muestra los avisos y habilita «Iniciar test» solo si se puede empezar.</summary>
    private void UpdateReadiness()
    {
        var choice = ProfileBox.SelectedItem as ProfileChoice;
        CustomPanel.Visibility = choice?.Profile is null ? Visibility.Visible : Visibility.Collapsed;
        var profile = SelectedProfile(out var profileError);
        ProfileDescription.Text = profile is null
            ? ""
            : $"{profile.Description} Uso medio de CPU: {profile.AverageCpuPercent:0} %.";

        var blockers = new List<string>();
        var warnings = new List<string>();
        if (profileError is not null)
            blockers.Add(profileError);
        if (!TryParse(MinPercentBox.Text, 0, 50, out var minPercent))
            blockers.Add("El mínimo de batería debe ser un número entre 0 y 50.");

        if (_power is { HasBattery: false } || (_power is not null && _batteries.Count == 0))
            blockers.Add("No se detectó ninguna batería: el test solo funciona en laptops.");
        else if (_power is not null)
        {
            if (_power.AcOnline == true)
                blockers.Add("Desconecta el cargador para empezar.");
            if (_power.BatteryPercent is { } percent)
            {
                if (percent <= minPercent + 5)
                    blockers.Add("La batería está demasiado baja para empezar.");
                else if (percent < 90)
                    warnings.Add($"La batería está al {percent} %. Para un resultado completo, cárgala al 100 % antes de empezar; si no, la duración total se estima.");
            }
            if (_power.BatterySaverOn)
                warnings.Add("El ahorro de batería está activado y cambia el resultado. Desactívalo para un test estándar.");
            if (_plan?.IsPowerSaver == true)
                warnings.Add($"El plan de energía activo es «{_plan.Name}». Para un test estándar usa el plan equilibrado.");
            if (!_brightnessControllable)
                warnings.Add("No se puede controlar el brillo de esta pantalla: déjalo a mano en un valor medio y no lo cambies durante el test.");
        }
        if (_recentCpu.Count == BackgroundSamples && _recentCpu.Average() > BusyBackgroundPercent)
            warnings.Add($"Hay mucha actividad de fondo ({_recentCpu.Average():0} % de CPU sin el test). Cierra programas o espera a que termine (por ejemplo, Windows Update).");

        WarningsText.Text = string.Join(Environment.NewLine, blockers.Concat(warnings).Select(m => "• " + m));
        StartButton.IsEnabled = blockers.Count == 0 && _hardware is not null && Checklist.All(c => c.IsChecked == true);
    }

    private LoadProfile? SelectedProfile(out string? error)
    {
        error = null;
        if (ProfileBox.SelectedItem is not ProfileChoice choice)
            return null;
        if (choice.Profile is not null)
            return choice.Profile;

        if (TryParse(CustomBurstCpu.Text, 0, 100, out var burstCpu) && TryParse(CustomRestCpu.Text, 0, 100, out var restCpu)
            && TryParse(CustomBurstSeconds.Text, 1, 600, out var burstSeconds) && TryParse(CustomRestSeconds.Text, 1, 600, out var restSeconds)
            && TryParse(CustomMemory.Text, 0, 4096, out var memory))
            return LoadProfile.Custom(burstCpu, restCpu, burstSeconds, restSeconds, memory, diskMBPerMinute: 5);

        error = "Revisa el perfil personalizado: CPU de 0 a 100 %, duraciones de 1 a 600 s y memoria de 0 a 4096 MB.";
        return null;
    }

    private static bool TryParse(string text, int min, int max, out int value) =>
        int.TryParse(text.Trim(), out value) && value >= min && value <= max;

    // ---------- Test ----------

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        var profile = SelectedProfile(out _);
        if (profile is null || _hardware is null || !TryParse(MinPercentBox.Text, 0, 50, out var minPercent))
            return;

        var checklist = Checklist.Select(c => c.Content is TextBlock t ? t.Text : c.Content?.ToString() ?? "").ToList();
        var options = new TestOptions(profile, minPercent, SampleInterval, TestBrightnessPercent, checklist);
        _session = new TestSession(options, _hardware, TestStore.Default);
        _session.StatusChanged += status => Dispatcher.BeginInvoke(() => TestStatusList.ItemsSource = InfoRows.TestStatus(status, _runningProfileName));
        _session.Finished += result => Dispatcher.BeginInvoke(() => OnTestFinished(result));
        _runningProfileName = profile.Name;

        SetMode(running: true);
        TestStatusList.ItemsSource = new[] { new InfoRow("Estado", "Preparando el test…") };
        _session.Start();
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        StopButton.Content = "Deteniendo…";
        _session?.Stop();
    }

    private void OnTestFinished(TestResult result)
    {
        _session = null;
        SetMode(running: false);
        ShowResult("Resultado del test", result.File, result.Summary, result.Reason, result.Detail);
        foreach (var check in Checklist)
            check.IsChecked = false; // cada test nuevo se vuelve a confirmar
        _ = RefreshBatteryAsync();
    }

    private void ShowResult(string title, TestFile? file, TestSummary? summary, TestEndReason reason, string? detail)
    {
        ResultTitle.Text = title;
        ResultList.ItemsSource = InfoRows.Result(file, summary, reason, detail);
        SetupPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
    }

    private void SetMode(bool running)
    {
        SetupPanel.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        RunningPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        StopButton.IsEnabled = true;
        StopButton.Content = "Detener test";
        Title = running ? "Battery Test — test en curso" : "Battery Test — Agente";
    }

    private void OnNewTestClick(object sender, RoutedEventArgs e)
    {
        ResultPanel.Visibility = Visibility.Collapsed;
        SetupPanel.Visibility = Visibility.Visible;
        UpdateReadiness();
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(TestStore.Default.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{TestStore.Default.Folder}\"") { UseShellExecute = true });
    }

    /// <summary>
    /// Si un test quedó abierto (la laptop se apagó sin batería), se cierra con su última lectura
    /// y se restaura el brillo que tenía el usuario antes del test.
    /// </summary>
    private async Task RecoverIncompleteTestsAsync()
    {
        var incomplete = await Task.Run(TestStore.Default.FindIncomplete);
        foreach (var open in incomplete)
        {
            var closed = await Task.Run(() =>
            {
                var file = TestStore.CloseIncomplete(open);
                if (file.Header is { BrightnessTarget: not null, BrightnessOriginal: { } original })
                    BrightnessController.Set(original);
                return file;
            });
            var summary = TestSummary.From(closed);
            ShowResult($"Test recuperado del {closed.Header!.Time.LocalDateTime:g}", closed, summary, TestEndReason.Shutdown, closed.End?.Detail);
            MessageBox.Show(this,
                $"Se encontró un test del {closed.Header.Time.LocalDateTime:g} que no llegó a cerrarse. " +
                "Lo más probable es que la laptop se apagara al agotarse la batería.\n\n" +
                "Se cerró con la última lectura guardada y se restauró tu brillo de pantalla. Puedes ver el resultado en la ventana.",
                "Test recuperado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ---------- Reporte y cierre ----------

    private async void OnCopyReportClick(object sender, RoutedEventArgs e)
    {
        await RefreshBatteryAsync();
        var raw = await Task.Run(BatteryReader.ReadRaw);
        var sections = new List<(string, IEnumerable<InfoRow>)>();
        if (RunningPanel.Visibility == Visibility.Visible)
            sections.Add(("Test en curso", Rows(TestStatusList)));
        if (ResultPanel.Visibility == Visibility.Visible)
            sections.Add((ResultTitle.Text, Rows(ResultList)));
        sections.Add(("Batería", Rows(BatteryList)));
        sections.Add(("Energía", Rows(PowerList)));
        sections.Add(("CPU en vivo", Rows(CpuList)));
        sections.Add(("Equipo", Rows(HardwareList)));
        var report = ReportText.Build(sections, raw);

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

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_session is null)
            return;
        var answer = MessageBox.Show(this, "Hay un test en curso. ¿Quieres detenerlo y salir?",
            "Test en curso", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        // Espera a que el test guarde el cierre y restaure el brillo.
        _session.Stop();
        _session.WaitForExit(TimeSpan.FromSeconds(15));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _cpu?.Dispose();
    }
}
