using System.Xml;
using Optim.Core.Tweaks;
using Xunit;

namespace Optim.Core.Tests;

/// <summary>
/// Keeps the Tweak_* strings in Strings/en-US/Resources.resw in sync with
/// TweakCatalog. The catalog is the source of truth; the resource file is
/// generated from it so the strings the UI falls back to can never drift from
/// the definitions that render the rows.
///
/// Regenerate after catalog edits by running:
///   pwsh scripts/generate-tweak-resw.ps1
/// which sets OPTIM_SYNC_RESW and runs the Regenerate test below. Without that
/// variable the generator test is a no-op so normal test runs never mutate
/// source files.
/// </summary>
public class TweakResourceSyncTests
{
    private static string ReswPath => RepoPaths.ResourcesResw();

    [Fact]
    public void Resw_is_in_sync_with_the_catalog()
    {
        var (added, updated, removed) = Sync(write: false);
        Assert.True(added == 0 && updated == 0 && removed == 0,
            $"Resources.resw is stale: {added} missing, {updated} outdated, {removed} orphaned Tweak_ entries. " +
            "Run pwsh scripts/generate-tweak-resw.ps1 and commit the result.");
    }

    [Fact]
    public void Regenerate_the_resw_when_explicitly_requested()
    {
        if (Environment.GetEnvironmentVariable("OPTIM_SYNC_RESW") != "1")
        {
            return; // normal test run: never touch source files
        }

        var (added, updated, removed) = Sync(write: true);
        Assert.True(added + updated + removed > 0 || true,
            $"Resources.resw regenerated: {added} added, {updated} updated, {removed} removed.");
    }

    /// <summary>Every tweak needs a Title and Description entry; nothing else may carry the Tweak_ prefix.</summary>
    internal static (int Added, int Updated, int Removed) Sync(bool write)
    {
        var doc = new XmlDocument();
        doc.Load(ReswPath);

        var existing = new Dictionary<string, XmlElement>(StringComparer.Ordinal);
        foreach (var node in doc.SelectNodes("/root/data")!.Cast<XmlNode>())
        {
            if (node is XmlElement el && el.GetAttribute("name").StartsWith("Tweak_", StringComparison.Ordinal))
            {
                existing[el.GetAttribute("name")] = el;
            }
        }

        var wanted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tweak in TweakCatalog.All)
        {
            wanted[TweakResourceKeys.Title(tweak.Id)] = tweak.Title;
            wanted[TweakResourceKeys.Description(tweak.Id)] = tweak.Description;
        }

        var added = 0;
        var updated = 0;

        if (write)
        {
            foreach (var (key, value) in wanted.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (existing.TryGetValue(key, out var element))
                {
                    var valueNode = element.GetElementsByTagName("value").Cast<XmlElement>().FirstOrDefault();
                    if (valueNode is null)
                    {
                        valueNode = doc.CreateElement("value");
                        _ = element.AppendChild(valueNode);
                    }

                    if (valueNode.InnerText != value)
                    {
                        valueNode.InnerText = value;
                        updated++;
                    }
                }
                else
                {
                    var data = doc.CreateElement("data");
                    data.SetAttribute("name", key);
                    data.SetAttribute("xml:space", "preserve");
                    var valueNode = doc.CreateElement("value");
                    valueNode.InnerText = value;
                    _ = data.AppendChild(valueNode);
                    _ = doc.DocumentElement!.AppendChild(data);
                    added++;
                }
            }
        }
        else
        {
            // Dry run: compute the same counts without touching the document.
            foreach (var (key, value) in wanted)
            {
                if (!existing.TryGetValue(key, out var element))
                {
                    added++;
                }
                else
                {
                    var text = element.GetElementsByTagName("value").Cast<XmlElement>().FirstOrDefault()?.InnerText;
                    if (text != value)
                    {
                        updated++;
                    }
                }
            }
        }

        var removed = 0;
        if (write)
        {
            foreach (var name in existing.Keys.Where(n => !wanted.ContainsKey(n)).ToList())
            {
                _ = doc.DocumentElement!.RemoveChild(existing[name]);
                removed++;
            }

            if (added + updated + removed > 0)
            {
                using var writer = XmlWriter.Create(ReswPath, new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    NewLineChars = "\n",
                    Async = false
                });
                doc.Save(writer);
            }
        }
        else
        {
            removed = existing.Keys.Count(n => !wanted.ContainsKey(n));
        }

        return (added, updated, removed);
    }
}
