using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optim.App.Services;
using Optim.Core.Cleanup;
using Optim.Core.Debloat;
using Optim.Core.Metrics;
using Optim.Core.Processes;
using Optim.Core.ServicesMgmt;

namespace Optim.App.Views;

public sealed partial class HomePage : Page
{
    private readonly MetricsEngine _metrics = App.Get<MetricsEngine>();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };

    public HomePage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
        GreetingText.Text = DateTime.Now.Hour switch
        {
            < 5 => "Up late?",
            < 12 => "Good morning",
            < 18 => "Good afternoon",
            _ => "Good evening"
        };
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        // Counts go stale after debloat/uninstall, so reload on every visit.
        // Slightly delayed so the first paint (graphs) never waits on enumeration.
        _ = DelayedCountsAsync();
        base.OnNavigatedTo(e);
    }

    private async Task DelayedCountsAsync()
    {
        try
        {
            await Task.Delay(400);
            await LoadCountsAsync();
        }
        catch
        {
        }
    }

    private void Refresh()
    {
        try
        {
            var s = _metrics.Sample();
            CpuText.Text = s.CpuPercent.ToString("F0", CultureInfo.InvariantCulture) + "%";
            RamText.Text = s.RamPercent.ToString("F0", CultureInfo.InvariantCulture) + "%";
            DiskText.Text = s.DiskAvailable
                ? s.DiskPercent.ToString("F0", CultureInfo.InvariantCulture) + "%"
                : "N/A";

            CpuGraph.AddValue(s.CpuPercent);
            RamGraph.AddValue(s.RamPercent);
            if (s.DiskAvailable)
            {
                DiskGraph.AddValue(s.DiskPercent);
            }
            else
            {
                // Keep the chart from presenting pre-failure samples as current.
                DiskGraph.MarkUnavailable();
            }
        }
        catch
        {
            // Sampling must never crash the UI.
        }
    }

    private async Task LoadCountsAsync()
    {
        try
        {
            // PackageManager is UI-affinitized: enumerate packages on the UI thread.
            var apps = App.Get<DebloatEngine>().ListInstalled().Count;
            AppsCountText.Text = apps.ToString(CultureInfo.InvariantCulture);

            var services = await Task.Run(() => App.Get<ServiceEngine>().List().Count);
            ServicesCountText.Text = services.ToString(CultureInfo.InvariantCulture);

            var processes = await Task.Run(ProcessEngine.Snapshot);
            ProcessesCountText.Text = processes.Count.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Home counts: {ex.Message}");
        }
    }

    private async void FlushDns_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var code = await Optim.Core.Network.NetworkEngine.FlushDnsAsync();
            Show(code == 0 ? "DNS resolver cache flushed." : $"DNS flush returned exit code {code} — run elevated.");
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"FlushDns: {ex}");
            Show("Could not flush DNS — see logs.");
        }
    }

    private async void RestorePoint_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        try
        {
            var confirm = new ContentDialog
            {
                Title = "Create restore point",
                Content = "Create a system restore point now? This can take a minute.",
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            // Same helper as Settings (RestorePoint.TryCreate): one code path,
            // one success definition.
            Show("Creating a restore point… this can take a minute.");
            var ok = await Task.Run(() => Optim.Core.SystemDeployment.RestorePoint.TryCreate("Optim restore point"));
            Show(ok
                ? "Restore point created."
                : "Restore point creation failed — enable System Protection for this drive, then run elevated.");
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"RestorePoint: {ex}");
            Show("Restore point creation failed — see logs.");
        }
    }

    private async void CleanTemp_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        try
        {
            var targets = CleanupEngine.Targets.Where(t => !t.IncludeRecycleBin).ToList();
            var estimate = await Task.Run(() => targets.Sum(CleanupEngine.Measure));
            var confirm = new ContentDialog
            {
                Title = "Clean temporary files",
                Content = $"Delete temp and shader-cache files (about {Optim.Core.SystemInfo.SystemInfoEngine.FormatBytes(estimate)})? Locked files are skipped.",
                PrimaryButtonText = "Clean",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            Show("Cleaning temporary files…");
            var results = await Task.Run(() => targets.Select(CleanupEngine.Clean).ToList());
            var freed = results.Sum(r => r.BytesFreed);
            var errors = results.Sum(r => r.Errors);
            var msg = $"Freed {Optim.Core.SystemInfo.SystemInfoEngine.FormatBytes(freed)} from temp files."
                + (errors > 0 ? $" {TuneKit.Count(errors, "locked file", "locked files")} skipped." : "");
            Show(msg);
            ToastService.Show(msg, errors > 0 ? ToastKind.Warning : ToastKind.Success);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"CleanTemp: {ex.Message}");
            Show("Cleanup failed — see logs.");
        }
    }

    private async void EmptyRecycleBin_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        try
        {
            var target = CleanupEngine.Targets.First(t => t.IncludeRecycleBin);
            var confirm = new ContentDialog
            {
                Title = "Empty Recycle Bin",
                Content = "Permanently delete everything in the Recycle Bin on all drives?",
                PrimaryButtonText = "Empty",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var result = await Task.Run(() => CleanupEngine.Clean(target));
            var msg = result.Errors == 0 ? "Recycle Bin emptied." : "Could not empty the Recycle Bin — see logs.";
            Show(msg);
            ToastService.Show(msg, result.Errors == 0 ? ToastKind.Success : ToastKind.Error);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"EmptyRecycleBin: {ex.Message}");
            Show("Cleanup failed — see logs.");
        }
    }

    private void Policies_Click(object sender, RoutedEventArgs e)
    {
        (App.CurrentWindow?.Content as ShellPage)?.NavigateTo("policies");
    }

    private void Show(string message)
    {
        ActionInfo.Title = "Done";
        ActionInfo.Message = message;
        ActionInfo.Severity = InfoBarSeverity.Informational;
        ActionInfo.IsOpen = true;
    }
}
