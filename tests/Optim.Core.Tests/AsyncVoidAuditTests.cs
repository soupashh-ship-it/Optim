using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Optim.Core.Tests;

/// <summary>
/// Hard gate for the async-void crash class. Every crash post-mortem in this
/// repo traced back to an exception escaping an async void event handler, so
/// the rule is: async void methods are allowed only where they are explicitly
/// audited here. A new async void handler fails this test until it is either
/// wrapped in the standard guard or added to the audited set with eyes on it.
///
/// The scan parses src/**/*.cs with Roslyn (precise namespace/type nesting,
/// no regex guessing) because the test project deliberately does not reference
/// the WinUI app assembly — loading it into the test host would drag WinUI
/// dependencies in.
/// </summary>
public class AsyncVoidAuditTests
{
    private static readonly string SourceRoot = Path.Combine(RepoPaths.Root(), "src");

    /// <summary>
    /// Async void methods already reviewed: each one wraps its whole body in a
    /// try/catch that logs (and toasts where user-facing) or no-ops safely
    /// without a XamlRoot. When you add a handler, wrap it and then list it
    /// here with the guard it relies on.
    /// </summary>
    private static readonly HashSet<string> Audited = new(StringComparer.Ordinal)
    {
        // The guard itself: Safe.Run's entire body is the standard try/catch
        // every other handler relies on, so this is the one async void that is
        // allowed to exist outside page code.
        // TweakListPage: all four wrap their bodies or no-op without XamlRoot.
        "Optim.App.Views.TweakListPage.ApplySuggested_Click",
        "Optim.App.Views.TweakListPage.Preview_Click",
        "Optim.App.Views.TweakListPage.RollbackCategory_Click",
        "Optim.App.Views.TweakListPage.ShowTweakInfo",

        // Home: guarded bodies; the metrics ticker is wrapped in try/catch.
        "Optim.App.Views.HomePage.FlushDns_Click",
        "Optim.App.Views.HomePage.RestorePoint_Click",
        "Optim.App.Views.HomePage.CleanTemp_Click",
        "Optim.App.Views.HomePage.EmptyRecycleBin_Click",

        // Settings: journal revert, import/export and log helpers, all guarded.
        "Optim.App.Views.SettingsPage.RevertAll_Click",
        "Optim.App.Views.SettingsPage.OpenLogs_Click",
        "Optim.App.Views.SettingsPage.RestorePoint_Click",
        "Optim.App.Views.SettingsPage.Export_Click",
        "Optim.App.Views.SettingsPage.Import_Click",
        "Optim.App.Views.SettingsPage.ViewLogs_Click",
        "Optim.App.Views.SettingsPage.Language_SelectionChanged",

        // Packages: scan/upgrade/search/install with CTS + guarded bodies.
        "Optim.App.Views.PackagesPage.Scan_Click",
        "Optim.App.Views.PackagesPage.Upgrade_Click",
        "Optim.App.Views.PackagesPage.Search_Click",
        "Optim.App.Views.PackagesPage.Install_Click",

        // Services/Startup/Processes/Debloat loaders and handlers, guarded.
        "Optim.App.Views.ServicesPage.Load",
        "Optim.App.Views.ServicesPage.Mode_SelectionChanged",
        "Optim.App.Views.StartupPage.Load",
        "Optim.App.Views.StartupPage.Add_Click",
        "Optim.App.Views.ProcessesPage.Load",
        "Optim.App.Views.ProcessesPage.EndTask_Click",
        "Optim.App.Views.ProcessesPage.Priority_Click",
        "Optim.App.Views.ProcessesPage.Affinity_Click",
        "Optim.App.Views.DebloatPage.Refresh_Click",
        "Optim.App.Views.DebloatPage.Uninstall_Click",
        "Optim.App.Views.DebloatPage.Reinstall_Click",

        // Network/Device/Security/Policies/Repair/About handlers, guarded.
        "Optim.App.Views.NetworkPage.RefreshAdapters",
        "Optim.App.Views.NetworkPage.ApplyDns_Click",
        "Optim.App.Views.NetworkPage.ResetDns_Click",
        "Optim.App.Views.NetworkPage.ApplyCustomDns_Click",
        "Optim.App.Views.NetworkPage.ToggleAdapter_Click",
        "Optim.App.Views.NetworkPage.Flush_Click",
        "Optim.App.Views.DevicePage.Refresh_Click",
        "Optim.App.Views.DevicePage.ExportDrivers_Click",
        "Optim.App.Views.SecurityPage.OpenDefender_Click",
        "Optim.App.Views.SecurityPage.Scan_Click",
        "Optim.App.Views.SecurityPage.UpdateDefs_Click",
        "Optim.App.Views.PoliciesPage.Scan_Click",
        "Optim.App.Views.PoliciesPage.Remove_Click",
        "Optim.App.Views.PoliciesPage.Export_Click",
        "Optim.App.Views.RepairPage.Run_Click",
        "Optim.App.Views.AboutPage.RestorePoint_Click",
        "Optim.App.Views.AboutPage.OpenLogs_Click",
    };

    static AsyncVoidAuditTests()
    {
        Audited.Add("Optim.App.Services.Safe.Run");
    }

    [Fact]
    public void Every_async_void_method_is_explicitly_audited()
    {
        var violations = Scan().Where(m => !Audited.Contains(m.FullName)).ToList();
        Assert.True(violations.Count == 0,
            "Unaudited async void methods found — wrap the body in the standard " +
            "guard (log + toast for user actions) or, after review, add the method " +
            "to the audited set in AsyncVoidAuditTests:\n  "
            + string.Join("\n  ", violations.Select(v => $"{v.FullName}  ({v.File})")));
    }

    [Fact]
    public void Audited_list_stays_accurate()
    {
        // The audited set must name real methods: stale entries would let a
        // rename sneak a genuinely unguarded method past the first test.
        var present = Scan().Select(m => m.FullName).ToHashSet(StringComparer.Ordinal);
        var stale = Audited.Where(id => !present.Contains(id)).ToList();
        Assert.True(stale.Count == 0,
            "Audited entries no longer exist (methods were removed or renamed):\n  "
            + string.Join("\n  ", stale));
    }

    internal static List<(string FullName, string File)> Scan()
    {
        var found = new List<(string, string)>();
        foreach (var file in Directory.EnumerateFiles(SourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            if (normalized.Contains("/obj/") || normalized.Contains("/bin/"))
            {
                continue;
            }

            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
            var root = tree.GetCompilationUnitRoot();

            // File-scoped and block-scoped namespaces both resolve correctly.
            var ns = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString()
                     ?? root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString()
                     ?? "?";

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                         .Where(m => m.Modifiers.Any(ModifierIsAsync)
                                     && m.ReturnType is PredefinedTypeSyntax { Keyword.ValueText: "void" }))
            {
                var container = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                var type = container is null ? "?" : container.Identifier.Text;
                found.Add(($"{ns}.{type}.{method.Identifier.Text}", normalized));
            }
        }

        return found;
    }

    private static bool ModifierIsAsync(SyntaxToken t) => t.IsKind(SyntaxKind.AsyncKeyword);
}
