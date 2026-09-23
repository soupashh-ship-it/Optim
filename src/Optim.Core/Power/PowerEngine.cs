using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Optim.Core.Power;

public sealed record PowerPlan(string Guid, string Name, bool IsActive);

/// <summary>Power-plan management via powercfg. Lists plans, activates them,
/// and unlocks the Ultimate Performance plan.</summary>
public sealed class PowerEngine
{
    public const string UltimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    public async Task<IReadOnlyList<PowerPlan>> ListAsync()
    {
        var (code, output) = await RunAsync("powercfg", "/list");
        if (code != 0)
        {
            return Array.Empty<PowerPlan>();
        }

        return ParseList(output);
    }

    public async Task<bool> SetActiveAsync(string guid)
    {
        if (!Guid.TryParse(guid, out _))
        {
            throw new ArgumentException("Invalid power plan GUID.", nameof(guid));
        }

        var (code, _) = await RunAsync("powercfg", $"/setactive {guid}");
        return code == 0;
    }

    public async Task<bool> EnableUltimateAsync()
    {
        var (code, _) = await RunAsync("powercfg", $"-duplicatescheme {UltimateGuid}");
        return code == 0;
    }

    /// <summary>Parses `powercfg /list` output. Public for unit tests.</summary>
    public static IReadOnlyList<PowerPlan> ParseList(string output)
    {
        var plans = new List<PowerPlan>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return plans;
        }

        // Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *
        var rx = new Regex(@"GUID:\s*([0-9a-fA-F-]{36})\s*\(([^)]+)\)\s*(\*)?",
            RegexOptions.Compiled);
        foreach (Match m in rx.Matches(output))
        {
            plans.Add(new PowerPlan(
                m.Groups[1].Value.ToLowerInvariant(),
                m.Groups[2].Value.Trim(),
                m.Groups[3].Success));
        }

        return plans;
    }

    private static async Task<(int Code, string Output)> RunAsync(string fileName, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                return (-1, "");
            }

            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return (p.ExitCode, output);
        }
        catch (Exception ex)
        {
            Logging.FileLogger.Error($"{fileName} {args}: {ex.Message}");
            return (-1, ex.Message);
        }
    }
}
