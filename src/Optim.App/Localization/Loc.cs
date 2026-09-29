using Microsoft.Windows.ApplicationModel.Resources;

namespace Optim.App.Localization;

/// <summary>
/// Resolves user-facing strings from Strings/&lt;culture&gt;/Resources.resw.
/// </summary>
/// <remarks>
/// Every lookup takes an English fallback and swallows loader failures, so a
/// missing key, a missing resource map or a packaging change can never blank
/// the UI. That matters here because Optim ships unpackaged: the WinAppSDK
/// resource loader reads resources.pri from the app folder, and this helper
/// has to stay safe if that file is ever absent.
/// </remarks>
public static class Loc
{
    private static ResourceLoader? _loader;
    private static bool _unavailable;

    private static Microsoft.Windows.ApplicationModel.Resources.ResourceManager? _overrideManager;
    private static Microsoft.Windows.ApplicationModel.Resources.ResourceContext? _overrideContext;

    /// <summary>
    /// BCP-47 language tag overriding the system language for Loc-resolved
    /// strings (set at startup from Settings). Null/empty = follow the system.
    /// </summary>
    public static string? OverrideLanguage { get; set; }

    /// <summary>
    /// Returns the localized value for <paramref name="key"/>, or
    /// <paramref name="fallback"/> when the key is missing or resources cannot
    /// be read.
    /// </summary>
    public static string Get(string key, string fallback)
    {
        if (_unavailable)
        {
            return fallback;
        }

        // Language override path: a dedicated context whose Language qualifier
        // points at the saved tag. Resolves the language's resw value, falling
        // back to the neutral resource per key (partial translations stay safe).
        if (!string.IsNullOrEmpty(OverrideLanguage))
        {
            try
            {
                _overrideManager ??= new Microsoft.Windows.ApplicationModel.Resources.ResourceManager();
                _overrideContext ??= _overrideManager.CreateResourceContext();
                _overrideContext.QualifierValues["Language"] = OverrideLanguage;
                var value = _overrideManager.MainResourceMap.GetValue(key, _overrideContext).ValueAsString;
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            catch
            {
                // Fall through to the default loader path.
            }
        }

        try
        {
            _loader ??= new ResourceLoader();
            var value = _loader.GetString(key);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
        catch (Exception ex)
        {
            // First failure disables the loader for the session: retrying a
            // broken resource map per string would just spam the log.
            _unavailable = true;
            Optim.Core.Logging.FileLogger.Warn(
                $"Localization unavailable, using built-in English text: {ex.Message}");
            return fallback;
        }
    }

    /// <summary>
    /// Drops the cached loader after a language change so the next Get reads
    /// the new qualifier values instead of a stale ResourceLoader instance.
    /// </summary>
    public static void ResetForLanguageChange()
    {
        _loader = null;
        _unavailable = false;
    }
}
