using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Optim.App.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            VersionText.Text = $"Version {version} — MIT licensed, 100% original code.";
            EnvText.Text = $"{Environment.OSVersion} — {(Environment.IsPrivilegedProcess ? "elevated" : "not elevated")}";
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"About load: {ex.Message}");
        }
    }

    private async void RestorePoint_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        try
        {
            var confirm = new ContentDialog
            {
                Title = "Create restore point",
                Content = "Create a system restore point now? This can take a minute.",
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            // Same helper as Settings/Home: one code path, one success definition.
            var ok = await Task.Run(() => Optim.Core.SystemDeployment.RestorePoint.TryCreate("Optim restore point"));
            Show(ok ? "Restore point created."
                    : "Restore point creation failed — enable System Protection for this drive, then run elevated.",
                ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"About restore point: {ex.Message}");
            Show("Restore point creation failed — see logs.", InfoBarSeverity.Error);
        }
    }

    private async void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Optim.Core.Logging.FileLogger.LogDirectory);
            await Windows.System.Launcher.LaunchFolderAsync(folder);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"About open logs: {ex.Message}");
            Show($"Could not open the logs folder: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void Show(string message, InfoBarSeverity severity)
    {
        ResultInfo.Message = message;
        ResultInfo.Severity = severity;
        ResultInfo.IsOpen = true;
    }
}
