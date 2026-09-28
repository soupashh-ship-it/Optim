namespace Optim.Core.Tests;

/// <summary>
/// Locates repository files from the test output folder so tests can assert
/// against source-controlled assets (the resource file) without hardcoding an
/// absolute path.
/// </summary>
internal static class RepoPaths
{
    /// <summary>Walks up from the test binaries until it finds the solution.</summary>
    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Optim.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException($"Could not find the repo root above {AppContext.BaseDirectory}.");
    }

    /// <summary>The neutral English resource file shipped with the app.</summary>
    public static string ResourcesResw() => Path.Combine(
        Root(), "src", "Optim.App", "Strings", "en-US", "Resources.resw");

    /// <summary>
    /// Resource key fragment for a tweak id. Dots become underscores so the only
    /// dot left in a key is the resource/property separator MRT expects.
    /// </summary>
    public static string Key(string tweakId) => tweakId.Replace('.', '_');
}
