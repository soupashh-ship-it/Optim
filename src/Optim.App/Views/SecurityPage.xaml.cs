using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Optim.Core.Security;

namespace Optim.App.Views;

public sealed record SecurityRow(SecurityItem Item)
{
    public string Name => Item.Name;
    public string State => Item.State;
    public string Detail => Item.Detail;
    public Brush StateBrush => new SolidColorBrush(Item.IsRecommended ? Colors.ForestGreen : Colors.OrangeRed);
}

public sealed partial class SecurityPage : Page
{
    private bool _populatingToggles;
    private ObservableCollection<SecurityRow> _rows = new();
    private bool _loaded;

    public SecurityPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        SecurityList.ItemsSource = _rows;
        SecurityList.ItemContainerTransitions?.Clear();
        Loaded += (_, _) => Load();
    }

    private void Load(bool force = false)
    {
        if (_loaded && !force)
        {
            return;
        }

        try
        {
            _rows.Clear();
            foreach (var item in SecurityPostureEngine.Read())
            {
                _rows.Add(new SecurityRow(item));
            }
            _loaded = true;
            PaintShield();
            SyncToggles();
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Security load: {ex.Message}");
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load(force: true);

    /// <summary>Hero summary: green when everything is in its recommended state.</summary>
    private void PaintShield()
    {
        try
        {
            var bad = _rows.Where(r => !r.Item.IsRecommended).ToList();
            var ok = bad.Count == 0;
            var color = ok ? Colors.ForestGreen : Colors.OrangeRed;
            ShieldRing.BorderBrush = new SolidColorBrush(color);
            ShieldGlyph.Foreground = new SolidColorBrush(color);
            ShieldGlyph.Glyph = ok ? "\uE73E" : "\uE783";
            ShieldState.Text = ok ? "Protected" : $"{TuneKit.Count(bad.Count, "item", "items")} need attention";
            ShieldDetail.Text = ok
                ? "Threat protection, real-time scanning, firewall and updates all report healthy."
                : string.Join(" · ", bad.Select(b => b.Name));
            ShieldStamp.Text = $"Last checked: {DateTime.Now:dd-MM-yyyy HH:mm:ss}";
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Security hero: {ex.Message}");
        }
    }

    private async void OpenDefender_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("windowsdefender:"));
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"OpenDefender: {ex.Message}");
            ActionStatus.Text = "Could not open Windows Security — see logs.";
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        ActionStatus.Text = "Quick scan running…";
        try
        {
            var mpCmd = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Windows Defender", "MpCmdRun.exe");
            var code = await Task.Run(() =>
            {
                try
                {
                    using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(mpCmd, "-Scan -ScanType 1")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    if (p is null)
                    {
                        return -1;
                    }
                    p.WaitForExit(1000 * 60 * 30);
                    return p.ExitCode;
                }
                catch (Exception ex)
                {
                    Optim.Core.Logging.FileLogger.Error($"MpCmdRun scan: {ex.Message}");
                    return -1;
                }
            });
            // MpCmdRun exit code 0 = clean; 2 = threats found/removed. Only
            // report "clean" on 0 so detections are never mislabeled.
            ActionStatus.Text = code switch
            {
                0 => "Quick scan finished clean.",
                2 => "Quick scan found threats — open Windows Security for details.",
                _ => $"Quick scan returned exit code {code} — see logs."
            };
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }

    /// <summary>Sets the toggle visuals from the posture rows without firing the handlers.</summary>
    private void SyncToggles()
    {
        if (_populatingToggles)
        {
            return;
        }

        _populatingToggles = true;
        try
        {
            bool? StateOf(string name, string contains)
            {
                var row = _rows.FirstOrDefault(r => r.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));
                var state = row?.State ?? "";
                if (state.Length == 0)
                {
                    return null;
                }

                return state.Contains(contains, StringComparison.OrdinalIgnoreCase)
                    ? true
                    : state.Contains("off", StringComparison.OrdinalIgnoreCase)
                        || state.Contains("Disabled", StringComparison.OrdinalIgnoreCase)
                        ? false
                        : null;
            }

            DefenderToggle.IsEnabled = true;
            DefenderToggle.IsOn = StateOf("Microsoft Defender", "Enabled") ?? false;
            RealtimeToggle.IsOn = StateOf("Real-time", "On") ?? false;
            SmartScreenToggle.IsOn = StateOf("SmartScreen", "On") ?? false;
            UacToggle.IsOn = StateOf("User Account Control", "On") ?? false;
        }
        finally
        {
            _populatingToggles = false;
        }
    }

    private void RunSecurityToggle(ToggleSwitch toggle, Func<bool, (bool Ok, string Detail)> action)
    {
        if (_populatingToggles)
        {
            return;
        }

        var wanted = toggle.IsOn;
        var (ok, detail) = action(wanted);
        if (ok)
        {
            ActionStatus.Text = detail;
            Load(force: true);
        }
        else
        {
            // The write failed: snap the switch back so it never lies.
            _populatingToggles = true;
            toggle.IsOn = !wanted;
            _populatingToggles = false;
            ActionStatus.Text = detail;
        }
    }

    private void DefenderToggle_Toggled(object sender, RoutedEventArgs e) =>
        RunSecurityToggle((ToggleSwitch)sender!, SecurityControlEngine.SetDefenderEnabled);

    private void RealtimeToggle_Toggled(object sender, RoutedEventArgs e) =>
        RunSecurityToggle((ToggleSwitch)sender!, SecurityControlEngine.SetRealtimeEnabled);

    private void SmartScreenToggle_Toggled(object sender, RoutedEventArgs e) =>
        RunSecurityToggle((ToggleSwitch)sender!, SecurityControlEngine.SetSmartScreenEnabled);

    private void UacToggle_Toggled(object sender, RoutedEventArgs e) =>
        RunSecurityToggle((ToggleSwitch)sender!, SecurityControlEngine.SetUacEnabled);

    private async void UpdateDefs_Click(object sender, RoutedEventArgs e)
    {
        ActionStatus.Text = "Updating definitions…";
        try
        {
            var mpCmd = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Windows Defender", "MpCmdRun.exe");
            var ok = await Task.Run(() =>
            {
                try
                {
                    using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(mpCmd, "-SignatureUpdate")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    if (p is null)
                    {
                        return false;
                    }
                    p.WaitForExit(1000 * 60 * 10);
                    return p.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    Optim.Core.Logging.FileLogger.Error($"MpCmdRun sigupdate: {ex.Message}");
                    return false;
                }
            });
            ActionStatus.Text = ok ? "Definitions updated." : "Definition update failed — see logs.";
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"UpdateDefs: {ex.Message}");
            ActionStatus.Text = "Definition update failed — see logs.";
        }
    }
}
