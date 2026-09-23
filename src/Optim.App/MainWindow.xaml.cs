using Microsoft.UI.Xaml;

namespace Optim.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "Optim";
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 720));

        // Apply the saved theme to the whole window (Settings only writes HKCU).
        try
        {
            var saved = App.AppSettings.GetString("Theme");
            if (!string.IsNullOrEmpty(saved)
                && Enum.TryParse<ElementTheme>(saved, out var theme)
                && Content is FrameworkElement root)
            {
                root.RequestedTheme = theme;
                ApplyCaptionButtons(theme);
            }
            else
            {
                ApplyCaptionButtons(ElementTheme.Default);
            }
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"MainWindow theme: {ex.Message}");
        }
    }

    /// <summary>Keeps the title-bar caption buttons readable on light/dark themes.</summary>
    public void ApplyCaptionButtons(ElementTheme theme)
    {
        try
        {
            var dark = theme == ElementTheme.Dark
                || (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
            var fg = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            var bar = AppWindow.TitleBar;
            bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonForegroundColor = fg;
            bar.ButtonHoverBackgroundColor = dark
                ? Windows.UI.Color.FromArgb(25, 255, 255, 255)
                : Windows.UI.Color.FromArgb(15, 0, 0, 0);
            bar.ButtonHoverForegroundColor = fg;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Caption buttons: {ex.Message}");
        }
    }
}
