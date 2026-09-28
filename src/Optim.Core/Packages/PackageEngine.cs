using System.Diagnostics;

namespace Optim.Core.Packages;

public sealed record WingetPackage(string Id, string Name, string InstalledVersion, string AvailableVersion);

public sealed record WingetSearchResult(string Id, string Name, string Version);

/// <summary>
/// Thin wrapper over the winget CLI. Lists upgrades and runs selected upgrades.
/// winget itself is the package source; this engine only orchestrates it.
/// </summary>
public sealed class PackageEngine
{
    public static bool IsWingetAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("winget", "--version")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            })!;
            var outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return p.ExitCode == 0 && outp.Contains('.');
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<WingetPackage>> ListUpgradesAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var (code, stdout) = await RunAsync("winget", "upgrade --include-unknown --disable-interactivity --accept-source-agreements --source winget", timeout.Token);
        if (code != 0)
        {
            return Array.Empty<WingetPackage>();
        }

        return ParseUpgradeTable(stdout);
    }

    /// <summary>
    /// Parses winget upgrade table output. Public for unit tests: winget table
    /// formats vary by version and locale (and `winget upgrade` has no JSON
    /// output as of winget 1.29), so this skips headers, separators, footers and
    /// status lines, and locates each row's package id token first rather than
    /// assuming a fixed column count — builds that drop the trailing Source
    /// column or print Unknown versions still parse.
    /// </summary>
    public static IReadOnlyList<WingetPackage> ParseUpgradeTable(string stdout)
    {
        var packages = new List<WingetPackage>();
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return packages;
        }

        foreach (var rawLine in stdout.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.Trim();
            var lower = trimmed.ToLowerInvariant();
            // Header separator, column headers (English + localized), and status/footers: skip noise, then require package-shaped ids.
            if (LooksLikeTableNoise(lower))
            {
                continue;
            }

            // Continuation lines of wrapped names carry no id of their own.
            if (char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            // Columns are Name Id Version Available [Source]: find the id token
            // (package-shaped, lettered so version-like tokens never match) and
            // read the two version tokens after it. Anchoring on the id instead
            // of the end of the line survives a missing Source column.
            for (var i = 0; i + 2 < parts.Length; i++)
            {
                var candidate = parts[i];
                if (!LooksLikePackageId(candidate) || !candidate.Any(char.IsLetter))
                {
                    continue;
                }

                if (!IsVersionToken(parts[i + 1]) || !IsVersionToken(parts[i + 2]))
                {
                    continue;
                }

                var idStart = IndexOfToken(line, parts, i);
                if (idStart <= 0)
                {
                    continue;
                }

                packages.Add(new WingetPackage(
                    candidate,
                    line[..idStart].Trim(),
                    parts[i + 1],
                    parts[i + 2]));
                break; // one package row per line
            }
        }

        return packages;
    }

    /// <summary>Version columns are digit-led or the literal Unknown.</summary>
    private static bool IsVersionToken(string token) =>
        token.Length > 0
        && (char.IsDigit(token[0]) || token.Equals("unknown", StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds the character offset of parts[index] in the original line.</summary>
    private static int IndexOfToken(string line, string[] parts, int index)
    {
        var pos = 0;
        for (var i = 0; i <= index; i++)
        {
            pos = line.IndexOf(parts[i], pos, StringComparison.Ordinal);
            if (pos < 0)
            {
                return -1;
            }

            pos += parts[i].Length;
        }

        return pos - parts[index].Length;
    }

    private static bool LooksLikeTableNoise(string lower)
    {
        if (lower.Contains("---")) return true;
        string[] prefixes = new[]
        {
            "name ", "nom ", "nombre ", "nome ", "name:",
            "the following", "no ", "no upgrades", "no applicable",
            "upgrades available", "up to date", "a jour", "aktuell",
            "aggiorn", "actualizad", "mise a jour", "keine",
            "aucun", "aucune", "nessun", "ningun", "ninguna",
            "geen ", "geen upgrades", "found ", "searching", "recherche",
            "buscando", "ricerca", "suche ", "zoeken", "package agreement",
            "current version", "version actuelle", "version disponible"
        };
        foreach (var p in prefixes) { if (lower.StartsWith(p, StringComparison.Ordinal)) return true; }
        string[] contains = new[] { "up to date", "a jour", "up-to-date", "no newer", "are available", "sont disponibles", "disponibles", "verfugbar" };
        foreach (var c in contains) { if (lower.Contains(c, StringComparison.Ordinal)) return true; }
        return false;
    }

    private static bool LooksLikePackageId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Contains("---")) return false;
        // winget ids are dotted (Vendor.Product), MS Store ids are 12+ alnum, 
        // and versions that anchor here look like 1.2.3.
        if (id.Contains(".", StringComparison.Ordinal)) return true;
        if (id.Length >= 12 && id.All(char.IsLetterOrDigit)) return true;
        return false;
    }

    public async Task<bool> UpgradeAsync(string packageId, Action<string> onOutput, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(packageId) || packageId.Contains('"'))
        {
            throw new ArgumentException("Invalid package id.", nameof(packageId));
        }

        var (code, _) = await RunAsync("winget", $"upgrade --id \"{packageId}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity --source winget", token, onOutput);
        return code == 0;
    }

    public async Task<IReadOnlyList<WingetSearchResult>> SearchAsync(string query, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<WingetSearchResult>();
        }

        var safe = query.Trim().Replace("\"", "");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var (code, stdout) = await RunAsync("winget", $"search \"{safe}\" --disable-interactivity --accept-source-agreements --source winget", timeout.Token);
        if (code != 0)
        {
            return Array.Empty<WingetSearchResult>();
        }

        return ParseSearchTable(stdout);
    }

    /// <summary>
    /// Parses winget search table output (Name | Id | Version | Match | Source).
    /// Columns are aligned with 2+ spaces, so the split survives Match values
    /// like "Tag: editor" that contain a single space. Public for unit tests.
    /// </summary>
    public static IReadOnlyList<WingetSearchResult> ParseSearchTable(string stdout)
    {
        var results = new List<WingetSearchResult>();
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return results;
        }

        foreach (var rawLine in stdout.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line) || char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            var trimmed = line.Trim();
            var lower = trimmed.ToLowerInvariant();
            if (trimmed.Contains("---")
                || trimmed.StartsWith("Name ", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("No ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cols = System.Text.RegularExpressions.Regex.Split(line, @"\s{2,}");
            if (cols.Length < 3)
            {
                continue;
            }

            var name = cols[0].Trim();
            var id = cols[1].Trim();
            var version = cols[2].Trim();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id)
                || string.IsNullOrEmpty(version) || !LooksLikePackageId(id))
            {
                continue;
            }

            results.Add(new WingetSearchResult(id, name, version));
        }

        return results;
    }

    public async Task<bool> InstallAsync(string packageId, Action<string> onOutput, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(packageId) || packageId.Contains('"'))
        {
            throw new ArgumentException("Invalid package id.", nameof(packageId));
        }

        var (code, _) = await RunAsync("winget", $"install --id \"{packageId}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity --source winget", token, onOutput);
        return code == 0;
    }

    private static async Task<(int Code, string Output)> RunAsync(string fileName, string args, CancellationToken token, Action<string>? onOutput = null)
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
                return (-1, "Could not start " + fileName);
            }

            var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) { lines.Enqueue(e.Data); onOutput?.Invoke(e.Data); } };
            // stderr must be drained too: an undrained pipe fills up and hangs winget.
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lines.Enqueue(e.Data); } };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            try
            {
                await p.WaitForExitAsync(token);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return (-1, "Cancelled");
            }

            // Wait for the async readers to flush the last lines before joining.
            await Task.Run(() => p.WaitForExit(), token);
            return (p.ExitCode, string.Join(Environment.NewLine, lines));
        }
        catch (OperationCanceledException)
        {
            return (-1, "Cancelled");
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"{fileName} failed: {ex.Message}");
            return (-1, ex.Message);
        }
    }
}
