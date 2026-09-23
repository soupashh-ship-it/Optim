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
    public void Every_tweak_belongs_to_a_known_section()
    {
        foreach (var tweak in TweakCatalog.All)
        {
            Assert.False(string.IsNullOrEmpty(tweak.Section));
            Assert.NotEqual(int.MaxValue, TweakCatalog.SectionIndex(tweak.Category, tweak.Section));
        }
    }
}
