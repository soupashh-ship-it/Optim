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
    private bool _sampling;

    /// <summary>Counts cache: tab switches are cheap, re-enumerating installed
    /// packages every visit is not. Short TTL keeps counts honest.</summary>
    private static readonly TimeSpan CountsTtl = TimeSpan.FromSeconds(60);
    private DateTimeOffset _countsAt = DateTimeOffset.MinValue;

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
            // Fresh enough from the last visit: skip the enumeration entirely.
            if (DateTimeOffset.UtcNow - _countsAt < CountsTtl)
            {
                return;
            }

            await Task.Delay(400);
            await LoadCountsAsync();
        }
        catch
        {
        }
    }

    private void Refresh()
    {
        // PerformanceCounter reads take real time; keep them off the UI thread
        // and skip the tick entirely when the previous sample is still running.
        if (_sampling)
        {
            return;
        }

        _sampling = true;
        _ = Task.Run(() => _metrics.Sample()).ContinueWith(t =>
        {
            _sampling = false;
            if (!t.IsCompletedSuccessfully)
            {
                Optim.Core.Logging.FileLogger.Warn($"Metrics sample failed: {t.Exception?.GetBaseException().Message}");
                return;
            }

            var sample = t.Result;
            DispatcherQueue.TryEnqueue(() => ApplySample(sample));
        });
    }

    private void ApplySample(Optim.Core.Metrics.UsageSample s)
    {
        try
        {
            // Rolled rather than assigned: the graphs below glide to the new
            // sample, and a number that snapped while its curve eased read as a
            // stutter. Same duration, so the figure and its curve move together.
            NumberRoll.To(CpuText, s.CpuPercent, Percent);
            NumberRoll.To(RamText, s.RamPercent, Percent);
            if (s.DiskAvailable)
            {
                NumberRoll.To(DiskText, s.DiskPercent, Percent);
            }
            else
            {
                DiskText.Text = "N/A";
            }

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

    private static string Percent(double value) =>
        value.ToString("F0", CultureInfo.InvariantCulture) + "%";

    private static string Count(double value) =>
        ((int)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    private async Task LoadCountsAsync()
    {
        try
        {
            // Services and processes enumerate happily off the UI thread; run
            // them concurrently so their numbers paint while the (UI-affinitized)
            // package enumeration below is still pending.
            var servicesTask = Task.Run(() => App.Get<ServiceEngine>().List().Count);
            var processesTask = Task.Run(ProcessEngine.Snapshot);

            // PackageManager is UI-affinitized (per-thread WinRT objects): the
            // enumeration itself must stay on the UI thread, so it runs last —
            // after services/processes are already counted and painted.
            // Each figure rolls in as it arrives, so the cards fill in with a
            // little motion instead of three numbers appearing at once.
            var services = await servicesTask;
            NumberRoll.To(ServicesCountText, services, Count);

            var processes = await processesTask;
            NumberRoll.To(ProcessesCountText, processes.Count, Count);

            var apps = App.Get<DebloatEngine>().ListInstalled().Count;
            NumberRoll.To(AppsCountText, apps, Count);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Home counts: {ex.Message}");
            return;
        }

        _countsAt = DateTimeOffset.UtcNow;
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
