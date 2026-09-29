using System.Diagnostics;
using System.Text.Json;

namespace Optim.Core.SystemDeployment;

/// <summary>A Windows optional feature with its current enablement state.</summary>
public sealed record OptionalFeature(
    string Name,
    string DisplayName,
    string Description,
    bool IsEnabled,
    bool RestartRequired,
    string StateText);

/// <summary>
/// Enumerates and toggles Windows optional features — the same surface as the
/// classic "Turn Windows features on or off" dialog.
///
/// Implementation: Get-WindowsOptionalFeature (the DISM PowerShell module) for
/// listing, because its State property serializes to the invariant strings
/// "Enabled"/"Disabled"/… in every OS language; dism.exe for enable/disable,
/// because its exit codes (0 / 3010) are deterministic and locale-independent.
/// Parsing localized DISM table output would silently mis-detect states on
/// non-English systems, which the app refuses to do.
/// </summary>
public static class DismFeatureEngine
{
    private const string ListScript =
        "Get-WindowsOptionalFeature -Online | " +
        "Select-Object FeatureName, DisplayName, Description, State | ConvertTo-Json -Compress";

    /// <summary>Lists every optional feature. Runs powershell.exe (2–5 s) — call off the UI thread.</summary>
    public static IReadOnlyList<OptionalFeature> ListFeatures()
    {
        var (ok, stdout, error) = RunPowerShell(ListScript, timeoutSeconds: 120);
        if (!ok)
        {
            throw new InvalidOperationException(
                $"Could not enumerate optional features: {error ?? stdout}. Run elevated.");
        }

        var result = new List<OptionalFeature>();
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return result;
        }

        using var doc = JsonDocument.Parse(stdout);
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            // ConvertTo-Json collapses a single-element list into one object.
            result.Add(FromJson(doc.RootElement));
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                result.Add(FromJson(el));
            }
        }

        return result
            .OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static OptionalFeature FromJson(JsonElement el)
    {
        string Str(string prop)
        {
            var s = el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
            return s ?? "";
        }

        var state = Str("State");
        var name = Str("FeatureName");
        var enabled = state.Contains("Enabled", StringComparison.OrdinalIgnoreCase)
            && !state.Contains("Disabled", StringComparison.OrdinalIgnoreCase);

        return new OptionalFeature(
            name,
            string.IsNullOrWhiteSpace(Str("DisplayName")) ? name : Str("DisplayName"),
            Str("Description"),
            enabled,
            state.Contains("Pending", StringComparison.OrdinalIgnoreCase),
            state.Length == 0 ? "Unknown" : state);
    }

    /// <summary>Enables one feature without restarting. Exit code 3010 = success, restart pending.</summary>
    public static (bool Ok, string Detail, bool RestartRequired) Enable(string featureName) =>
        Toggle(featureName, enable: true);

    /// <summary>Disables one feature without restarting.</summary>
    public static (bool Ok, string Detail, bool RestartRequired) Disable(string featureName) =>
        Toggle(featureName, enable: false);

    private static (bool Ok, string Detail, bool RestartRequired) Toggle(string featureName, bool enable)
    {
        if (featureName.Contains('"') || featureName.Contains('\''))
        {
            return (false, "Feature name contains an invalid character.", false);
        }

        var verb = enable ? "enable-feature" : "disable-feature";
        var args = $"/online /{verb} /featurename:{featureName} /norestart";
        try
        {
            var psi = new ProcessStartInfo("dism.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                return (false, "Could not start dism.exe.", false);
            }

            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var exited = p.WaitForExit(1000 * 60 * 10);
            if (!exited)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return (false, "dism.exe timed out after 10 minutes.", false);
            }

            var code = p.ExitCode;
            _ = stdoutTask; // output is English boilerplate; the exit code is the signal
            return code switch
            {
                0 => (true, $"'{featureName}' {(enable ? "enabled" : "disabled")}.", false),
                3010 => (true, $"'{featureName}' {(enable ? "enabled" : "disabled")} — a restart is required to finish.", true),
                87 => (false, $"Windows refused the change for '{featureName}' (invalid name or parent feature missing).", false),
                50 => (false, $"'{featureName}' cannot be changed on this Windows installation.", false),
                _ => (false, $"dism.exe failed for '{featureName}' (exit code {code}). See the application log.", false)
            };
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"DISM {verb} {featureName}: {ex.Message}");
            return (false, $"Could not run dism.exe: {ex.Message}", false);
        }
    }

    /// <summary>Runs powershell.exe with a script and returns (success, stdout, error text).</summary>
    private static (bool Ok, string Stdout, string? Error) RunPowerShell(string script, int timeoutSeconds)
    {
        try
        {
            var psi = new ProcessStartInfo(
                "powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                return (false, "", "could not start powershell.exe");
            }

            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            var exited = p.WaitForExit(timeoutSeconds * 1000);
            if (!exited)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return (false, "", "timed out");
            }

            var error = stderr.GetAwaiter().GetResult();
            return p.ExitCode == 0
                ? (true, stdout.GetAwaiter().GetResult(), null)
                : (false, "", string.IsNullOrWhiteSpace(error) ? $"exit code {p.ExitCode}" : error);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }
}
