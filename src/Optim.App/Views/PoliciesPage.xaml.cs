using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optim.Core.Policies;

namespace Optim.App.Views;

public sealed record PolicyRow(PolicyFinding Finding)
{
    public string Friendly => PolicyScanEngine.FriendlyNameFor(Finding) ?? Finding.ValueName;
    public Visibility FriendlyVisibility =>
        PolicyScanEngine.FriendlyNameFor(Finding) is null ? Visibility.Collapsed : Visibility.Visible;
    public string Raw => $"{Finding.Path} :: {Finding.ValueName} = {Finding.Value ?? "(default)"}";
}

public sealed partial class PoliciesPage : Page
{
    private List<PolicyRow> _rows = new();
    private IReadOnlyList<PolicyFinding> _findings = Array.Empty<PolicyFinding>();

    public PoliciesPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        FindingsList.ItemContainerTransitions?.Clear();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        // Registry recursion can block; keep it off the UI thread and never
        // let a scan exception escape an async void handler.
        BusyHint(true);
        try
        {
            _findings = await Task.Run(PolicyScanEngine.Scan);
            // One reset for the whole result set.
            _rows = _findings.Select(f => new PolicyRow(f)).ToList();
            FindingsList.ItemsSource = _rows;

            CountText.Text = TuneKit.Count(_findings.Count, "override found", "overrides found");
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Policy scan: {ex.Message}");
            CountText.Text = "Policy scan failed — see logs.";
        }
        finally
        {
            BusyHint(false);
        }
    }

    private void BusyHint(bool busy)
    {
        ScanBtn.IsEnabled = !busy;
        CountText.Text = busy ? "Scanning…" : CountText.Text;
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PolicyRow row } || XamlRoot is null)
        {
            return;
        }

        var friendly = PolicyScanEngine.FriendlyNameFor(row.Finding);
        var what = friendly is null ? $"'{row.Finding.ValueName}'" : $"'{friendly}' ({row.Finding.ValueName})";
        var confirm = new ContentDialog
        {
            Title = "Remove policy override",
            Content = $"Delete {what} from {row.Finding.Path}?\n\nThe old value is journaled — Settings → Revert all can restore it. Windows may recreate domain-pushed policies on its own.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var (ok, message) = await Task.Run(() =>
            PolicyScanEngine.Remove(row.Finding, App.Get<Optim.Core.Tweaks.RegistryTweakEngine>()));
        if (ok)
        {
            _rows = _rows.Where(r => !ReferenceEquals(r, row)).ToList();
            FindingsList.ItemsSource = _rows;
            CountText.Text = TuneKit.Count(_rows.Count, "override found", "overrides found");
        }

        Optim.App.Services.ToastService.Show(
            ok ? $"Removed {what}. {message}" : message,
            ok ? Optim.App.Services.ToastKind.Success : Optim.App.Services.ToastKind.Error);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_findings.Count == 0 || App.CurrentWindow is null || XamlRoot is null)
        {
            return;
        }

        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedFileName = "optim-policy-report",
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeChoices.Add("Text", new List<string> { ".txt" });
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            await Windows.Storage.FileIO.WriteTextAsync(file, PolicyScanEngine.ExportReport(_findings));
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Policy export: {ex.Message}");
            CountText.Text = "Export failed — see logs.";
        }
    }
}
