using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optim.Core.Repair;

namespace Optim.App.Views;

public sealed partial class RepairPage : Page
{
    private readonly RepairEngine _engine = App.Get<RepairEngine>();
    private CancellationTokenSource? _cts;

    public RepairPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        ActionBox.ItemsSource = RepairEngine.Catalog;
        DriveBox.ItemsSource = RepairEngine.FixedDriveNames();
    }

    private void ActionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActionBox.SelectedItem is not RepairAction action)
        {
            ActionHint.Visibility = Visibility.Collapsed;
            DriveBox.Visibility = Visibility.Collapsed;
            return;
        }

        // Drive-selecting actions (chkdsk) need a target drive.
        var needsDrive = action.Arguments.Contains("{drive}");
        DriveBox.Visibility = needsDrive ? Visibility.Visible : Visibility.Collapsed;
        if (needsDrive && DriveBox.SelectedIndex < 0 && DriveBox.Items.Count > 0)
        {
            DriveBox.SelectedIndex = 0;
        }

        var kindText = action.Kind switch
        {
            RepairKind.Check => "Check (read-only)",
            RepairKind.Repair => "Repair (changes the system)",
            _ => "Tool"
        };
        var pairedNote = action.Kind == RepairKind.Repair && action.PairedCheckIds is { Length: > 0 }
            ? $"\n\nTip: run a check first — {string.Join(" or ", action.PairedCheckIds)}."
            : "";
        ActionHint.Text = $"{kindText}. {action.Description}{pairedNote}";
        ActionHint.Visibility = Visibility.Visible;

        // Surface when this action last finished successfully (0 exit code).
        var last = App.AppSettings.GetString("repair-last-" + action.Id);
        ActionHint.Text += last is null
            ? "\n\nNot run successfully yet."
            : $"\n\nLast successful run: {last}";
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (ActionBox.SelectedItem is not RepairAction action)
        {
            return;
        }

        if (XamlRoot is null)
        {
            return;
        }

        // Fill drive placeholders up front so the dialog shows the real command.
        var resolvedAction = _engine.ResolvePlaceholders(action, DriveBox.SelectedItem as string ?? DriveBox.Text);
        if (resolvedAction is null)
        {
            Optim.Core.Logging.FileLogger.Error($"Repair '{action.Id}' skipped: no drive selected.");
            return;
        }

        var confirm = new ContentDialog
        {
            Title = action.Title,
            Content = action.Description + "\n\nThis runs: " + action.FileName + " " + action.Arguments,
            PrimaryButtonText = "Run",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        OutputText.Text = string.Empty;
        Busy.IsActive = true;
        CancelBtn.IsEnabled = true;
        RunBtn.IsEnabled = false;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            var code = await _engine.RunAsync(resolvedAction, line =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    OutputText.Text += line + Environment.NewLine;
                });
            }, _cts.Token);

            DispatcherQueue.TryEnqueue(() =>
            {
                OutputText.Text += $"\n[exit code {code}]";
                if (code == 0)
                {
                    App.AppSettings.SetString("repair-last-" + action.Id, DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                    ActionHint.Text = ActionHint.Text.Replace(
                        "Not run successfully yet.",
                        "Last successful run: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                }
            });
        }
        catch (OperationCanceledException)
        {
            DispatcherQueue.TryEnqueue(() => OutputText.Text += "\n[cancelled]");
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Repair run: {ex.Message}");
            DispatcherQueue.TryEnqueue(() => OutputText.Text += $"\n[error] {ex.Message}");
        }
        finally
        {
            Busy.IsActive = false;
            CancelBtn.IsEnabled = false;
            RunBtn.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }
}
