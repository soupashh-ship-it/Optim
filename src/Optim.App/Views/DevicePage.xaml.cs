using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optim.Core.SystemInfo;
using Windows.Storage.Pickers;

namespace Optim.App.Views;

public sealed partial class DevicePage : Page
{
    public DevicePage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        Loaded += async (_, _) => { if (!_loaded) await LoadAsync(); };
    }

    private async Task LoadAsync()
    {
        try
        {
            var inv = await Task.Run(SystemInfoEngine.Read);

            HostText.Text = $"{inv.HostName}";
            // Sum raw bytes first, then convert: per-stick flooring undercounts
            // odd capacities (e.g. 16.0 GB strings re-parsed as digits = 160).
            var totalBytes = inv.Memory.Sum(m => m.CapacityBytes);
            var ramGb = (long)Math.Round(totalBytes / 1073741824.0);
            var ramPart = ramGb > 0 ? $" | {ramGb} GB RAM" : "";
            SpecText.Text = $"{inv.Os.Caption} | {inv.Cpu.Name}{ramPart}";

            SectionsPanel.Children.Clear();
            AddSection("Operating System",
                $"OS Name: {inv.Os.Caption}{Environment.NewLine}" +
                $"Version: {inv.Os.Version}{Environment.NewLine}" +
                $"Build: {inv.Os.Build}{Environment.NewLine}" +
                $"Architecture: {inv.Os.Architecture}{Environment.NewLine}" +
                $"Installed: {inv.Os.InstallDate}");
            AddSection("Processor",
                $"Name: {inv.Cpu.Name}{Environment.NewLine}" +
                $"Cores: {inv.Cpu.Cores}  ({inv.Cpu.LogicalProcessors} threads){Environment.NewLine}" +
                $"Max clock: {inv.Cpu.MaxClockMhz}");
            if (inv.Gpus.Count > 0)
            {
                AddSection("Graphics", string.Join(Environment.NewLine,
                    inv.Gpus.Select(g => $"{g.Name}  (driver {g.DriverVersion})")));
            }
            if (inv.Memory.Count > 0)
            {
                AddSection("Memory", string.Join(Environment.NewLine,
                    inv.Memory.Select(m => $"{m.BankLabel}: {m.Capacity} @ {m.Speed}")));
            }
            if (inv.Disks.Count > 0)
            {
                AddSection("Storage", string.Join(Environment.NewLine,
                    inv.Disks.Select(d => $"{d.Model}  {SystemInfoEngine.FormatBytes(d.SizeBytes)}  ({d.Interface})")));
            }

            StatusText.Text = $"User: {inv.HostName}\\{inv.UserName}";
            _loaded = true;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Device load: {ex}");
            StatusText.Text = "Could not read the system inventory — see logs.";
        }
    }

    /// <summary>One collapsible section with a Copy button, like a spec sheet.</summary>
    private void AddSection(string title, string body)
    {
        var text = new TextBlock
        {
            Text = body,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(4, 8, 4, 4)
        };

        var copy = new Button { Content = "Copy" };
        copy.Click += (_, _) =>
        {
            try
            {
                var pack = new Windows.ApplicationModel.DataTransfer.DataPackage();
                pack.SetText(body);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pack);
                StatusText.Text = $"{title} copied to the clipboard.";
            }
            catch (Exception ex)
            {
                Optim.Core.Logging.FileLogger.Error($"Device copy: {ex.Message}");
                StatusText.Text = "Copy failed — see logs.";
            }
        };

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleBlock = new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(titleBlock, 0);
        Grid.SetColumn(copy, 1);
        header.Children.Add(titleBlock);
        header.Children.Add(copy);

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(text);

        var expander = new Expander
        {
            Header = title,
            Content = panel,
            IsExpanded = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        SectionsPanel.Children.Add(expander);
    }

    private bool _loaded;

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _loaded = false;
        await LoadAsync();
    }

    private async void ExportDrivers_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            // WinUI 3 desktop requires a non-empty filter or the picker
            // fails with E_INVALIDARG; "*.*" is the standard workaround.
            picker.FileTypeFilter.Add("*.*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            var psi = new System.Diagnostics.ProcessStartInfo("pnputil.exe",
                $"/export-driver * \"{folder.Path}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            await System.Diagnostics.Process.Start(psi)!.WaitForExitAsync();
            StatusText.Text = $"Drivers exported to {folder.Path}";
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"ExportDrivers: {ex}");
            StatusText.Text = "Driver export failed — see logs.";
        }
    }
}
