namespace Optim.Core.Tweaks;

/// <summary>A named bundle of catalog tweaks that can be applied and reverted together.</summary>
public sealed record TweakPreset(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<string> TweakIds);

/// <summary>
/// Curated tweak bundles shown on the Quick Tweaks page. Presets are derived
/// from the catalog's own tiers (Recommended / Gentle / Moderate / Advanced)
/// instead of a second hand-maintained id list, so a catalog edit flows into
/// every preset automatically.
/// The preset-driven UX is inspired by popular community optimizers; every
/// definition, bundle and string here is original.
/// </summary>
public static class TweakPresets
{
    /// <summary>
    /// The gentlest possible set: privacy essentials only, nothing advanced.
    /// Suitable for a family PC or anyone who wants value with near-zero risk.
    /// </summary>
    public static TweakPreset Minimal { get; } = new(
        "minimal",
        "Minimal",
        "The gentlest set: recommended privacy essentials only. Nothing advanced, nothing that changes how the system feels — safe for any PC.",
        Ids(t => t.Category == TweakCategory.Privacy
            && t.Recommended
            && t.Risk == TweakRisk.Gentle
            && !t.IsAdvanced));

    /// <summary>
    /// Balanced defaults for most users: every recommended gentle tweak across
    /// performance and privacy.
    /// </summary>
    public static TweakPreset Standard { get; } = new(
        "standard",
        "Standard",
        "Balanced defaults for most users: every recommended gentle performance and privacy tweak. This is the set Optim's tune score is built around.",
        Ids(t => (t.Category == TweakCategory.Optimize || t.Category == TweakCategory.Privacy)
            && t.Recommended
            && t.Risk == TweakRisk.Gentle
            && !t.IsAdvanced));

    /// <summary>
    /// Deep tweaks for power users: everything recommended plus the moderate
    /// tier (service tuning, network throttling, indexing, …). Bold tweaks
    /// (hardware-dependent) stay excluded on purpose.
    /// </summary>
    public static TweakPreset Advanced { get; } = new(
        "advanced",
        "Advanced",
        "Deep tweaks for power users: everything in Standard plus the moderate tier — service tuning, network throttling, indexing and friends. Hardware-dependent bold tweaks stay excluded.",
        Ids(t => (t.Category == TweakCategory.Optimize || t.Category == TweakCategory.Privacy)
            && (t.Recommended || t.Risk == TweakRisk.Moderate)));

    public static IReadOnlyList<TweakPreset> All { get; } = new[] { Minimal, Standard, Advanced };

    private static IReadOnlyList<string> Ids(Func<TweakDefinition, bool> predicate) =>
        TweakCatalog.All.Where(predicate).Select(t => t.Id).ToList();
}
