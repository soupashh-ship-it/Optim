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
}
