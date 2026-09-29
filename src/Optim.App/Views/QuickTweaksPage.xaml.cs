using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optim.App.Services;
using Optim.Core.Tweaks;

namespace Optim.App.Views;

/// <summary>
/// Preset-driven tuning surface in the spirit of community optimizers: pick a
/// curated bundle (Minimal / Standard / Advanced), apply it with one click,
/// revert it just as easily. Every bundle is composed from Optim's own tweak
/// catalog — the same single source of truth the per-tweak pages render — so
/// presets can never drift from the tweaks they contain.
/// </summary>
public sealed partial class QuickTweaksPage : Page
{
    private readonly RegistryTweakEngine _engine = App.Get<RegistryTweakEngine>();
    private bool _busy;

    public QuickTweaksPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        // Counts go stale when toggles flip on the per-tweak pages.
        if (!_busy)
        {
            _ = RefreshCountsAsync();
        }
        base.OnNavigatedTo(e);
    }

    private async Task RefreshCountsAsync()
    {
        try
        {
            foreach (var preset in TweakPresets.All)
            {
                var (applied, total) = await CountAppliedAsync(preset);
                UpdatePill(preset.Id, applied, total);
            }
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"QuickTweaks counts: {ex.Message}");
        }
    }

    private async Task<(int Applied, int Total)> CountAppliedAsync(TweakPreset preset)
    {
        var defs = preset.TweakIds
            .Select(id => TweakCatalog.All.FirstOrDefault(t => t.Id == id))
            .OfType<TweakDefinition>()
            .ToList();

        var applied = await Task.Run(() => defs.Count(d => _engine.Detect(d) == TweakState.Applied));
        return (applied, defs.Count);
    }

    private void UpdatePill(string presetId, int applied, int total)
    {
        var pill = this.FindName($"{presetId}CountPill") as TextBlock;
        var applyBtn = this.FindName($"{presetId}Apply") as Button;
        var revertBtn = this.FindName($"{presetId}Revert") as Button;

        if (pill is not null)
        {
            pill.Text = $"{applied} of {total} applied";
        }

        if (applyBtn is not null)
        {
            applyBtn.IsEnabled = !_busy && applied < total;
            applyBtn.Content = applied == 0 ? "Apply all" : $"Apply {total - applied} remaining";
        }

        if (revertBtn is not null)
        {
            revertBtn.IsEnabled = !_busy && applied > 0;
        }
    }

    private async Task RunPresetAsync(TweakPreset preset, bool apply)
    {
        if (_busy || XamlRoot is null)
        {
            return;
        }

        var verb = apply ? "Apply" : "Revert";
        var defs = preset.TweakIds
            .Select(id => TweakCatalog.All.FirstOrDefault(t => t.Id == id))
            .OfType<TweakDefinition>()
            .ToList();

        _busy = true;
        SetBusy(preset.Id, true);
        try
        {
            if (apply && !await SettingsPage.EnsureRestorePointOfferAsync(XamlRoot))
            {
                return;
            }

            var targets = await Task.Run(() => defs
                .Where(d => (_engine.Detect(d) == TweakState.Applied) != apply)
                .ToList());
            if (targets.Count == 0)
            {
                ToastService.Show($"{preset.Title}: already {(apply ? "fully applied" : "fully reverted")}.", ToastKind.Info);
                return;
            }

            var (done, failed) = await Task.Run(() =>
            {
                var okCount = 0;
                var failCount = 0;
                foreach (var def in targets)
                {
                    try
                    {
                        if (apply)
                        {
                            _engine.Apply(def);
                        }
                        else
                        {
                            _engine.Revert(def);
                        }
                        okCount++;
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        Optim.Core.Logging.FileLogger.Error($"QuickTweaks {verb} {def.Id}: {ex.Message}");
                    }
                }
                return (okCount, failCount);
            });

            var msg = $"{preset.Title}: {TuneKit.Count(done, "tweak", "tweaks")} {(apply ? "applied" : "reverted")}"
                + (failed == 0 ? "." : $", {failed} failed — see logs.");
            ToastService.Show(msg, failed == 0 ? ToastKind.Success : ToastKind.Warning);
            Show(msg, failed == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"QuickTweaks {verb} {preset.Id}: {ex.Message}");
            Show($"{verb} failed: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            _busy = false;
            SetBusy(preset.Id, false);
            await RefreshCountsAsync();
        }
    }

    private void SetBusy(string presetId, bool busy)
    {
        var ring = this.FindName($"{presetId}Ring") as ProgressRing;
        if (ring is not null)
        {
            ring.IsActive = busy;
            ring.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var suffix in new[] { "Apply", "Revert", "Details" })
        {
            if (this.FindName($"{presetId}{suffix}") is Button btn)
            {
                btn.IsEnabled = !busy;
            }
        }
    }

    private void Show(string message, InfoBarSeverity severity)
    {
        ResultInfo.Message = message;
        ResultInfo.Severity = severity;
        ResultInfo.IsOpen = true;
    }

    private async void MinimalApply_Click(object sender, RoutedEventArgs e) =>
        await RunPresetAsync(TweakPresets.Minimal, apply: true);

    private async void MinimalRevert_Click(object sender, RoutedEventArgs e) =>
        await RunPresetAsync(TweakPresets.Minimal, apply: false);

    private async void StandardApply_Click(object sender, RoutedEventArgs e) =>
        await RunPresetAsync(TweakPresets.Standard, apply: true);

    private async void StandardRevert_Click(object sender, RoutedEventArgs e) =>
        await RunPresetAsync(TweakPresets.Standard, apply: false);

    private async void AdvancedApply_Click(object sender, RoutedEventArgs e) =>
        await RunPresetAsync(TweakPresets.Advanced, apply: true);

    private async void AdvancedRevert_Click(object sender, RoutedEventArgs e) =>
        await RunPresetAsync(TweakPresets.Advanced, apply: false);

    private async void Details_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        try
        {
            var presetId = (sender as FrameworkElement)?.Tag as string;
            var preset = TweakPresets.All.FirstOrDefault(p => p.Id == presetId);
            if (preset is null)
            {
                return;
            }

            var defs = preset.TweakIds
                .Select(id => TweakCatalog.All.FirstOrDefault(t => t.Id == id))
                .OfType<TweakDefinition>()
                .ToList();
            var journey = await Task.Run(() => defs
                .Select(d => (Def: d, On: _engine.Detect(d) == TweakState.Applied))
                .ToList());

            var panel = new StackPanel { Spacing = 4 };
            foreach (var (def, on) in journey)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(new TextBlock
                {
                    Text = on ? "\uE73E" : "\uE739",
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        on ? Microsoft.UI.Colors.ForestGreen : Microsoft.UI.Colors.Gray)
                });
                row.Children.Add(new TextBlock { Text = def.Title, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(row);
            }

            await new ContentDialog
            {
                Title = $"{preset.Title} — {TuneKit.Count(defs.Count, "tweak", "tweaks")}",
                Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
                CloseButtonText = "Close",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"QuickTweaks details: {ex.Message}");
        }
    }
}
