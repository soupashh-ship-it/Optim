namespace Optim.App.Models;

/// <summary>A searchable entry in the title-bar search index. Keywords are
/// matched but never displayed (descriptions, synonyms, action names).</summary>
public sealed record SearchItem(string DisplayName, string Category, string Glyph, string Route, string? TweakId = null, string Keywords = "");

/// <summary>Navigation payload for catalog pages: title plus optional tweak to highlight.</summary>
public sealed record TweakPageArgs(string Title, string? HighlightTweakId = null);
