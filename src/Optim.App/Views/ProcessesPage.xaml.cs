using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optim.Core.Processes;

namespace Optim.App.Views;

public sealed record ProcessRow(ProcessEntry Entry)
{
    public string Name => Entry.Name;
    public string WindowTitle => Entry.WindowTitle ?? "";
    public int Pid => Entry.Id;
    public string RamText => Optim.Core.SystemInfo.SystemInfoEngine.FormatBytes(Entry.WorkingSetBytes);
    public string CpuText => Entry.CpuPercent <= 0 ? "—" : Entry.CpuPercent.ToString("0.0", CultureInfo) + " %";
    public string ThreadText => Entry.Threads > 0 ? Entry.Threads.ToString(CultureInfo) : "—";
    private static CultureInfo CultureInfo => System.Globalization.CultureInfo.InvariantCulture;
}

public sealed partial class ProcessesPage : Page
{
    private ObservableCollection<ProcessRow> _rows = new();
    private readonly List<ProcessRow> _allRows = new();
    private bool _loaded;
    private bool _loading;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _timer;

    public ProcessesPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        ProcessList.ItemsSource = _rows;
        ProcessList.ItemContainerTransitions?.Clear();
        Loaded += (_, _) =>
        {
            Load(force: false);
            // The toggle defaults to on; the timer must survive page caching.
            if (AutoRefreshToggle.IsChecked == true)
            {
                StartAutoRefresh();
            }
        };
        Unloaded += (_, _) => StopAutoRefresh();
    }

    private async void Load(bool force = false)
    {
        if ((_loaded && !force) || _loading)
        {
            return;
        }

        _loading = true;
        try
        {
            var items = await Task.Run(ProcessEngine.Snapshot);
            _allRows.Clear();
            _allRows.AddRange(items.Select(p => new ProcessRow(p)));
            await ApplyFilterAsync();
            _loaded = true;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Processes load: {ex.Message}");
            CountText.Text = "Could not load processes — see logs.";
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Filters by name/title/PID and refreshes the match count.</summary>
    private async Task ApplyFilterAsync()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        IEnumerable<ProcessRow> visible = _allRows;
        if (query.Length > 0)
        {
            visible = _allRows.Where(r =>
                r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.WindowTitle.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (int.TryParse(query, out var pid) && r.Pid == pid));
        }

        _rows.Clear();
        var added = 0;
        foreach (var row in visible)
        {
            _rows.Add(row);
            // Swapping hundreds of rows in one gulp stalls the UI; yield.
            if (++added % 60 == 0)
            {
                await Task.Yield();
            }
        }

        CountText.Text = query.Length == 0
            ? $"{_allRows.Count} processes"
            : $"{_rows.Count} of {_allRows.Count} processes match";
    }

    private void SearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _ = ApplyFilterAsync();
        }
    }

    private void AutoRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (AutoRefreshToggle.IsChecked == true)
        {
            StartAutoRefresh();
        }
        else
        {
            StopAutoRefresh();
        }
    }

    /// <summary>5 s cadence: long enough for CPU% deltas to be meaningful.</summary>
    private void StartAutoRefresh()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(5);
        _timer.Tick += (_, _) => Load(force: true);
        _timer.Start();
    }

    private void StopAutoRefresh()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load(force: true);

    private async void EndTask_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ProcessList.SelectedItem is not ProcessRow row)
            {
                return;
            }

            if (ProcessEngine.IsCritical(row.Pid, row.Name))
            {
                CountText.Text = $"'{row.Name}' is a critical system process and cannot be ended here.";
                return;
            }

            if (XamlRoot is null)
            {
                return;
            }

            var confirm = new ContentDialog
            {
                Title = "End task",
                Content = $"Kill '{row.Name}' (PID {row.Pid})?",
                PrimaryButtonText = "Kill",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var ok = await Task.Run(() => ProcessEngine.EndTask(row.Pid));
            if (!ok)
            {
                CountText.Text = $"Could not end '{row.Name}' — see logs.";
                return;
            }

            Load(force: true);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"EndTask_Click: {ex}");
            CountText.Text = "End task failed — see logs.";
        }
    }
}
