using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optim.Core.Network;

namespace Optim.App.Views;

public sealed partial class NetworkPage : Page
{
    private readonly NetworkEngine _engine = App.Get<NetworkEngine>();
    private bool _loaded;
    private bool _loading;

    public NetworkPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        // Rows are static once loaded; the platform's staggered entrance
        // animation on a machine with dozens of adapters only costs frames.
        AdapterList.ItemContainerTransitions?.Clear();
        DnsBox.ItemsSource = NetworkEngine.KnownProfiles;
        Loaded += (_, _) => RefreshAdapters();
    }

    private async void RefreshAdapters(bool force = false)
    {
        if ((_loaded && !force) || _loading)
        {
            return;
        }

        _loading = true;
        var selected = AdapterBox.SelectedItem as string;
        try
        {
            var adapters = await Task.Run(NetworkEngine.ListAdapters);

            // One reset per collection instead of a clear + N adds: the list
            // and the combo each rebuild their containers in a single pass.
            AdapterList.ItemsSource = adapters;
            AdapterBox.ItemsSource = adapters.Select(a => a.Name).ToList();

            // Keep the user's adapter selected across refreshes.
            if (selected is not null && adapters.Any(a => a.Name == selected))
            {
                AdapterBox.SelectedItem = selected;
            }

            _loaded = true;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Adapters refresh: {ex.Message}");
            Show("Could not list adapters — see logs.", InfoBarSeverity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAdapters(force: true);

    private async void ApplyDns_Click(object sender, RoutedEventArgs e)
    {
        if (AdapterBox.SelectedItem is not string adapter || DnsBox.SelectedItem is not DnsProfile profile)
        {
            Show("Pick an adapter and a DNS profile first.", InfoBarSeverity.Warning);
            return;
        }

        if (XamlRoot is null)
        {
            return;
        }

        try
        {
            var confirm = new ContentDialog
            {
                Title = "Change DNS servers",
                Content = $"Point '{adapter}' at {profile.Name} ({profile.Primary}, {profile.Secondary})? This changes how this PC reaches the network.",
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var result = await _engine.ApplyDnsAsync(adapter, profile);
            Show(result.V4 ? $"DNS for '{adapter}' set to {profile.Name} ({profile.Primary}). {result.Detail}" : $"Failed to apply DNS. {result.Detail}",
                result.V4 ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            RefreshAdapters(force: true);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"ApplyDns: {ex.Message}");
            Show($"Failed to apply DNS: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void ResetDns_Click(object sender, RoutedEventArgs e)
    {
        if (AdapterBox.SelectedItem is not string adapter)
        {
            Show("Pick an adapter first.", InfoBarSeverity.Warning);
            return;
        }

        if (XamlRoot is null)
        {
            return;
        }

        try
        {
            var confirm = new ContentDialog
            {
                Title = "Reset DNS servers",
                Content = $"Return '{adapter}' to automatic (DHCP) DNS?",
                PrimaryButtonText = "Reset",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var ok = await _engine.ResetDnsAsync(adapter);
            Show(ok ? $"DNS for '{adapter}' reset to automatic." : "Failed to reset DNS. See logs.",
                ok ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            RefreshAdapters(force: true);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"ResetDns: {ex.Message}");
            Show($"Failed to reset DNS: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void Flush_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var code = await NetworkEngine.FlushDnsAsync();
            Show(code == 0 ? "DNS resolver cache flushed." : $"DNS flush returned exit code {code} — run elevated and check logs.",
                code == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"FlushDns: {ex.Message}");
            Show($"Could not flush DNS: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void ApplyCustomDns_Click(object sender, RoutedEventArgs e)
    {
        if (AdapterBox.SelectedItem is not string adapter)
        {
            Show("Pick an adapter first.", InfoBarSeverity.Warning);
            return;
        }

        if (XamlRoot is null)
        {
            return;
        }

        var (primary, secondary, error) = NetworkEngine.ParseCustomServers(
            CustomPrimaryBox.Text, CustomSecondaryBox.Text);
        if (error is not null)
        {
            Show(error, InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var confirm = new ContentDialog
            {
                Title = "Apply custom DNS servers",
                Content = $"Point '{adapter}' at {primary}" + (secondary is null ? "" : $" and {secondary}") + "?",
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var result = await _engine.ApplyCustomDnsAsync(adapter, primary!, secondary);
            Show(result.V4 ? $"DNS for '{adapter}' set. {result.Detail}" : $"Failed to apply custom DNS. {result.Detail}",
                result.V4 ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            RefreshAdapters(force: true);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"ApplyCustomDns: {ex.Message}");
            Show($"Failed to apply custom DNS: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void ToggleAdapter_Click(object sender, RoutedEventArgs e)
    {
        if (AdapterBox.SelectedItem is not string adapter)
        {
            Show("Pick an adapter first.", InfoBarSeverity.Warning);
            return;
        }

        if (XamlRoot is null)
        {
            return;
        }

        var disable = ToggleAdapterBtn.Content as string == "Disable adapter";
        var verb = disable ? "disable" : "re-enable";
        try
        {
            var confirm = new ContentDialog
            {
                Title = $"{char.ToUpperInvariant(verb[0])}{verb[1..]} adapter",
                Content = disable
                    ? $"Disable '{adapter}' now? You will lose connectivity through it until it is re-enabled (from Optim or Device Manager)."
                    : $"Re-enable '{adapter}' now?",
                PrimaryButtonText = char.ToUpperInvariant(verb[0]) + verb[1..],
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var ok = await NetworkEngine.SetAdapterEnabledAsync(adapter, !disable);
            Show(ok
                ? $"Adapter {(disable ? "disabled" : "enabled")}: '{adapter}'."
                : $"Could not {verb} '{adapter}' — run elevated and check logs.",
                ok ? InfoBarSeverity.Success : InfoBarSeverity.Error);

            if (disable)
            {
                // A disabled adapter drops off the enumeration; reflect that
                // immediately instead of leaving a stale selection.
                ToggleAdapterBtn.Content = "Enable adapter";
            }
            RefreshAdapters(force: true);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"ToggleAdapter: {ex.Message}");
            Show($"Failed to {verb} the adapter: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void Show(string message, InfoBarSeverity severity)
    {
        ResultInfo.Message = message;
        ResultInfo.Severity = severity;
        ResultInfo.IsOpen = true;
    }
}
