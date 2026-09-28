using Optim.Core.Tweaks;
using Xunit;

namespace Optim.Core.Tests;

public class TweakCatalogTests
{
    [Fact]
    public void Catalog_has_entries_for_every_category()
    {
        Assert.NotEmpty(TweakCatalog.For(TweakCategory.Optimize));
        Assert.NotEmpty(TweakCatalog.For(TweakCategory.Privacy));
        Assert.NotEmpty(TweakCatalog.For(TweakCategory.Features));
    }

    [Fact]
    public void All_tweak_ids_are_unique()
    {
        var ids = TweakCatalog.All.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Every_tweak_has_apply_and_revert_operations()
    {
        foreach (var tweak in TweakCatalog.All)
        {
            Assert.NotEmpty(tweak.Apply);
            Assert.NotEmpty(tweak.Revert);
        }
    }

    [Fact]
    public void Every_registry_operation_names_a_known_hive()
    {
        var known = new[] { RegistryOperation.HKLM, RegistryOperation.HKCU };
        foreach (var tweak in TweakCatalog.All)
        {
            Assert.All(tweak.Apply.Concat(tweak.Revert),
                op => Assert.Contains(op.Hive, known));
        }
    }

    [Fact]
    public void Advanced_tier_hides_a_subset_only()
    {
        var advanced = TweakCatalog.All.Where(t => t.IsAdvanced).ToList();
        var basic = TweakCatalog.All.Where(t => !t.IsAdvanced).ToList();
        Assert.NotEmpty(advanced);
        Assert.NotEmpty(basic);
        Assert.True(advanced.Count < basic.Count);
        foreach (var category in new[] { TweakCategory.Optimize, TweakCategory.Privacy, TweakCategory.Features })
        {
            Assert.Contains(TweakCatalog.For(category), t => !t.IsAdvanced);
        }
    }

    [Fact]
    public void Every_tweak_can_be_detected_against_the_live_system()
    {
        // The pages detect every tweak on load; Detect returns Unknown only when
        // the registry read throws. This is the catalog-wide equivalent of
        // opening each tuning page, without needing the UI.
        var engine = new RegistryTweakEngine(new ChangeJournal(
            Path.Combine(Path.GetTempPath(), "optim-catalog-probe.json")));
        foreach (var tweak in TweakCatalog.All)
        {
            Assert.True(engine.Detect(tweak) != TweakState.Unknown,
                $"{tweak.Id} could not be detected");
        }
    }

    [Fact]
    public void Every_registry_path_is_well_formed()
    {
        // Read-only: missing keys return null, malformed paths throw. Guards the
        // catalog (and future additions) against typos that would only surface as
        // runtime "Detect failed" noise.
        foreach (var op in TweakCatalog.All.SelectMany(t => t.Apply.Concat(t.Revert)))
        {
            Assert.False(string.IsNullOrWhiteSpace(op.KeyPath));
            var root = op.Hive == RegistryOperation.HKLM
                ? Microsoft.Win32.Registry.LocalMachine
                : Microsoft.Win32.Registry.CurrentUser;
            using var key = root.OpenSubKey(op.KeyPath);
        }
    }

    [Fact]
    public void Every_tweak_has_a_visible_default_state_source()
    {
        // Detect compares live values against the Apply operations, so each one
        // must name a value or explicitly delete one.
        foreach (var tweak in TweakCatalog.All)
        {
            Assert.All(tweak.Apply,
                op => Assert.True(op.DeleteKey || op.DeleteValue || op.Value is not null,
                    $"{tweak.Id} has an apply op with no value"));
        }
    }

    [Fact]
    public void DWord_operations_carry_integer_values()
    {
        foreach (var op in TweakCatalog.All.SelectMany(t => t.Apply.Concat(t.Revert)))
        {
            if (op.Kind == RegistryValueHint.DWord && !op.DeleteValue)
            {
                Assert.IsNotType<string>(op.Value);
            }
        }
    }

    [Fact]
    public void Impact_tiers_are_consistent()
    {
        Assert.Contains(TweakCatalog.All, t => t.Risk == TweakRisk.Bold);
        Assert.Contains(TweakCatalog.All, t => t.Risk == TweakRisk.Moderate);
        foreach (var tweak in TweakCatalog.All)
        {
            Assert.True(tweak.Points > 0);
            // Bold tweaks are never part of "apply suggested".
            Assert.True(tweak.Risk != TweakRisk.Bold || !tweak.Recommended);
        }
    }

    [Fact]
    public void Latency_tweaks_exist_with_reversible_pairs()
    {
        foreach (var id in new[] { "optimize.mouse-precision-off", "optimize.keyboard-fast-repeat", "optimize.fullscreen-optimizations-off" })
        {
            var tweak = Assert.Single(TweakCatalog.All, t => t.Id == id);
            Assert.NotEmpty(tweak.Apply);
            Assert.NotEmpty(tweak.Revert);
        }
    }

    [Fact]
    public void Catalog_is_broad_enough_to_cover_the_breadth_areas()
    {
        // Breadth target: comparable optimizers ship well over a hundred tweaks.
        Assert.True(TweakCatalog.All.Count >= 100,
            $"catalog only has {TweakCatalog.All.Count} tweaks");

        foreach (var category in new[] { TweakCategory.Optimize, TweakCategory.Privacy, TweakCategory.Features })
        {
            Assert.True(TweakCatalog.For(category).Count() >= 20,
                $"{category} only has {TweakCatalog.For(category).Count()} tweaks");
        }

        var sections = TweakCatalog.All.Select(t => t.Section).ToHashSet(StringComparer.Ordinal);
        foreach (var expected in new[]
                 {
                     "Gaming & Input", "Visual Speed", "Memory & Storage", "Network",
                     "Diagnostics & Feedback", "Tracking & Ads", "Devices & Cameras",
                     "AI & Copilot", "Windows Update", "Explorer & Desktop"
                 })
        {
            Assert.Contains(expected, sections);
        }
    }

    [Fact]
    public void Every_tweak_belongs_to_a_known_section()
    {
        foreach (var tweak in TweakCatalog.All)
        {
            Assert.False(string.IsNullOrEmpty(tweak.Section));
            Assert.NotEqual(int.MaxValue, TweakCatalog.SectionIndex(tweak.Category, tweak.Section));
        }
    }
}
