using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optim.Core.Packages;

namespace Optim.App.Views;

public sealed class WingetRow : INotifyPropertyChanged
{
    private readonly WingetPackage _pkg;
    private bool _isSelected;

    public WingetRow(WingetPackage pkg) => _pkg = pkg;

    public string Id => _pkg.Id;
    public string Name => _pkg.Name;
    public string InstalledVersion => _pkg.InstalledVersion;
    public string AvailableVersion => _pkg.AvailableVersion;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class PackagesPage : Page
{
    private readonly PackageEngine _engine = App.Get<PackageEngine>();
    private ObservableCollection<WingetRow> _rows = new();
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _opCts;

    public PackagesPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        PackageList.ItemsSource = _rows;
        PackageList.ItemContainerTransitions?.Clear();
        Unloaded += (_, _) =>
        {
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
            _cts = null;
            // Cancel a running install/upgrade loop when the page goes away.
            try { _opCts?.Cancel(); } catch { }
        };
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (!PackageEngine.IsWingetAvailable())
        {
            Show("winget CLI not found. Install App Installer from the Store.", InfoBarSeverity.Warning);
            return;
        }

        Busy.IsActive = true;
        _rows.Clear();
        StatusText.Text = "Scanning…";
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            var packages = await _engine.ListUpgradesAsync(token);
            foreach (var p in packages)
            {
                _rows.Add(new WingetRow(p));
            }
            StatusText.Text = TuneKit.Count(packages.Count, "upgrade", "upgrades") + " available";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Winget scan: {ex}");
            StatusText.Text = "Scan failed — see logs.";
            Show($"Upgrade scan failed: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            Busy.IsActive = false;
        }
    }

    private async void Upgrade_Click(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(r => r.IsSelected).Select(r => r.Id).ToList();
        if (selected.Count == 0 || XamlRoot is null)
        {
            return;
        }

        ContentDialogResult choice;
        try
        {
            var confirm = new ContentDialog
            {
                Title = "Upgrade packages",
                Content = $"Upgrade {TuneKit.Count(selected.Count, "package", "packages")}? Each upgrade runs the vendor installer.",
                PrimaryButtonText = "Upgrade",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            choice = await confirm.ShowAsync();
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Upgrade confirm: {ex.Message}");
            return;
        }

        if (choice != ContentDialogResult.Primary)
        {
            return;
        }

        Busy.IsActive = true;
        using var opCts = _opCts = new CancellationTokenSource();
        var ok = 0;
        try
        {
            foreach (var id in selected)
            {
                if (await _engine.UpgradeAsync(id, _ => { }, opCts.Token))
                {
                    ok++;
                }
            }

            Show($"{ok}/{selected.Count} completed.", ok == selected.Count ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Winget upgrade: {ex}");
            Show($"Upgrade failed: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            Busy.IsActive = false;
            Scan_Click(sender, e);
        }
    }

    private void Show(string message, InfoBarSeverity severity)
    {
        ResultInfo.Message = message;
        ResultInfo.Severity = severity;
        ResultInfo.IsOpen = true;
    }

    private void SearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            Search_Click(sender, new RoutedEventArgs());
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            return;
        }

        if (!PackageEngine.IsWingetAvailable())
        {
            Show("winget CLI not found. Install App Installer from the Store.", InfoBarSeverity.Warning);
            return;
        }

        Busy.IsActive = true;
        _rows.Clear();
        StatusText.Text = $"Searching for '{query}'…";
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            var results = await _engine.SearchAsync(query, _cts.Token);
            foreach (var r in results)
            {
                // InstalledVersion stays empty; AvailableVersion shows the
                // version that would be installed.
                _rows.Add(new WingetRow(new WingetPackage(r.Id, r.Name, "", r.Version)));
            }
            StatusText.Text = $"{TuneKit.Count(results.Count, "result", "results")} for '{query}'";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Search cancelled.";
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Winget search: {ex}");
            StatusText.Text = "Search failed — see logs.";
        }
        finally
        {
            Busy.IsActive = false;
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(r => r.IsSelected).Select(r => r.Id).ToList();
        if (selected.Count == 0 || XamlRoot is null)
        {
            return;
        }

        ContentDialogResult choice;
        try
        {
            choice = await new ContentDialog
            {
                Title = "Install packages",
                Content = $"Install {TuneKit.Count(selected.Count, "package", "packages")}? Each install runs the vendor installer.",
                PrimaryButtonText = "Install",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Install confirm: {ex.Message}");
            return;
        }

        if (choice != ContentDialogResult.Primary)
        {
            return;
        }

        Busy.IsActive = true;
        using var opCts = _opCts = new CancellationTokenSource();
        var ok = 0;
        try
        {
            foreach (var id in selected)
            {
                if (await _engine.InstallAsync(id, _ => { }, opCts.Token))
                {
                    ok++;
                }
            }

            Show($"{ok}/{selected.Count} installs completed.", ok == selected.Count ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Winget install: {ex}");
            Show($"Install failed: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            Busy.IsActive = false;
        }
    }
}
