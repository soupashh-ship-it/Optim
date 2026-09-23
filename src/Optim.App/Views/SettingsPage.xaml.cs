using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optim.Core.Tweaks;

namespace Optim.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly ChangeJournal _journal = App.Get<ChangeJournal>();
    private readonly RegistryTweakEngine _engine = App.Get<RegistryTweakEngine>();

    public SettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        LogPathText.Text = Optim.Core.Logging.FileLogger.LogDirectory;
        RefreshJournalCount();
        RestoreSavedTheme();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        // Journal changes from other pages; refresh the count on every visit.
        RefreshJournalCount();
        base.OnNavigatedTo(e);
    }

    private void RestoreSavedTheme()
    {
        var saved = App.AppSettings.GetString("Theme");
        if (string.IsNullOrEmpty(saved))
        {
            return;
        }

        foreach (var rb in ThemeButtons.Items.OfType<RadioButton>())
        {
            if ((string?)rb.Tag == saved)
            {
                rb.IsChecked = true;
                break;
            }
        }

        ApplyTheme(saved);
    }

    private void RefreshJournalCount()
    {
        JournalCount.Text = TuneKit.Count(_journal.Snapshot().Count, "journaled change", "journaled changes");
    }

    private async void RevertAll_Click(object sender, RoutedEventArgs e)
    {
        var count = _journal.Snapshot().Count;
        if (count == 0)
        {
            Show("Nothing to revert — the journal is empty.", InfoBarSeverity.Informational);
            return;
        }

        var confirm = new ContentDialog
        {
            Title = "Revert all changes",
            Content = $"Restore all {TuneKit.Count(count, "journaled registry value", "journaled registry values")} to their pre-Optim state?",
            PrimaryButtonText = "Revert",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await Task.Run(_engine.RevertAllFromJournal);
            Show("All journaled changes were reverted.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"RevertAll: {ex.Message}");
            Show($"Revert failed: {ex.Message}", InfoBarSeverity.Error);
        }
        RefreshJournalCount();
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeButtons.SelectedItem is RadioButton { Tag: string tag })
        {
            ApplyTheme(tag);

            // ApplicationData.Current throws in unpackaged apps; persist to HKCU.
            App.AppSettings.SetString("Theme", tag);
        }
    }

    private static void ApplyTheme(string tag)
    {
        try
        {
            if (!Enum.TryParse<ElementTheme>(tag, out var theme))
            {
                return;
            }

            // Page-level RequestedTheme does not propagate to the title bar or
            // other pages: apply to the shell root so the whole window follows.
            if (App.CurrentWindow?.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme;
            }

            (App.CurrentWindow as MainWindow)?.ApplyCaptionButtons(theme);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"ApplyTheme: {ex.Message}");
        }
    }

    /// <summary>
    /// One-time safety gate per session: before the first real system change,
    /// offer a restore point. Returns false when the user cancels the change.
    /// Non-blocking and dismissible: if the dialog cannot be shown or the
    /// helper fails, the change proceeds (journaling remains the safety net).
    /// </summary>
    public static async Task<bool> EnsureRestorePointOfferAsync(Microsoft.UI.Xaml.XamlRoot? xamlRoot)
    {
        if (SafetyGate.RestoreOfferedOrDone)
        {
            return true;
        }

        SafetyGate.RestoreOfferedOrDone = true;
        if (xamlRoot is null)
        {
            return true;
        }

        try
        {
            var dialog = new ContentDialog
            {
                Title = "Create a restore point first?",
                Content = "Recommended before system changes. Creates a System Restore point now (can take a minute). You can skip and rely on journaled revert instead.",
                PrimaryButtonText = "Create",
                SecondaryButtonText = "Proceed without",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            };
            var choice = await dialog.ShowAsync();
            if (choice == ContentDialogResult.None)
            {
                return false;
            }

            if (choice == ContentDialogResult.Primary)
            {
                var ok = await Task.Run(() => Optim.Core.SystemDeployment.RestorePoint.TryCreate("Optim before changes"));
                if (!ok)
                {
                    await new ContentDialog
                    {
                        Title = "Restore point not created",
                        Content = "System Protection may be off for this drive. You can proceed — every registry change stays journaled and reversible — or cancel and enable it first.",
                        PrimaryButtonText = "Proceed anyway",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = xamlRoot
                    }.ShowAsync();
                    return true;
                }
            }

            return true;
        }
        catch
        {
            return true;
        }
    }

    private static class SafetyGate
    {
        public static bool RestoreOfferedOrDone;
    }

    private async void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Optim.Core.Logging.FileLogger.LogDirectory);
            await Windows.System.Launcher.LaunchFolderAsync(folder);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"OpenLogs: {ex.Message}");
            Show($"Could not open the logs folder: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void Show(string message, InfoBarSeverity severity)
    {
        ResultInfo.Message = message;
        ResultInfo.Severity = severity;
        ResultInfo.IsOpen = true;
    }

    private async void RestorePoint_Click(object sender, RoutedEventArgs e)
    {
        Show("Creating restore point…", InfoBarSeverity.Informational);
        var ok = await Task.Run(() => Optim.Core.SystemDeployment.RestorePoint.TryCreate("Optim before changes"));
        Show(ok
            ? "Restore point created."
            : "Could not create a restore point — enable System Protection for this drive first.",
            ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    private static nint WindowHandle()
    {
        var window = App.CurrentWindow!;
        return WinRT.Interop.WindowNative.GetWindowHandle(window);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var applied = await Task.Run(() => TweakCatalog.All
                .Where(t => _engine.Detect(t) == TweakState.Applied)
                .Select(t => t.Id)
                .ToList());
            if (applied.Count == 0)
            {
                Show("Nothing to export — no tweaks are currently applied.", InfoBarSeverity.Informational);
                return;
            }

            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"optim-backup-{DateTime.Now:yyyyMMdd-HHmmss}"
            };
            picker.FileTypeChoices.Add("Optim backup", new List<string> { ".optim.json" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle());
            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            var payload = new
            {
                app = "Optim",
                version = 1,
                exportedUtc = DateTime.UtcNow,
                appliedTweakIds = applied
            };
            var json = System.Text.Json.JsonSerializer.Serialize(payload,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await Windows.Storage.FileIO.WriteTextAsync(file, json);
            Show($"Exported {TuneKit.Count(applied.Count, "applied tweak", "applied tweaks")}.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Export: {ex.Message}");
            Show($"Export failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle());
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            using var stream = await file.OpenStreamForReadAsync();
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("appliedTweakIds", out var ids)
                || ids.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                Show("Not an Optim backup file.", InfoBarSeverity.Error);
                return;
            }

            var wanted = ids.EnumerateArray()
                .Select(el => el.GetString() ?? "")
                .Where(s => s.Length > 0)
                .Distinct()
                .ToList();
            var confirm = new ContentDialog
            {
                Title = $"Apply {TuneKit.Count(wanted.Count, "backed-up tweak", "backed-up tweaks")}?",
                Content = "Tweaks already on are skipped. Everything applied is journaled.",
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var (ok, skipped, unknown, failed) = await Task.Run(() =>
            {
                var okCount = 0;
                var skippedCount = 0;
                var unknownCount = 0;
                var failedCount = 0;
                foreach (var id in wanted)
                {
                    var def = TweakCatalog.All.FirstOrDefault(t => t.Id == id);
                    if (def is null)
                    {
                        unknownCount++;
                        continue;
                    }
                    try
                    {
                        if (_engine.Detect(def) == TweakState.Applied)
                        {
                            skippedCount++;
                        }
                        else
                        {
                            _engine.Apply(def);
                            okCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        Optim.Core.Logging.FileLogger.Error($"Import {id}: {ex.Message}");
                    }
                }
                return (okCount, skippedCount, unknownCount, failedCount);
            });

            RefreshJournalCount();
            Show($"Import: {ok} applied, {skipped} already on, {unknown} unknown, {failed} failed.",
                failed == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Import: {ex.Message}");
            Show($"Import failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void ViewLogs_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        try
        {
            var dir = Optim.Core.Logging.FileLogger.LogDirectory;
            var latest = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "optim-*.log").OrderDescending().FirstOrDefault()
                : null;
            string text;
            if (latest is null)
            {
                text = "No log files yet.";
            }
            else
            {
                var lines = await Task.Run(() => File.ReadAllLines(latest));
                text = string.Join(Environment.NewLine, lines.TakeLast(200));
            }

            await new ContentDialog
            {
                Title = latest is null ? "Logs" : $"Logs — {Path.GetFileName(latest)} (last 200 lines)",
                Content = new ScrollViewer
                {
                    MaxHeight = 420,
                    Content = new TextBlock
                    {
                        Text = text,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 11,
                        IsTextSelectionEnabled = true,
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                    }
                },
                CloseButtonText = "Close",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"ViewLogs: {ex.Message}");
            Show($"Could not read logs: {ex.Message}", InfoBarSeverity.Error);
        }
    }
}
