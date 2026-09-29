using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optim.Core.Startup;

namespace Optim.App.Views;

public sealed class StartupRow : INotifyPropertyChanged
{
    private bool _enabled;

    public StartupItem Item { get; init; }
    public string Name => Item.Name;
    public string Command => Item.Command;
    public string Location => Item.Location;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        }
    }

    /// <summary>Restores the switch to the last successfully applied state.</summary>
    public void Resnap(bool applied)
    {
        _enabled = applied;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
    }

    public StartupRow(StartupItem item)
    {
        Item = item;
        _enabled = item.Enabled;
    }

    public bool CanToggle => Item.CanToggle;
    public bool IsFolderItem => !Item.CanToggle;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class StartupPage : Page
{
    private List<StartupRow> _rows = new();
    private bool _populating;
    private bool _resnapping;
    private bool _loaded;
    private bool _loading;

    public StartupPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        StartupList.ItemContainerTransitions?.Clear();
        Loaded += (_, _) => Load();
    }

    private async void Load(bool force = false)
    {
        if ((_loaded && !force) || _loading)
        {
            return;
        }

        // ToggleSwitch.Toggled fires while rows are added; suppress so a page
        // load never rewrites StartupApproved flags.
        _populating = true;
        _loading = true;
        try
        {
            var items = await Task.Run(StartupEngine.List);
            // Single reset: build the rows first, then hand the list over.
            _rows = items.Select(i => new StartupRow(i)).ToList();
            StartupList.ItemsSource = _rows;
            _loaded = true;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Startup load: {ex.Message}");
        }
        finally
        {
            _loading = false;
            _populating = false;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load(force: true);

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        var nameBox = new TextBox { PlaceholderText = "Name, e.g. My Tool", Margin = new Thickness(0, 0, 0, 8) };
        var commandBox = new TextBox { PlaceholderText = @"Command, e.g. C:\Tools\tool.exe --minimized" };
        var form = new StackPanel();
        form.Children.Add(nameBox);
        form.Children.Add(commandBox);

        var dialog = new ContentDialog
        {
            Title = "Add startup app",
            Content = form,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (!StartupEngine.Add(nameBox.Text, commandBox.Text))
        {
            Optim.Core.Logging.FileLogger.Warn("Startup add rejected (empty name/command or write failed).");
        }

        Load(force: true);
    }

    private void EnableAll_Click(object sender, RoutedEventArgs e) => SetAll(true);

    private void DisableAll_Click(object sender, RoutedEventArgs e) => SetAll(false);

    private void SetAll(bool enabled)
    {
        _resnapping = true;
        try
        {
            foreach (var row in _rows)
            {
                // Folder items are not toggleable, so bulk actions leave them alone.
                if (!row.CanToggle)
                {
                    continue;
                }

                if (StartupEngine.SetEnabled(row.Name, enabled, row.Item.IsMachineWide))
                {
                    row.Resnap(enabled);
                }
            }
        }
        finally
        {
            _resnapping = false;
        }
    }

    private void Toggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_populating || _resnapping)
        {
            return;
        }

        if (sender is ToggleSwitch { DataContext: StartupRow row } && row.CanToggle)
        {
            // The binding already pushed the new value into row.Enabled.
            // Resnap the switch when the registry write fails so it never lies.
            var applied = StartupEngine.SetEnabled(row.Name, row.Enabled, row.Item.IsMachineWide);
            if (!applied)
            {
                _resnapping = true;
                try
                {
                    row.Resnap(!row.Enabled);
                }
                finally
                {
                    _resnapping = false;
                }
            }
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        _ = DeleteWithConfirmAsync(sender);
    }

    private async Task DeleteWithConfirmAsync(object sender)
    {
        if (sender is not Button { DataContext: StartupRow row } || XamlRoot is null)
        {
            return;
        }

        // RunOnce entries drive one-time installer behavior; refusing keeps
        // that intact and matches the disabled toggle. Folder shortcuts stay
        // deletable — Delete removes the file.
        if (row.Location.Contains("RunOnce", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await new ContentDialog
                {
                    Title = "Read-only entry",
                    Content = $"'{row.Name}' is a one-time entry that Windows deletes after the next boot, so Optim leaves it alone.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                }.ShowAsync();
            }
            catch (Exception ex)
            {
                Optim.Core.Logging.FileLogger.Error($"Startup readonly dialog: {ex.Message}");
            }
            return;
        }

        try
        {
            var isFolder = row.IsFolderItem;
            var confirm = new ContentDialog
            {
                Title = "Delete startup entry",
                Content = isFolder ? $"Delete the startup shortcut file '{row.Name}'?" : $"Remove '{row.Name}' from startup?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            {
                if (!StartupEngine.Remove(row.Name, row.Item.IsMachineWide, row.IsFolderItem, row.Item.Command))
                {
                    Optim.Core.Logging.FileLogger.Warn($"Startup delete reported failure for {row.Name}");
                }

                Load(force: true);
            }
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Startup delete: {ex.Message}");
        }
    }
}
