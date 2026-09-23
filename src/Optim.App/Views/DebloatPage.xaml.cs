using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optim.Core.Debloat;

namespace Optim.App.Views;

/// <summary>Row wrapper for a package checkbox binding.</summary>
public sealed class PackageRow : INotifyPropertyChanged
{
    private readonly AppPackage _pkg;
    private bool _isSelected;

    public PackageRow(AppPackage pkg) => _pkg = pkg;

    public string DisplayName => _pkg.DisplayName;
    public string PackageFamilyName => _pkg.PackageFamilyName;
    public bool IsProtected => _pkg.IsProtected;
    public string ProtectedVisibility => IsProtected ? "Visible" : "Collapsed";

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class DebloatPage : Page
{
    private readonly DebloatEngine _engine = App.Get<DebloatEngine>();
    private ObservableCollection<PackageRow> _rows = new();
    private List<PackageRow> _all = new();
    private bool _busy;

    public DebloatPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        PackagesList.ItemsSource = _rows;
        PackagesList.ItemContainerTransitions?.Clear();
        Loaded += async (_, _) =>
        {
            // Cached page: keep results between visits, reload when empty.
            if (_all.Count == 0)
            {
                await LoadAsync();
            }
        };
    }

    // PackageManager is UI-affinitized WinRT; enumerate on the UI thread.
    private async Task LoadAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        Busy.IsActive = true;
        // Let the spinner paint before the synchronous enumeration blocks us.
        await Task.Yield();
        try
        {
            var items = _engine.ListInstalled();
            _all.Clear();
            var added = 0;
            foreach (var p in items)
            {
                _all.Add(new PackageRow(p));
                if (++added % 25 == 0)
                {
                    await Task.Yield();
                }
            }
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Debloat load: {ex}");
        }
        finally
        {
            _busy = false;
            Busy.IsActive = false;
        }
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        _rows.Clear();
        foreach (var row in _all.Where(r =>
            string.IsNullOrEmpty(query)
            || r.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || r.PackageFamilyName.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            _rows.Add(row);
        }
        CountText.Text = _rows.Count == _all.Count
            ? $"{_all.Count} apps"
            : $"{_rows.Count} of {_all.Count} shown";
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var anyUnselected = _rows.Any(r => !r.IsSelected && !r.IsProtected);
        foreach (var r in _rows)
        {
            if (!r.IsProtected)
            {
                r.IsSelected = anyUnselected;
            }
        }
        SelectAllBtn.Content = anyUnselected ? "Deselect all" : "Select all";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(r => r.IsSelected && !r.IsProtected).Select(r => r.PackageFamilyName).ToList();
        if (selected.Count == 0 || XamlRoot is null)
        {
            return;
        }

        ContentDialogResult choice;
        try
        {
            var confirm = new ContentDialog
            {
                Title = "Uninstall packages",
                Content = $"Remove {TuneKit.Count(selected.Count, "package", "packages")} for the current user?",
                PrimaryButtonText = "Uninstall",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            choice = await confirm.ShowAsync();
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Uninstall confirm: {ex.Message}");
            return;
        }

        if (choice != ContentDialogResult.Primary)
        {
            return;
        }

        Busy.IsActive = true;
        try
        {
            var results = await _engine.UninstallAsync(selected);
            var failures = results.Count(r => !r.Value.StartsWith("Removed", StringComparison.Ordinal));
            var dialog = new ContentDialog
            {
                Title = "Finished",
                Content = failures == 0 ? "All selected packages were removed." : $"{TuneKit.Count(failures, "package", "packages")} could not be removed. See logs.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Uninstall: {ex.Message}");
        }
        finally
        {
            Busy.IsActive = false;
        }
    }
}
