using static Optim.Core.Tweaks.RegistryOperation;

namespace Optim.Core.Tweaks;

/// <summary>
/// The single source of truth for every registry-backed tweak. Pages render
/// from this catalog; adding a tweak is adding one entry here.
/// Definitions written from public Windows documentation.
/// </summary>
public static class TweakCatalog
{
    /// <summary>
    /// System-level tweaks hidden unless the "Advanced" box is checked.
    /// Basic tweaks stay visible; these need more care to reverse.
    /// Declared before All: static fields initialize in order.
    /// </summary>
    private static readonly HashSet<string> AdvancedIds = new(StringComparer.Ordinal)
    {
        "optimize.network-throttle", "optimize.systemresponsiveness",
        "optimize.gaming-priority", "optimize.search-indexing",
        "optimize.8dot3-disable", "optimize.hags",
        "optimize.power-throttling-off", "optimize.delivery-p2p-disable",
        "optimize.verbose-status",
        "privacy.input-personalization", "privacy.online-speech",
        "privacy.ceip", "privacy.error-reporting",
        "privacy.cloud-clipboard", "privacy.edge-personalization",
        "features.autorun-disable", "features.update-notify",
        "features.no-auto-reboot", "features.exclude-drivers",
        "features.logon-blur-disable", "features.consumer-features-block",
        "optimize.fullscreen-optimizations-off"
    };

    /// <summary>
    /// Impact tiers in Optim's own language. Gentle is the default (safe for
    /// everyone); Moderate needs intent; Bold can bite on some hardware.
    /// Bold tweaks are never part of "apply suggested".
    /// </summary>
    private static readonly HashSet<string> ModerateIds = new(StringComparer.Ordinal)
    {
        "optimize.network-throttle", "optimize.systemresponsiveness",
        "optimize.gaming-priority", "optimize.8dot3-disable",
        "optimize.power-throttling-off", "optimize.search-indexing",
        "optimize.fullscreen-optimizations-off",
        "features.hibernation", "features.update-notify",
        "features.no-auto-reboot", "features.exclude-drivers"
    };

    private static readonly HashSet<string> BoldIds = new(StringComparer.Ordinal)
    {
        "optimize.hags"
    };

    private static readonly HashSet<string> NotSuggestedIds = new(StringComparer.Ordinal)
    {
        "optimize.power-throttling-off", "optimize.fullscreen-optimizations-off",
        "features.update-notify", "features.no-auto-reboot", "features.exclude-drivers"
    };

    /// <summary>
    /// Display sections (Optim's own grouping). Applied in the tier pass so
    /// the 71 entries stay declarative without per-entry edits.
    /// </summary>
    private static readonly Dictionary<string, string> SectionMap = new(StringComparer.Ordinal)
    {
        // Optimize
        ["optimize.menu-show-delay"] = "Visual Speed",
        ["optimize.startup-delay"] = "Startup & Shutdown",
        ["optimize.shutdown-fast"] = "Startup & Shutdown",
        ["optimize.service-shutdown"] = "Startup & Shutdown",
        ["optimize.win32-long-paths"] = "System Core",
        ["optimize.network-throttle"] = "System Core",
        ["optimize.systemresponsiveness"] = "System Core",
        ["optimize.gaming-priority"] = "Gaming & Input",
        ["optimize.gamebar"] = "Gaming & Input",
        ["optimize.visual-effects-performance"] = "Visual Speed",
        ["optimize.search-indexing"] = "System Core",
        ["optimize.transparency-disable"] = "Visual Speed",
        ["optimize.window-animation"] = "Visual Speed",
        ["optimize.background-apps"] = "System Core",
        ["optimize.8dot3-disable"] = "System Core",
        ["optimize.autoend-tasks"] = "Startup & Shutdown",
        ["optimize.hags"] = "Gaming & Input",
        ["optimize.game-mode"] = "Gaming & Input",
        ["optimize.power-throttling-off"] = "Power & Battery",
        ["optimize.verbose-status"] = "Startup & Shutdown",
        ["optimize.delivery-p2p-disable"] = "System Core",
        ["optimize.aero-shake-disable"] = "Visual Speed",
        ["optimize.snap-flyout-disable"] = "Visual Speed",
        ["optimize.mouse-precision-off"] = "Gaming & Input",
        ["optimize.keyboard-fast-repeat"] = "Gaming & Input",
        ["optimize.fullscreen-optimizations-off"] = "Gaming & Input",
        // Privacy
        ["privacy.telemetry"] = "Diagnostics & Feedback",
        ["privacy.advertising-id"] = "Tracking & Ads",
        ["privacy.activity-history"] = "System & Cloud",
        ["privacy.location"] = "System & Cloud",
        ["privacy.tailored-experiences"] = "Tracking & Ads",
        ["privacy.feedback"] = "Diagnostics & Feedback",
        ["privacy.cortana"] = "Input & Voice",
        ["privacy.bing-search"] = "System & Cloud",
        ["privacy.recall"] = "System & Cloud",
        ["privacy.wifi-sense"] = "System & Cloud",
        ["privacy.app-launch-tracking"] = "System & Cloud",
        ["privacy.input-personalization"] = "Input & Voice",
        ["privacy.online-speech"] = "Input & Voice",
        ["privacy.language-list-access"] = "Input & Voice",
        ["privacy.ceip"] = "Diagnostics & Feedback",
        ["privacy.error-reporting"] = "Diagnostics & Feedback",
        ["privacy.search-highlights"] = "Tracking & Ads",
        ["privacy.clipboard-history"] = "System & Cloud",
        ["privacy.cloud-clipboard"] = "System & Cloud",
        ["privacy.edge-personalization"] = "Tracking & Ads",
        ["privacy.suggested-apps"] = "Tracking & Ads",
        ["privacy.handwriting-sharing"] = "Input & Voice",
        // Features
        ["features.widgets"] = "Taskbar & Shell",
        ["features.chat"] = "Taskbar & Shell",
        ["features.copilot"] = "System Behavior",
        ["features.hibernation"] = "System Behavior",
        ["features.lock-screen"] = "Explorer & Desktop",
        ["features.action-center"] = "Taskbar & Shell",
        ["features.context-menu-classic"] = "Taskbar & Shell",
        ["features.bing-taskbar"] = "Taskbar & Shell",
        ["features.file-extensions"] = "Explorer & Desktop",
        ["features.hidden-files"] = "Explorer & Desktop",
        ["features.full-path-title"] = "Explorer & Desktop",
        ["features.taskbar-widgets-toggle"] = "Taskbar & Shell",
        ["features.taskbar-chat-toggle"] = "Taskbar & Shell",
        ["features.end-task-rightclick"] = "Taskbar & Shell",
        ["features.autorun-disable"] = "System Behavior",
        ["features.update-notify"] = "Windows Update",
        ["features.no-auto-reboot"] = "Windows Update",
        ["features.exclude-drivers"] = "Windows Update",
        ["features.logon-blur-disable"] = "Explorer & Desktop",
        ["features.thispc-desktop"] = "Explorer & Desktop",
        ["features.consumer-features-block"] = "System Behavior",
        ["features.dark-theme"] = "Explorer & Desktop"
    };

    private static readonly Dictionary<TweakCategory, string[]> SectionOrder = new()
    {
        [TweakCategory.Optimize] = new[] { "Power & Battery", "Startup & Shutdown", "Gaming & Input", "Visual Speed", "System Core" },
        [TweakCategory.Privacy] = new[] { "Tracking & Ads", "Diagnostics & Feedback", "Input & Voice", "System & Cloud" },
        [TweakCategory.Features] = new[] { "Taskbar & Shell", "Explorer & Desktop", "System Behavior", "Windows Update" }
    };

    /// <summary>Sort rank for grouped display; unknown sections sink to the end.</summary>
    public static int SectionIndex(TweakCategory category, string section)
    {
        if (SectionOrder.TryGetValue(category, out var order))
        {
            var i = Array.IndexOf(order, section);
            if (i >= 0)
            {
                return i;
            }
        }
        return int.MaxValue;
    }

    public static IReadOnlyList<TweakDefinition> All { get; } = Build();

    public static IEnumerable<TweakDefinition> For(TweakCategory category) =>
        All.Where(t => t.Category == category);

    private static IReadOnlyList<TweakDefinition> Build()
    {
        var list = new List<TweakDefinition>();

        // ------------------------- Optimize -------------------------
        list.Add(new TweakDefinition(
            "optimize.menu-show-delay", TweakCategory.Optimize,
            "Instant menu display",
            "Removes the artificial delay before menus open.",
            new[] { new RegistryOperation(HKCU, @"Control Panel\Desktop", "MenuShowDelay", RegistryValueHint.String, "0") },
            new[] { new RegistryOperation(HKCU, @"Control Panel\Desktop", "MenuShowDelay", RegistryValueHint.String, "400") }));

        list.Add(new TweakDefinition(
            "optimize.startup-delay", TweakCategory.Optimize,
            "Remove startup delay",
            "Runs startup apps without the artificial startup serialization delay.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "optimize.shutdown-fast", TweakCategory.Optimize,
            "Fast shutdown",
            "Trims the wait window for unresponsive apps at shutdown.",
            new[]
            {
                new RegistryOperation(HKCU, @"Control Panel\Desktop", "WaitToKillAppTimeout", RegistryValueHint.String, "2000"),
                new RegistryOperation(HKCU, @"Control Panel\Desktop", "HungAppTimeout", RegistryValueHint.String, "1000")
            },
            new[]
            {
                new RegistryOperation(HKCU, @"Control Panel\Desktop", "WaitToKillAppTimeout", RegistryValueHint.String, "5000"),
                new RegistryOperation(HKCU, @"Control Panel\Desktop", "HungAppTimeout", RegistryValueHint.String, "5000")
            }));

        list.Add(new TweakDefinition(
            "optimize.service-shutdown", TweakCategory.Optimize,
            "Faster service shutdown",
            "Shortens the grace period Windows gives services to stop.",
            new[] { new RegistryOperation(HKLM, @"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", RegistryValueHint.String, "2000") },
            new[] { new RegistryOperation(HKLM, @"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", RegistryValueHint.String, "5000") }));

        list.Add(new TweakDefinition(
            "optimize.win32-long-paths", TweakCategory.Optimize,
            "Win32 long paths",
            "Allows paths longer than 260 characters.",
            new[] { new RegistryOperation(HKLM, @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKLM, @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", RegistryValueHint.DWord, 0) }));

        list.Add(new TweakDefinition(
            "optimize.network-throttle", TweakCategory.Optimize,
            "Lift network throttling",
            "Removes the multimedia-class network throttle for streaming/VOIP headroom.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", RegistryValueHint.DWord, unchecked((int)0xFFFFFFFF)) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "optimize.systemresponsiveness", TweakCategory.Optimize,
            "Background responsiveness",
            "Prioritizes active programs over background services.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", RegistryValueHint.DWord, 10) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", RegistryValueHint.DWord, 20) }));

        list.Add(new TweakDefinition(
            "optimize.gaming-priority", TweakCategory.Optimize,
            "Gaming priority",
            "Schedules games as high priority tasks.",
            new[]
            {
                new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "GPU Priority", RegistryValueHint.DWord, 8),
                new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Priority", RegistryValueHint.DWord, 6),
                new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Scheduling Category", RegistryValueHint.String, "High")
            },
            new[]
            {
                new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "GPU Priority", RegistryValueHint.DWord, null, DeleteValue: true),
                new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Priority", RegistryValueHint.DWord, null, DeleteValue: true),
                new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Scheduling Category", RegistryValueHint.String, null, DeleteValue: true)
            }));

        list.Add(new TweakDefinition(
            "optimize.gamebar", TweakCategory.Optimize,
            "Disable Game Bar DVR",
            "Stops background game clip recording to free GPU time.",
            new[]
            {
                new RegistryOperation(HKCU, @"System\GameConfigStore", "GameDVR_Enabled", RegistryValueHint.DWord, 0),
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", RegistryValueHint.DWord, 0)
            },
            new[]
            {
                new RegistryOperation(HKCU, @"System\GameConfigStore", "GameDVR_Enabled", RegistryValueHint.DWord, 1),
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", RegistryValueHint.DWord, null, DeleteValue: true)
            }));

        list.Add(new TweakDefinition(
            "optimize.visual-effects-performance", TweakCategory.Optimize,
            "Best visual performance",
            "Sets the performance/quality selector to custom; pair it with the animation and transparency toggles for the full effect.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", RegistryValueHint.DWord, 2) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", RegistryValueHint.DWord, null, DeleteValue: true) },
            RestartRequired: true));

        list.Add(new TweakDefinition(
            "optimize.search-indexing", TweakCategory.Optimize,
            "Disable search indexing",
            "Stops background indexing for lower disk and CPU load.",
            new[] { new RegistryOperation(HKLM, @"SYSTEM\CurrentControlSet\Services\WSearch", "Start", RegistryValueHint.DWord, 4) },
            new[] { new RegistryOperation(HKLM, @"SYSTEM\CurrentControlSet\Services\WSearch", "Start", RegistryValueHint.DWord, null, DeleteValue: true) },
            RestartRequired: true));

        list.Add(new TweakDefinition(
            "optimize.transparency-disable", TweakCategory.Optimize,
            "Disable transparency effects",
            "Turns off acrylic and transparency for a snappier desktop.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", RegistryValueHint.DWord, 1) }));

        list.Add(new TweakDefinition(
            "optimize.window-animation", TweakCategory.Optimize,
            "Disable window animations",
            "Stops minimize/maximize animations for instant window response.",
            new[] { new RegistryOperation(HKCU, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", RegistryValueHint.String, "0") },
            new[] { new RegistryOperation(HKCU, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", RegistryValueHint.String, "1") }));

        list.Add(new TweakDefinition(
            "optimize.background-apps", TweakCategory.Optimize,
            "Disable background apps",
            "Prevents Store apps from running in the background.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", RegistryValueHint.DWord, 0) }));

        list.Add(new TweakDefinition(
            "optimize.8dot3-disable", TweakCategory.Optimize,
            "Disable 8.3 short filenames for new volumes",
            "Sets the global default so newly formatted volumes skip legacy 8.3 names; existing volumes are untouched.",
            new[] { new RegistryOperation(HKLM, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisable8dot3NameCreation", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKLM, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisable8dot3NameCreation", RegistryValueHint.DWord, null, DeleteValue: true) },
            RestartRequired: true));

        list.Add(new TweakDefinition(
            "optimize.autoend-tasks", TweakCategory.Optimize,
            "Auto-close hung apps",
            "Ends unresponsive apps automatically at shutdown without prompts.",
            new[] { new RegistryOperation(HKCU, @"Control Panel\Desktop", "AutoEndTasks", RegistryValueHint.String, "1") },
            new[] { new RegistryOperation(HKCU, @"Control Panel\Desktop", "AutoEndTasks", RegistryValueHint.String, "0") }));

        list.Add(new TweakDefinition(
            "optimize.hags", TweakCategory.Optimize,
            "Hardware GPU scheduling",
            "Lets the GPU manage its own memory to cut latency in games.",
            new[] { new RegistryOperation(HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", RegistryValueHint.DWord, 2) },
            new[] { new RegistryOperation(HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", RegistryValueHint.DWord, 1) },
            RestartRequired: true));

        list.Add(new TweakDefinition(
            "optimize.game-mode", TweakCategory.Optimize,
            "Enable Game Mode",
            "Prioritizes game processes for steadier frame rates.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\GameBar", "AllowAutoGameMode", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\GameBar", "AllowAutoGameMode", RegistryValueHint.DWord, 0) }));

        list.Add(new TweakDefinition(
            "optimize.power-throttling-off", TweakCategory.Optimize,
            "Disable power throttling",
            "Stops Windows from throttling background work on laptops.",
            new[] { new RegistryOperation(HKLM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKLM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", RegistryValueHint.DWord, 0) },
            RestartRequired: true));

        list.Add(new TweakDefinition(
            "optimize.verbose-status", TweakCategory.Optimize,
            "Verbose boot messages",
            "Shows detailed status text during startup and shutdown.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "VerboseStatus", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "VerboseStatus", RegistryValueHint.DWord, 0) }));

        list.Add(new TweakDefinition(
            "optimize.delivery-p2p-disable", TweakCategory.Optimize,
            "Disable update peer sharing",
            "Stops uploading downloaded updates to other PCs on the network.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "optimize.aero-shake-disable", TweakCategory.Optimize,
            "Disable Aero Shake",
            "Stops windows minimizing when you shake the title bar.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DisallowShaking", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DisallowShaking", RegistryValueHint.DWord, 0) }));

        list.Add(new TweakDefinition(
            "optimize.snap-flyout-disable", TweakCategory.Optimize,
            "Disable Snap flyout",
            "Hides the snap-layout popup when hovering maximize.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "EnableSnapAssistFlyout", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "EnableSnapAssistFlyout", RegistryValueHint.DWord, 1) }));

        // ------------------------- Privacy -------------------------
        list.Add(new TweakDefinition(
            "privacy.telemetry", TweakCategory.Privacy,
            "Minimal telemetry (policy)",
            "Requests Security-level diagnostic data via policy; on Home/Pro builds that ignore level 0 the request is clamped to the lowest honored level.",
            new[]
            {
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", RegistryValueHint.DWord, 0),
                new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", RegistryValueHint.DWord, 0)
            },
            new[]
            {
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", RegistryValueHint.DWord, null, DeleteValue: true),
                new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", RegistryValueHint.DWord, null, DeleteValue: true)
            }));

        list.Add(new TweakDefinition(
            "privacy.advertising-id", TweakCategory.Privacy,
            "Disable advertising ID",
            "Stops apps from using an advertising ID for cross-app tracking.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", RegistryValueHint.DWord, 1) }));

        list.Add(new TweakDefinition(
            "privacy.activity-history", TweakCategory.Privacy,
            "Disable activity history",
            "Stops the timeline from collecting app and file usage.",
            new[]
            {
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", RegistryValueHint.DWord, 0),
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", RegistryValueHint.DWord, 0),
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", RegistryValueHint.DWord, 0)
            },
            new[]
            {
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", RegistryValueHint.DWord, null, DeleteValue: true),
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", RegistryValueHint.DWord, null, DeleteValue: true),
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", RegistryValueHint.DWord, null, DeleteValue: true)
            }));

        list.Add(new TweakDefinition(
            "privacy.location", TweakCategory.Privacy,
            "Disable location tracking",
            "Prevents apps and services from reading your location.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", RegistryValueHint.DWord, 0) }));

        list.Add(new TweakDefinition(
            "privacy.tailored-experiences", TweakCategory.Privacy,
            "Disable tailored experiences",
            "Turns off personalized tips and ads based on diagnostic data.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", RegistryValueHint.DWord, 1) }));

        list.Add(new TweakDefinition(
            "privacy.feedback", TweakCategory.Privacy,
            "Silence feedback requests",
            "Stops Windows from asking for feedback.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "privacy.cortana", TweakCategory.Privacy,
            "Disable Cortana",
            "Turns off the Cortana assistant and its data collection.",
            new[]
            {
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", RegistryValueHint.DWord, 0),
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", RegistryValueHint.DWord, 0)
            },
            new[]
            {
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", RegistryValueHint.DWord, null, DeleteValue: true),
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", RegistryValueHint.DWord, null, DeleteValue: true)
            }));

        list.Add(new TweakDefinition(
            "privacy.bing-search", TweakCategory.Privacy,
            "Local-only start menu search",
            "Keeps start menu search off the web.",
            new[] { new RegistryOperation(HKCU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKCU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "privacy.recall", TweakCategory.Privacy,
            "Disable Recall snapshots",
            "Blocks periodic screen snapshots used by AI recall features.",
            new[]
            {
                new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DisableAIDataAnalysis", RegistryValueHint.DWord, 1),
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", RegistryValueHint.DWord, 1)
            },
            new[]
            {
                new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DisableAIDataAnalysis", RegistryValueHint.DWord, null, DeleteValue: true),
                new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", RegistryValueHint.DWord, null, DeleteValue: true)
            }));

        list.Add(new TweakDefinition(
            "privacy.wifi-sense", TweakCategory.Privacy,
            "Disable Wi-Fi sharing",
            "Stops sharing of Wi-Fi credentials with contacts.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config", "AutoConnectAllowedOEM", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config", "AutoConnectAllowedOEM", RegistryValueHint.DWord, 1) }));

        list.Add(new TweakDefinition(
            "privacy.app-launch-tracking", TweakCategory.Privacy,
            "Disable app launch tracking",
            "Stops Windows tracking app launches to highlight new installs.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", RegistryValueHint.DWord, 1) }));

        list.Add(new TweakDefinition(
            "privacy.input-personalization", TweakCategory.Privacy,
            "Disable typing personalization",
            "Stops collection of typing and inking data for personalization.",
            new[]
            {
                new RegistryOperation(HKCU, @"Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", RegistryValueHint.DWord, 0),
                new RegistryOperation(HKCU, @"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", RegistryValueHint.DWord, 1),
                new RegistryOperation(HKCU, @"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", RegistryValueHint.DWord, 1)
            },
            new[]
            {
                new RegistryOperation(HKCU, @"Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", RegistryValueHint.DWord, 1),
                new RegistryOperation(HKCU, @"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", RegistryValueHint.DWord, 0),
                new RegistryOperation(HKCU, @"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", RegistryValueHint.DWord, 0)
            }));

        list.Add(new TweakDefinition(
            "privacy.online-speech", TweakCategory.Privacy,
            "Disable online speech recognition",
            "Keeps voice data on-device instead of cloud processing.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", RegistryValueHint.DWord, 1) }));

        list.Add(new TweakDefinition(
            "privacy.language-list-access", TweakCategory.Privacy,
            "Block sites reading language list",
            "Stops websites accessing your preferred-languages list.",
            new[] { new RegistryOperation(HKCU, @"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKCU, @"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", RegistryValueHint.DWord, 0) }));

        list.Add(new TweakDefinition(
            "privacy.ceip", TweakCategory.Privacy,
            "Disable Customer Experience program",
            "Opts out of the Windows Customer Experience Improvement Program.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "privacy.error-reporting", TweakCategory.Privacy,
            "Disable error reporting",
            "Stops sending crash dumps and error reports to Microsoft.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", RegistryValueHint.DWord, 0) }));

        list.Add(new TweakDefinition(
            "privacy.search-highlights", TweakCategory.Privacy,
            "Disable search highlights",
            "Removes sponsored and web highlights from Windows Search.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", RegistryValueHint.DWord, 1) }));

        list.Add(new TweakDefinition(
            "privacy.clipboard-history", TweakCategory.Privacy,
            "Disable clipboard history",
            "Stops Windows remembering everything you copy.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Clipboard", "EnableClipboardHistory", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Clipboard", "EnableClipboardHistory", RegistryValueHint.DWord, 1) }));

        list.Add(new TweakDefinition(
            "privacy.cloud-clipboard", TweakCategory.Privacy,
            "Disable cloud clipboard sync",
            "Keeps clipboard content on this device only.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "AllowCrossDeviceClipboard", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "AllowCrossDeviceClipboard", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "privacy.edge-personalization", TweakCategory.Privacy,
            "Disable Edge personalization",
            "Stops Edge sending browsing data for personalized ads.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "PersonalizationReportingEnabled", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "PersonalizationReportingEnabled", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "privacy.suggested-apps", TweakCategory.Privacy,
            "Block suggested apps",
            "Stops automatic install of suggested third-party apps.",
            new[]
            {
                new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", RegistryValueHint.DWord, 0),
                new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", RegistryValueHint.DWord, 0)
            },
            new[]
            {
                new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", RegistryValueHint.DWord, 1),
                new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", RegistryValueHint.DWord, 1)
            }));

        list.Add(new TweakDefinition(
            "privacy.handwriting-sharing", TweakCategory.Privacy,
            "Disable handwriting data sharing",
            "Stops sharing handwriting samples to improve recognition.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "HandwritingDataSharingEnabled", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "HandwritingDataSharingEnabled", RegistryValueHint.DWord, 1) }));

        // ------------------------- Features -------------------------
        list.Add(new TweakDefinition(
            "features.widgets", TweakCategory.Features,
            "Disable Widgets",
            "Removes the widgets board and taskbar entry.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", RegistryValueHint.DWord, null, DeleteValue: true) },
            RestartRequired: true));

        list.Add(new TweakDefinition(
            "features.chat", TweakCategory.Features,
            "Disable Chat taskbar icon",
            "Removes the Teams Chat flyout from the taskbar.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Chat", "ChatIcon", RegistryValueHint.DWord, 3) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Chat", "ChatIcon", RegistryValueHint.DWord, null, DeleteValue: true) },
            RestartRequired: true));

        list.Add(new TweakDefinition(
            "features.copilot", TweakCategory.Features,
            "Disable Copilot (policy)",
            "Writes the Copilot policy key; it applies on builds where the OS honors it, otherwise it is a harmless marker.",
            new[] { new RegistryOperation(HKCU, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKCU, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", RegistryValueHint.DWord, null, DeleteValue: true) },
            RestartRequired: true));

        list.Add(new TweakDefinition(
            "features.hibernation", TweakCategory.Features,
            "Disable hibernation",
            "Turns hibernation off and lets Windows reclaim hiberfil.sys (verified via the power API, not just a registry guess).",
            new[] { new RegistryOperation(HKLM, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKLM, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", RegistryValueHint.DWord, 1) },
            RestartRequired: true));

        list.Add(new TweakDefinition(
            "features.lock-screen", TweakCategory.Features,
            "Disable lock screen",
            "Skips the lock screen when resuming from sleep.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", RegistryValueHint.DWord, 0) }));

        list.Add(new TweakDefinition(
            "features.action-center", TweakCategory.Features,
            "Disable notification center",
            "Hides notifications and the quick settings flyout.",
            new[] { new RegistryOperation(HKCU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableNotificationCenter", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKCU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableNotificationCenter", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "features.context-menu-classic", TweakCategory.Features,
            "Classic context menu",
            "Restores the full Windows 10-style right-click menu.",
            new[]
            {
                new RegistryOperation(HKCU, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", null, RegistryValueHint.String, "")
            },
            new[]
            {
                new RegistryOperation(HKCU, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", null, RegistryValueHint.String, null, DeleteKey: true)
            },
            RestartRequired: true));

        list.Add(new TweakDefinition(
            "features.bing-taskbar", TweakCategory.Features,
            "Minimal taskbar search box",
            "Sets the taskbar search box to its most compact mode; use the Search settings page to hide it fully.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "features.file-extensions", TweakCategory.Features,
            "Show file extensions",
            "Always shows extensions like .exe and .txt in Explorer.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", RegistryValueHint.DWord, 1) }));

        list.Add(new TweakDefinition(
            "features.hidden-files", TweakCategory.Features,
            "Show hidden files",
            "Reveals hidden files and folders in Explorer.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", RegistryValueHint.DWord, 2) }));

        list.Add(new TweakDefinition(
            "features.full-path-title", TweakCategory.Features,
            "Full path in title bar",
            "Shows the complete folder path in Explorer title bars.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState", "FullPath", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState", "FullPath", RegistryValueHint.DWord, 0) }));

        list.Add(new TweakDefinition(
            "features.taskbar-widgets-toggle", TweakCategory.Features,
            "Hide Widgets from taskbar",
            "Removes the Widgets entry point from the taskbar.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", RegistryValueHint.DWord, 1) }));

        list.Add(new TweakDefinition(
            "features.taskbar-chat-toggle", TweakCategory.Features,
            "Hide Chat from taskbar",
            "Removes the Chat icon from the taskbar.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn", RegistryValueHint.DWord, 1) }));

        list.Add(new TweakDefinition(
            "features.end-task-rightclick", TweakCategory.Features,
            "End task with right-click",
            "Adds End task to the taskbar app right-click menu.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", "TaskbarEndTask", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", "TaskbarEndTask", RegistryValueHint.DWord, 0) }));

        list.Add(new TweakDefinition(
            "features.autorun-disable", TweakCategory.Features,
            "Disable AutoRun",
            "Blocks USB and disc AutoRun to stop malware spread.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun", RegistryValueHint.DWord, 255) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "features.update-notify", TweakCategory.Features,
            "Notify before downloading updates",
            "Sets the update policy to notify before download; on Windows 10/11 Home this policy may be ignored.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions", RegistryValueHint.DWord, 2) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "features.no-auto-reboot", TweakCategory.Features,
            "No forced reboot after updates",
            "Prevents automatic restart while you are signed in.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "features.exclude-drivers", TweakCategory.Features,
            "Exclude drivers from updates",
            "Stops Windows Update replacing working drivers automatically.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "features.logon-blur-disable", TweakCategory.Features,
            "Sharp sign-in background",
            "Removes the blur from the sign-in screen background.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "DisableAcrylicBackgroundOnLogon", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "DisableAcrylicBackgroundOnLogon", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "features.thispc-desktop", TweakCategory.Features,
            "Show This PC on desktop",
            "Pins the This PC icon to the desktop.",
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel", "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", RegistryValueHint.DWord, 0) },
            new[] { new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel", "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", RegistryValueHint.DWord, 1) }));

        list.Add(new TweakDefinition(
            "features.consumer-features-block", TweakCategory.Features,
            "Block suggested bloat",
            "Stops Windows reinstalling suggested third-party apps.",
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", RegistryValueHint.DWord, null, DeleteValue: true) }));

        list.Add(new TweakDefinition(
            "features.dark-theme", TweakCategory.Features,
            "Dark app theme",
            "Switches built-in apps and system UI to dark mode.",
            new[]
            {
                new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", RegistryValueHint.DWord, 0),
                new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", RegistryValueHint.DWord, 0)
            },
            new[]
            {
                new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", RegistryValueHint.DWord, 1),
                new RegistryOperation(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", RegistryValueHint.DWord, 1)
            }));

        AddLatencyTweaks(list);

        return list.Select(ApplyTiers).ToList();
    }

    private static TweakDefinition ApplyTiers(TweakDefinition t)
    {
        if (AdvancedIds.Contains(t.Id))
        {
            t = t with { IsAdvanced = true };
        }

        if (BoldIds.Contains(t.Id))
        {
            t = t with { Risk = TweakRisk.Bold, Points = 10, Recommended = false };
        }
        else if (ModerateIds.Contains(t.Id) && t.Risk == TweakRisk.Gentle)
        {
            t = t with { Risk = TweakRisk.Moderate, Points = 7 };
        }

        if (NotSuggestedIds.Contains(t.Id))
        {
            t = t with { Recommended = false };
        }

        if (SectionMap.TryGetValue(t.Id, out var section))
        {
            t = t with { Section = section };
        }

        return t;
    }

    /// <summary>
    /// Gaming and input-latency tweaks, appended after the main catalog.
    /// Key paths verified against Microsoft Community guidance; every write
    /// is journaled so toggle-off restores the exact previous state.
    /// </summary>
    private static void AddLatencyTweaks(List<TweakDefinition> list)
    {
        list.Add(new TweakDefinition(
            "optimize.mouse-precision-off", TweakCategory.Optimize,
            "Raw mouse input",
            "Disables pointer precision acceleration for 1:1 mouse movement.",
            new[]
            {
                new RegistryOperation(HKCU, @"Control Panel\Mouse", "MouseSpeed", RegistryValueHint.String, "0"),
                new RegistryOperation(HKCU, @"Control Panel\Mouse", "MouseThreshold1", RegistryValueHint.String, "0"),
                new RegistryOperation(HKCU, @"Control Panel\Mouse", "MouseThreshold2", RegistryValueHint.String, "0")
            },
            new[]
            {
                new RegistryOperation(HKCU, @"Control Panel\Mouse", "MouseSpeed", RegistryValueHint.String, "1"),
                new RegistryOperation(HKCU, @"Control Panel\Mouse", "MouseThreshold1", RegistryValueHint.String, "6"),
                new RegistryOperation(HKCU, @"Control Panel\Mouse", "MouseThreshold2", RegistryValueHint.String, "10")
            }));

        list.Add(new TweakDefinition(
            "optimize.keyboard-fast-repeat", TweakCategory.Optimize,
            "Fast key repeat",
            "Shortens the key-press delay and maximizes the repeat rate.",
            new[]
            {
                new RegistryOperation(HKCU, @"Control Panel\Keyboard", "KeyboardDelay", RegistryValueHint.String, "0"),
                new RegistryOperation(HKCU, @"Control Panel\Keyboard", "KeyboardSpeed", RegistryValueHint.String, "31")
            },
            new[]
            {
                new RegistryOperation(HKCU, @"Control Panel\Keyboard", "KeyboardDelay", RegistryValueHint.String, "1"),
                new RegistryOperation(HKCU, @"Control Panel\Keyboard", "KeyboardSpeed", RegistryValueHint.String, null, DeleteValue: true)
            }));

        list.Add(new TweakDefinition(
            "optimize.fullscreen-optimizations-off", TweakCategory.Optimize,
            "True fullscreen exclusive",
            "Stops Windows replacing exclusive fullscreen with optimized borderless in games.",
            new[]
            {
                new RegistryOperation(HKCU, @"System\GameConfigStore", "GameDVR_FSEBehaviorMode", RegistryValueHint.DWord, 2),
                new RegistryOperation(HKCU, @"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", RegistryValueHint.DWord, 1),
                new RegistryOperation(HKCU, @"System\GameConfigStore", "GameDVR_FSEBehavior", RegistryValueHint.DWord, 2),
                new RegistryOperation(HKCU, @"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", RegistryValueHint.DWord, 1)
            },
            new[]
            {
                new RegistryOperation(HKCU, @"System\GameConfigStore", "GameDVR_FSEBehaviorMode", RegistryValueHint.DWord, null, DeleteValue: true),
                new RegistryOperation(HKCU, @"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", RegistryValueHint.DWord, null, DeleteValue: true),
                new RegistryOperation(HKCU, @"System\GameConfigStore", "GameDVR_FSEBehavior", RegistryValueHint.DWord, null, DeleteValue: true),
                new RegistryOperation(HKCU, @"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", RegistryValueHint.DWord, null, DeleteValue: true)
            },
            RestartRequired: true,
            Risk: TweakRisk.Moderate,
            Points: 8,
            Recommended: false));
    }
}
