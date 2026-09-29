using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Optim.App.Views;
using Optim.Core.Debloat;
using Optim.Core.Metrics;
using Optim.Core.Network;
using Optim.Core.Packages;
using Optim.Core.Policies;
using Optim.Core.Power;
using Optim.Core.Repair;
using Optim.Core.Security;
using Optim.Core.ServicesMgmt;
using Optim.Core.SystemInfo;
using Optim.Core.Tweaks;

namespace Optim.App;

/// <summary>Application entry point with a minimal DI container.</summary>
public partial class App : Application
{
    private IServiceProvider? _services;
    public static IServiceProvider Services =>
        ((App)Current)._services ?? throw new InvalidOperationException("App not initialized.");

    public static T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public App()
    {
        InitializeComponent();

        // Single instance: a second copy would fight the first over the
        // journal and the live system state. Bring the existing copy forward
        // is out of scope for a portable build; exiting with a toast logged
        // to the log file is the safe behaviour.
        if (!SingleInstanceGuard.TryAcquire())
        {
            Optim.Core.Logging.FileLogger.Warn("Another Optim instance is already running — exiting.");
            Environment.Exit(42);
        }

        _services = ConfigureServices();

        // Language override saved by Settings: Loc resolves strings through an
        // override resource context so code-resolved text picks it up. Empty
        // value = follow the system language.
        try
        {
            Optim.App.Localization.Loc.OverrideLanguage = AppSettings.GetString("Language");
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Language override: {ex.Message}");
        }

        UnhandledException += (_, e) =>
        {
            Optim.Core.Logging.FileLogger.Error($"UnhandledException: {e.Message}\n{e.Exception}");
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var argv = Environment.GetCommandLineArgs();
        for (var i = 0; i < argv.Length - 1; i++)
        {
            if (argv[i] is "--page" or "/page")
            {
                StartupPageTag = argv[i + 1];
                break;
            }
        }

        Optim.Core.Logging.FileLogger.Info($"Session start — {Environment.OSVersion}, elevated: {Environment.IsPrivilegedProcess}");
        try
        {
            CurrentWindow = new MainWindow();
            CurrentWindow.Activate();
            Optim.Core.Logging.FileLogger.Info("MainWindow activated");
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"OnLaunched failed: {ex}");
            throw;
        }
    }

    /// <summary>The main window, assigned before activation so pages can resolve it.</summary>
    public static MainWindow? CurrentWindow { get; private set; }

    /// <summary>
    /// Optional startup route ("--page optimize" style, tag only). Lets docs
    /// tooling open a specific page directly; unknown tags fall back to Home.
    /// </summary>
    public static string? StartupPageTag { get; private set; }

    /// <summary>
    /// Registry-backed per-user app settings. ApplicationData.Current throws for
    /// unpackaged WinUI apps (no package identity), so we persist to HKCU instead.
    /// </summary>
    public static class AppSettings
    {
        private const string KeyPath = "Software\\Optim";

        public static string? GetString(string name)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(KeyPath);
                return key?.GetValue(name) as string;
            }
            catch
            {
                return null;
            }
        }

        public static void SetString(string name, string value)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
                key.SetValue(name, value, Microsoft.Win32.RegistryValueKind.String);
            }
            catch (Exception ex)
            {
                Optim.Core.Logging.FileLogger.Warn($"AppSettings {name}: {ex.Message}");
            }
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Core engines
        services.AddSingleton<ChangeJournal>();
        services.AddSingleton<RegistryTweakEngine>();
        services.AddSingleton<MetricsEngine>();
        services.AddSingleton<DebloatEngine>();
        services.AddSingleton<ServiceEngine>();
        services.AddSingleton<NetworkEngine>();
        services.AddSingleton<PackageEngine>();
        services.AddSingleton<RepairEngine>();
        services.AddSingleton<PowerEngine>();

        return services.BuildServiceProvider();
    }
}

/// <summary>Process-wide single-instance guard for the Optim executable.</summary>
internal static class SingleInstanceGuard
{
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, "Global\\OptimApp-SingleInstance", out var createdNew);
            return createdNew;
        }
        catch
        {
            return false;
        }
    }
}
