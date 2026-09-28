namespace Optim.Core.Tweaks;

/// <summary>
/// Single source of the resource-key scheme for tweak strings, shared by the
/// generator, the app and the tests so the three can never disagree.
/// </summary>
public static class TweakResourceKeys
{
    public const string Prefix = "Tweak_";
    public const string TitleSuffix = ".Title";
    public const string DescriptionSuffix = ".Description";

    /// <summary>Dots become underscores: the only dot left is the MRT separator.</summary>
    public static string Sanitize(string tweakId) => tweakId.Replace('.', '_');

    public static string Title(string tweakId) => Prefix + Sanitize(tweakId) + TitleSuffix;

    public static string Description(string tweakId) => Prefix + Sanitize(tweakId) + DescriptionSuffix;
}
