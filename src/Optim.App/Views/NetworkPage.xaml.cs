using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optim.Core.Network;

namespace Optim.App.Views;

public sealed partial class NetworkPage : Page
{
    private readonly NetworkEngine _engine = App.Get<NetworkEngine>();
    private ObservableCollection<NetworkAdapterInfo> _adapters = new();
    private bool _loaded;
    private bool _loading;

    public NetworkPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        AdapterList.ItemsSource = _adapters;
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
            _adapters.Clear();
            foreach (var a in adapters)
            {
                _adapters.Add(a);
            }

            AdapterBox.Items.Clear();
            foreach (var a in adapters)
            {
                AdapterBox.Items.Add(a.Name);
            }

            // Keep the user's adapter selected across refreshes.
            if (selected is not null && AdapterBox.Items.Contains(selected))
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

    private void Show(string message, InfoBarSeverity severity)
    {
        ResultInfo.Message = message;
        ResultInfo.Severity = severity;
        ResultInfo.IsOpen = true;
    }
}
