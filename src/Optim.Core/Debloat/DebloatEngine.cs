using Windows.Management.Deployment;

namespace Optim.Core.Debloat;

public sealed record AppPackage(string DisplayName, string PackageFamilyName, string Publisher, bool IsProtected);

/// <summary>
/// Lists installed Appx packages and removes selected ones. A protected list
/// prevents uninstalling components Windows depends on.
/// </summary>
/// <remarks>
/// Threading: PackageManager instances are created per operation on the calling
/// thread. Sharing one WinRT object across threads causes native crashes.
/// </remarks>
public sealed class DebloatEngine
{
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.WindowsStore",
        "Microsoft.Windows.ShellExperienceHost",
        "Microsoft.Windows.StartMenuExperienceHost",
        "Microsoft.UI.Xaml.CBS",
        "Microsoft.WindowsAppRuntime.CBS",
        "Microsoft.VCLibs",
        "Microsoft.NET.Native",
        "Microsoft.Services.Store.Engagement",
        "Microsoft.StorePurchaseApp",
        "Microsoft.SecHealthUI",
        "Microsoft.Windows.Photos",
        "Microsoft.WindowsCalculator",
        "Microsoft.WindowsNotepad",
        "Microsoft.WindowsTerminal",
        "Microsoft.DesktopAppInstaller",
        "Microsoft.XboxIdentityProvider",
        "Microsoft.AAD.BrokerPlugin",
        "Microsoft.AccountsControl",
        "Microsoft.Windows.CloudExperienceHost",
        "Microsoft.Windows.ContentDeliveryManager",
        "Microsoft.Windows.OOBENetworkCaptivePortal",
        "Microsoft.Windows.ParentalControls",
        "Microsoft.Windows.PeopleExperienceHost",
        "Microsoft.Windows.PinningConfirmationDialog",
        "Microsoft.Windows.SecHealthUI",
        "Microsoft.Win32WebViewHost",
        "Microsoft.WindowsAppRuntime"
    };

    /// <summary>
    /// True when the package is a protected system component. Matches on the
    /// bare package name (the part before the first '_' in a family name like
    /// Name_publisher), because comparing the full family string against short
    /// names fails open on real machines.
    /// </summary>
    public static bool IsProtected(string familyName)
    {
        var name = PackageNameOf(familyName);
        return Protected.Any(p =>
            name.Equals(p, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Bare package name: the segment before the first '_' in a family name.</summary>
    public static string PackageNameOf(string? familyName)
    {
        if (string.IsNullOrWhiteSpace(familyName))
        {
            return string.Empty;
        }

        var trimmed = familyName.Trim();
        var cut = trimmed.IndexOf('_');
        return cut <= 0 ? trimmed : trimmed.Substring(0, cut);
    }

    public IReadOnlyList<AppPackage> ListInstalled()
    {
        var result = new List<AppPackage>();
        var pm = new PackageManager();

        foreach (var pkg in pm.FindPackages())
        {
            try
            {
                if (pkg.IsFramework || pkg.IsResourcePackage || pkg.IsBundle)
                {
                    continue;
                }

                var family = pkg.Id.FamilyName;
                result.Add(new AppPackage(
                    string.IsNullOrWhiteSpace(pkg.DisplayName) ? pkg.Id.Name : pkg.DisplayName,
                    family,
                    pkg.PublisherDisplayName ?? "",
                    IsProtected(family)));
            }
            catch
            {
                // Skip packages that fail to project properties.
            }
        }

        return result.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Removes the given packages for the current user. Returns per-package status.</summary>
    public async Task<IReadOnlyDictionary<string, string>> UninstallAsync(IEnumerable<string> familyNames)
    {
        var results = new Dictionary<string, string>();
        foreach (var family in familyNames)
        {
            if (IsProtected(family))
            {
                results[family] = "Skipped: protected component";
                continue;
            }

            try
            {
                var pm = new PackageManager();
                var pkg = pm.FindPackages().FirstOrDefault(p => p.Id.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase));
                if (pkg is null)
                {
                    results[family] = "Not found";
                    continue;
                }

                await pm.RemovePackageAsync(pkg.Id.FullName);
                results[family] = "Removed";
                Optim.Core.Logging.FileLogger.Info($"Removed package {family}");
            }
            catch (Exception ex)
            {
                results[family] = $"Failed: {ex.Message}";
                Optim.Core.Logging.FileLogger.Error($"Uninstall {family}: {ex.Message}");
            }
        }

        return results;
    }
}
