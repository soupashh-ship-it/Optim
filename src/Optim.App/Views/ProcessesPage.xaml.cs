using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optim.Core.Processes;

namespace Optim.App.Views;

/// <summary>
/// One process row. Mutable on purpose: auto-refresh updates the entry in place
/// and raises change notifications, so a tick repaints the CPU/RAM text without
/// rebuilding the list (which would drop the user's selection every 5 seconds).
/// </summary>
public sealed class ProcessRow : INotifyPropertyChanged
{
    private ProcessEntry _entry;

    public ProcessRow(ProcessEntry entry) => _entry = entry;

    public ProcessEntry Entry => _entry;
    public string Name => _entry.Name;
    public int Pid => _entry.Id;
    public string WindowTitle => _entry.WindowTitle ?? "";
    public string RamText => Optim.Core.SystemInfo.SystemInfoEngine.FormatBytes(_entry.WorkingSetBytes);
    public string CpuText => _entry.CpuPercent <= 0 ? "—" : _entry.CpuPercent.ToString("0.0", CultureInfo) + " %";
    public string ThreadText => _entry.Threads > 0 ? _entry.Threads.ToString(CultureInfo) : "—";
    private static CultureInfo CultureInfo => System.Globalization.CultureInfo.InvariantCulture;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Points the row at a fresh sample of the same process.</summary>
    public void Update(ProcessEntry entry)
    {
        _entry = entry;
        Raise(nameof(WindowTitle));
        Raise(nameof(RamText));
        Raise(nameof(CpuText));
        Raise(nameof(ThreadText));
    }

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed partial class ProcessesPage : Page
{
    private List<ProcessRow> _rows = new();
    private readonly List<ProcessRow> _allRows = new();
    private bool _loaded;
    private bool _loading;
    private int? _selectedPid;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _timer;

    public ProcessesPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        ProcessList.ItemContainerTransitions?.Clear();
        ProcessList.SelectionChanged += (_, _) =>
        {
            _selectedPid = (ProcessList.SelectedItem as ProcessRow)?.Pid;
            var has = ProcessList.SelectedItem is ProcessRow;
            EndTaskBtn.IsEnabled = has;
            PriorityBtn.IsEnabled = has;
            AffinityBtn.IsEnabled = has;
        };
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

            // Reuse the row objects that are still alive and only push the new
            // numbers into them; rows for exited processes drop out and new
            // ones are created. Snapshot order is memory-descending, so most
            // refreshes keep the same sequence and skip the list reset
            // entirely — no container churn, no lost selection.
            var previous = _allRows.ToDictionary(r => r.Pid);
            _allRows.Clear();
            foreach (var entry in items)
            {
                if (previous.TryGetValue(entry.Id, out var row))
                {
                    row.Update(entry);
                }
                else
                {
                    row = new ProcessRow(entry);
                }
                _allRows.Add(row);
            }

            ApplyFilter();
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

    /// <summary>
    /// Filters by name/title/PID and refreshes the match count. Reassigns
    /// ItemsSource only when the visible sequence actually changes; otherwise
    /// the already-updated rows repaint through their own notifications.
    /// </summary>
    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var visible = query.Length == 0
            ? _allRows.ToList()
            : _allRows.Where(r =>
                r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.WindowTitle.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (int.TryParse(query, out var pid) && r.Pid == pid)).ToList();

        if (!SameSequence(_rows, visible))
        {
            _rows = visible;
            ProcessList.ItemsSource = _rows;

            // Keep the selected process selected across a real reshuffle.
            if (_selectedPid is int pid)
            {
                ProcessList.SelectedItem = _rows.FirstOrDefault(r => r.Pid == pid);
            }
        }

        CountText.Text = query.Length == 0
            ? $"{_allRows.Count} processes"
            : $"{_rows.Count} of {_allRows.Count} processes match";
    }

    private static bool SameSequence(List<ProcessRow> current, List<ProcessRow> next)
    {
        if (current.Count != next.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!ReferenceEquals(current[i], next[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Search-as-you-type: Enter keeps working for muscle memory.</summary>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
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

    private async void Priority_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ProcessList.SelectedItem is not ProcessRow row || XamlRoot is null)
            {
                return;
            }

            if (ProcessEngine.IsCritical(row.Pid, row.Name))
            {
                CountText.Text = $"'{row.Name}' is a critical system process; its priority cannot be changed here.";
                return;
            }

            var current = await Task.Run(() => ProcessEngine.GetPriorityLabel(row.Pid));
            var combo = new ComboBox
            {
                MinWidth = 220,
                DisplayMemberPath = "Label",
                ItemsSource = ProcessEngine.Priorities.Where(p => p.Value != 0x00000100).ToList(),
                PlaceholderText = "Pick a priority class"
            };
            if (current is not null)
            {
                combo.SelectedIndex = ProcessEngine.Priorities
                    .Where(p => p.Value != 0x00000100)
                    .ToList()
                    .FindIndex(p => p.Label == current);
            }

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock
            {
                Text = $"'{row.Name}' (PID {row.Pid}). Realtime is refused on purpose; High is enough for games.",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(combo);

            var confirm = new ContentDialog
            {
                Title = "Set priority",
                Content = panel,
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary || combo.SelectedItem is not ProcessEngine.PriorityChoice choice)
            {
                return;
            }

            var ok = await Task.Run(() => ProcessEngine.SetPriority(row.Pid, choice.Value));
            CountText.Text = ok
                ? $"Priority of '{row.Name}' set to {choice.Label}."
                : $"Could not set priority for '{row.Name}' — see logs.";
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Priority_Click: {ex}");
            CountText.Text = "Priority change failed — see logs.";
        }
    }

    private async void Affinity_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ProcessList.SelectedItem is not ProcessRow row || XamlRoot is null)
            {
                return;
            }

            if (ProcessEngine.IsCritical(row.Pid, row.Name))
            {
                CountText.Text = $"'{row.Name}' is a critical system process; its affinity cannot be changed here.";
                return;
            }

            var current = await Task.Run(() => ProcessEngine.GetAffinityMask(row.Pid));
            if (current is null)
            {
                CountText.Text = $"Could not read the affinity of '{row.Name}'.";
                return;
            }

            var cores = Environment.ProcessorCount;
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = $"'{row.Name}' (PID {row.Pid}) may run on the checked CPUs.",
                TextWrapping = TextWrapping.Wrap
            });
            var boxes = new List<CheckBox>();
            var grid = new Grid { ColumnSpacing = 8, RowSpacing = 4 };
            for (var i = 0; i < cores; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            for (var row_ = 0; row_ * 8 < cores; row_++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (var col = 0; col < 8 && row_ * 8 + col < cores; col++)
                {
                    var cpu = row_ * 8 + col;
                    var cb = new CheckBox
                    {
                        Content = $"CPU {cpu}",
                        IsChecked = (current.Value & (1L << cpu)) != 0
                    };
                    Grid.SetRow(cb, row_);
                    Grid.SetColumn(cb, col);
                    grid.Children.Add(cb);
                    boxes.Add(cb);
                }
            }

            panel.Children.Add(grid);
            var warn = new TextBlock
            {
                Text = "At least one CPU must stay checked.",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Opacity = 0.7
            };
            panel.Children.Add(warn);

            var confirm = new ContentDialog
            {
                Title = "CPU affinity",
                Content = panel,
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            long mask = 0;
            foreach (var (cb, cpu) in boxes.Select((b, i) => (b, i)))
            {
                if (cb.IsChecked == true)
                {
                    mask |= 1L << cpu;
                }
            }

            if (mask == 0)
            {
                CountText.Text = "No CPU selected — affinity unchanged.";
                return;
            }

            var ok = await Task.Run(() => ProcessEngine.SetAffinityMask(row.Pid, mask));
            CountText.Text = ok
                ? $"Affinity of '{row.Name}' updated."
                : $"Could not set affinity for '{row.Name}' — see logs.";
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Affinity_Click: {ex}");
            CountText.Text = "Affinity change failed — see logs.";
        }
    }

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
