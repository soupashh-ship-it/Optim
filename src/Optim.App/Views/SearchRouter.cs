using Optim.App.Localization;
using Optim.App.Models;
using Optim.Core.Network;
using Optim.Core.Repair;
using Optim.Core.ServicesMgmt;
using Optim.Core.Tweaks;

namespace Optim.App.Views;

/// <summary>
/// Routes search hits to the right page. Catalog tweaks are indexed by title
/// AND description, plus page actions (repair tools, DNS profiles, vetted
/// services, settings operations) so a query like "telemetry" or "battery"
/// jumps straight to the right place.
/// </summary>
public static class SearchRouter
{
    private static IReadOnlyList<SearchItem>? _index;

    public static IReadOnlyList<SearchItem> Query(string text)
    {
        var index = _index ??= BuildIndex();
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<SearchItem>();
        }

        return index
            .Where(i => i.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase)
                      || i.Category.Contains(text, StringComparison.OrdinalIgnoreCase)
                      || i.Keywords.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.DisplayName.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToList();
    }

    private static IReadOnlyList<SearchItem> BuildIndex()
    {
        var items = new List<SearchItem>();

        (string Name, string Route, string Glyph)[] pages =
        {
            ("Home", "home", "\uE80F"),
            ("Quick Tweaks", "quicktweaks", "\uE7B7"),
            ("Optimize", "optimize", "\uF259"),
            ("Privacy", "privacy", "\uE7B3"),
            ("Features", "features", "\uE74C"),
            ("Debloat", "debloat", "\uE74D"),
            ("Services", "services", "\uE9F5"),
            ("Processes", "processes", "\uECAA"),
            ("Startup apps", "startup", "\uE7B8"),
            ("Packages", "packages", "\uE896"),
            ("Network", "network", "\uE968"),
            ("Security", "security", "\uEA18"),
            ("Group Policies", "policies", "\uE9D5"),
            ("Repair", "repair", "\uE90F"),
            ("Device", "device", "\uE770"),
            ("Settings", "settings", "\uE713"),
            ("About", "about", "\uE946")
        };

        foreach (var (name, route, glyph) in pages)
        {
            items.Add(new SearchItem(name, "Page", glyph, route));
        }

        foreach (var tweak in TweakCatalog.All)
        {
            var route = tweak.Category switch
            {
                TweakCategory.Optimize => "optimize",
                TweakCategory.Privacy => "privacy",
                _ => "features"
            };
            // Same localized strings the rows render, with the catalog text as
            // the English fallback, so search matches what the user sees.
            var title = Loc.Get(TweakResourceKeys.Title(tweak.Id), tweak.Title);
            var description = Loc.Get(TweakResourceKeys.Description(tweak.Id), tweak.Description);
            items.Add(new SearchItem(title, tweak.Category.ToString(), "\uE713", route, tweak.Id, description));
        }

        // Page actions: static shortcuts that route to the owning page.
        foreach (var action in RepairEngine.Catalog)
        {
            items.Add(new SearchItem(action.Title, "Repair action", "\uE90F", "repair", Keywords: action.Description));
        }
        foreach (var profile in NetworkEngine.KnownProfiles)
        {
            items.Add(new SearchItem($"{profile.Name} DNS", "Network", "\uE968", "network"));
        }
        foreach (var name in ServiceEngine.VettedNames)
        {
            items.Add(new SearchItem(name, "Service", "\uE9F5", "services"));
        }
        foreach (var preset in TweakPresets.All)
        {
            items.Add(new SearchItem(
                $"{preset.Title} preset", "Quick Tweaks", "\uE7B7", "quicktweaks",
                Keywords: $"bundle preset apply {preset.Title} quick tweaks"));
        }

        (string Title, string Category, string Route, string Keywords)[] shortcuts =
        {
            ("Revert all changes", "Settings", "settings", "undo rollback restore journal"),
            ("Create restore point", "Settings", "settings", "backup snapshot protection"),
            ("Export settings", "Settings", "settings", "backup save json"),
            ("Import settings", "Settings", "settings", "load restore json"),
            ("Flush DNS", "Network", "network", "dns cache ipconfig"),
            ("Defender quick scan", "Security", "security", "antivirus malware scan"),
            ("Update Defender definitions", "Security", "security", "antivirus signatures update"),
            ("Export drivers", "Device", "device", "backup pnputil"),
            ("Check for package updates", "Packages", "packages", "winget upgrade list"),
            ("Apply suggested tweaks", "Optimize", "optimize", "recommended tune score"),
            ("End task", "Processes", "processes", "kill process task manager"),
        };
        foreach (var (title, category, route, keywords) in shortcuts)
        {
            items.Add(new SearchItem(title, category, "\uE713", route, Keywords: keywords));
        }

        return items;
    }
}
