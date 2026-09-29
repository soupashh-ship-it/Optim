using System.ComponentModel;
using System.ServiceProcess;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optim.Core.ServicesMgmt;

namespace Optim.App.Views;

public sealed class ServiceRow : INotifyPropertyChanged
{
    private readonly ServiceEngine _engine;
    private int _startModeIndex;
    private bool _initializing = true;

    public ServiceRow(ServiceEntry entry, ServiceEngine engine)
    {
        Entry = entry;
        _engine = engine;
        _startModeIndex = entry.StartMode switch
        {
            ServiceStartMode.Automatic => entry.IsDelayedAuto ? 3 : 0,
            ServiceStartMode.Manual => 1,
            ServiceStartMode.Disabled => 2,
            _ => 1
        };
        AppliedIndex = _startModeIndex;
        _initializing = false;
    }

    public ServiceEntry Entry { get; }
    public string Name => Entry.Name;
    public string DisplayName => Entry.DisplayName;
    public string Description => Entry.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Entry.Description);
    public string StatusText => Entry.IsRunning ? "Running" : "Stopped";

    /// <summary>True when the service binary lives in the Windows directory (Task Manager's Microsoft-services signal).</summary>
    public bool IsMicrosoft => Entry.IsMicrosoft;

    /// <summary>False for boot-critical services: combo and buttons render disabled.</summary>
    public bool CanModify => Entry.CanModify;

    /// <summary>False when the start type could not be read: the combo is disabled.</summary>
    public bool CanChange => Entry.CanModify && Entry.StartModeKnown;

    public string StartModeText => Entry.StartModeKnown
        ? Entry.StartMode == ServiceStartMode.Automatic && Entry.IsDelayedAuto
            ? "Automatic (delayed)"
            : Entry.StartMode.ToString()
        : "Unknown";

    /// <summary>Badge shown only for the boot-critical refuse-list. Typed as
    /// Visibility so the compiled binding in the row template needs no converter.</summary>
    public Microsoft.UI.Xaml.Visibility CriticalVisibility =>
        Entry.IsCritical ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>Start is offered to controllable services that are not running.</summary>
    public bool CanStart => Entry.CanModify && !Entry.IsRunning;

    /// <summary>Stop/Restart are offered to controllable running services.</summary>
    public bool CanStop => Entry.CanModify && Entry.IsRunning;

    public int StartModeIndex
    {
        get => _startModeIndex;
        set
        {
            // Binding pushes only: the SelectionChanged handler decides whether
            // to apply (with confirmation). Never call the engine from here, or
            // the confirm dialog gets bypassed.
            if (_initializing || value == _startModeIndex || value < 0 || value > 3)
            {
                return;
            }

            _startModeIndex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartModeIndex)));
        }
    }

    /// <summary>Last successfully applied index. The UI resnaps to this on cancel/failure.</summary>
    public int AppliedIndex { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool TryApply(int index)
    {
        if (_initializing || index < 0 || index > 3)
        {
            return false;
        }

        var (mode, delayed) = index switch
        {
            0 => (ServiceStartMode.Automatic, false),
            1 => (ServiceStartMode.Manual, false),
            2 => (ServiceStartMode.Disabled, false),
            _ => (ServiceStartMode.Automatic, true)
        };

        try
        {
            if (_engine.SetStartType(Entry.Name, mode, delayed))
            {
                _startModeIndex = index;
                AppliedIndex = index;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartModeIndex)));
                return true;
            }
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Service {Entry.Name}: {ex.Message}");
        }

        return false;
    }

    /// <summary>Snaps the ComboBox back to the last applied value.</summary>
    public void Resnap()
    {
        _startModeIndex = AppliedIndex;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartModeIndex)));
    }
}

public sealed partial class ServicesPage : Page
{
    private readonly ServiceEngine _engine = App.Get<ServiceEngine>();
    private List<ServiceRow> _rows = new();
    private readonly List<ServiceRow> _allRows = new();

    /// <summary>Waits out a burst of typing before rebuilding the filtered list.</summary>
    private readonly Optim.App.Services.Debouncer _filter = new(
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(),
        TimeSpan.FromMilliseconds(120));

    public ServicesPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        // Staggered entrance animations on 200+ rows feel heavy; rows just appear.
        ServicesList.ItemContainerTransitions?.Clear();

        // Handled-events-too: the wheel is watched here whether or not a row's
        // drop-down consumed it, because that is exactly the case that used to
        // raise a confirm dialog.
        ServicesList.AddHandler(
            Microsoft.UI.Xaml.UIElement.PointerWheelChangedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(List_PointerWheelChanged),
            handledEventsToo: true);

        Loaded += (_, _) => Load();
    }

    /// <summary>
    /// True while the mouse wheel is turning over the list. A drop-down that
    /// changes while the user is scrolling is not a decision: the pointer simply
    /// travelled over the cell on its way down the list, and acting on it asked
    /// people to confirm disabling services they never touched. The window is
    /// short so it cannot swallow a deliberate pick, which does not involve the
    /// wheel at all.
    /// </summary>
    private bool IsScrolling => Environment.TickCount64 < _scrollingUntil;

    private long _scrollingUntil;

    private void List_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _scrollingUntil = Environment.TickCount64 + 250;
    }

    /// <summary>Set while the code below repaints a drop-down, so its own
    /// SelectionChanged does not get treated as user input.</summary>
    private bool _syncing;

    /// <summary>Repaints a row's drop-down from the value that is actually applied.</summary>
    private void SyncComboBox(ComboBox combo, int appliedIndex)
    {
        if (combo.SelectedIndex == appliedIndex)
        {
            return;
        }

        _syncing = true;
        try
        {
            combo.SelectedIndex = appliedIndex;
        }
        finally
        {
            _syncing = false;
        }
    }

    // ServiceController is plain .NET, safe on a background thread.
    private async Task LoadAsync()
    {
        try
        {
            var entries = await Task.Run(_engine.List);
            _allRows.Clear();
            _allRows.AddRange(entries.Select(s => new ServiceRow(s, _engine)));
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Services load: {ex.Message}");
            CountText.Text = "Could not load services — see logs.";
        }
    }

    /// <summary>
    /// Filters by name/display/description and shows running counts. Assigns a
    /// fresh list to ItemsSource rather than clearing and re-adding rows one at
    /// a time, so typing in the search box costs one reset instead of hundreds
    /// of collection-change notifications.
    /// </summary>
    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        IEnumerable<ServiceRow> visible = _hideMicrosoft
            ? _allRows.Where(r => !r.IsMicrosoft)
            : _allRows;
        if (query.Length > 0)
        {
            visible = _allRows.Where(r =>
                r.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (r.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // Reset any pending combo edit before the list rebinds.
        foreach (var row in _rows.Where(r => r.StartModeIndex != r.AppliedIndex).ToList())
        {
            row.Resnap();
        }

        _rows = visible.ToList();
        ServicesList.ItemsSource = _rows;

        var running = _allRows.Count(r => r.Entry.IsRunning);
        var suffix = _hideMicrosoft ? " · Microsoft services hidden" : "";
        CountText.Text = query.Length == 0
            ? $"{_allRows.Count} services · {running} running{suffix}"
            : $"{_rows.Count} of {_allRows.Count} services match · {running} running{suffix}";
    }

    private bool _hideMicrosoft;

    private bool _populating;
    private bool _changing;
    private bool _loaded;
    private bool _loading;

    private async void Load(bool force = false)
    {
        // Cached page: load once, Refresh forces. Never overlap two loads.
        if ((_loaded && !force) || _loading)
        {
            return;
        }

        // ComboBox SelectionChanged fires while rows are added; suppress so a
        // page load never rewrites service start types.
        _populating = true;
        _loading = true;
        try
        {
            await LoadAsync();
            _loaded = true;
        }
        finally
        {
            _loading = false;
            _populating = false;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load(force: true);

    /// <summary>Task Manager-style filter: drop services whose binary lives in the Windows directory.</summary>
    private void HideMicrosoft_Click(object sender, RoutedEventArgs e)
    {
        _hideMicrosoft = HideMicrosoftToggle.IsChecked == true;
        ApplyFilter();
    }

    private async void Mode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_populating || _syncing)
            {
                return;
            }

            if (sender is not ComboBox { SelectedIndex: >= 0 } cb || cb.DataContext is not ServiceRow row)
            {
                return;
            }

            var index = cb.SelectedIndex;
            if (index == row.AppliedIndex)
            {
                return;
            }

            // Scrolling must never change a service. Revert the cell and behave as
            // if the change never happened; a real edit comes from opening the
            // drop-down or from the keyboard, neither of which touches the wheel.
            if (IsScrolling)
            {
                SyncComboBox(cb, row.AppliedIndex);
                return;
            }

            // Boot-critical and unknown-mode services render read-only (controls
            // disabled): the engine only ever changes controllable services.
            if (!row.CanChange)
            {
                row.Resnap();
                return;
            }

            // A change is already being confirmed/applied: snap this one back
            // instead of stacking dialogs and losing track of state.
            if (_changing)
            {
                row.Resnap();
                return;
            }

            _changing = true;
            try
            {
                var (ok, message) = await ConfirmAndApplyAsync(row, index);
                if (!ok)
                {
                    row.Resnap();
                    if (message is not null)
                    {
                        Optim.App.Services.ToastService.Show(message, Optim.App.Services.ToastKind.Error);
                    }
                }
            }
            finally
            {
                _changing = false;
            }
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Service mode change: {ex.Message}");
        }
    }

    /// <summary>
    /// Confirms a start-type change and applies it. Disabled is destructive
    /// enough to always warrant a dialog; the other modes apply directly so
    /// the page stays fast. Returns (ok, toast message on failure).
    /// </summary>
    private async Task<(bool Ok, string? Error)> ConfirmAndApplyAsync(ServiceRow row, int index)
    {
        if (index == 2 && XamlRoot is not null)
        {
            var confirm = new ContentDialog
            {
                Title = "Disable service",
                Content = $"Disable '{row.DisplayName}' ({row.Name})? Windows and apps will not be able to start it.",
                PrimaryButtonText = "Disable",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return (false, null);
            }
        }

        if (row.TryApply(index))
        {
            return (true, null);
        }

        return (false, $"Could not change '{row.DisplayName}' — see logs.");
    }

    /// <summary>
    /// Search-as-you-type, settled after the typing pauses: filtering 200+ rows on
    /// every keystroke meant each one threw away the list reset the previous one
    /// had just queued. Enter still applies immediately for muscle memory.
    /// </summary>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filter.Run(ApplyFilter);
    }

    private void SearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _filter.Flush();
        }
    }

    private async Task<bool> ControlSelectedAsync(object sender, string verb)
    {
        if (sender is not Button { DataContext: ServiceRow row } || XamlRoot is null)
        {
            return false;
        }

        // Belt and braces: buttons are bound-disabled for boot-critical services,
        // but never control a service the engine would refuse.
        if (!row.CanModify)
        {
            return false;
        }

        try
        {
            var confirm = new ContentDialog
            {
                Title = $"{verb} service",
                Content = $"{verb} '{row.DisplayName}' ({row.Name}) now?",
                PrimaryButtonText = verb,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return false;
            }

            var ok = verb switch
            {
                "Start" => await _engine.StartAsync(row.Name),
                "Stop" => await _engine.StopAsync(row.Name),
                _ => await _engine.RestartAsync(row.Name)
            };
            if (!ok)
            {
                Optim.App.Services.ToastService.Show(
                    $"Could not {verb.ToLowerInvariant()} '{row.DisplayName}' — see logs.",
                    Optim.App.Services.ToastKind.Error);
            }

            // Re-read live state; the wait may have timed out even on success.
            // Load is async void by design (same as Refresh_Click).
            Load(force: true);
            return ok;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Service {verb}: {ex.Message}");
            return false;
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e) => _ = ControlSelectedAsync(sender, "Start");

    private void Stop_Click(object sender, RoutedEventArgs e) => _ = ControlSelectedAsync(sender, "Stop");

    private void Restart_Click(object sender, RoutedEventArgs e) => _ = ControlSelectedAsync(sender, "Restart");
}
