namespace Optim.Core.Tweaks;

/// <summary>Logical grouping of tweaks, one per tuning page.</summary>
public enum TweakCategory
{
    Optimize,
    Privacy,
    Features
}

/// <summary>Whether a tweak is currently applied to the system.</summary>
public enum TweakState
{
    Applied,
    NotApplied,
    Unknown
}

/// <summary>The registry value kinds the engine knows how to write and journal.</summary>
public enum RegistryValueHint
{
    String,
    ExpandString,
    MultiString,
    DWord,
    QWord,
    Binary
}

/// <summary>
/// A single registry mutation. <see cref="ValueName"/> null means the default value.
/// When <see cref="DeleteValue"/> is true the value is removed instead of written.
/// </summary>
public sealed record RegistryOperation(
    string Hive,
    string KeyPath,
    string? ValueName,
    RegistryValueHint Kind,
    object? Value,
    bool DeleteValue = false,
    bool DeleteKey = false)
{
    public const string HKLM = "HKLM";
    public const string HKCU = "HKCU";
}

/// <summary>
/// How much care a tweak needs. Optim's own impact language:
/// Gentle (safe for everyone), Moderate (know what it does), Bold (experts only).
/// </summary>
public enum TweakRisk
{
    Gentle,
    Moderate,
    Bold
}

/// <summary>
/// A declarative tuning definition. The UI, journaling and revert logic are
/// generic; adding a tweak means adding one of these records to the catalog.
/// </summary>
public sealed record TweakDefinition(
    string Id,
    TweakCategory Category,
    string Title,
    string Description,
    IReadOnlyList<RegistryOperation> Apply,
    IReadOnlyList<RegistryOperation> Revert,
    bool RestartRequired = false,
    bool IsAdvanced = false,
    TweakRisk Risk = TweakRisk.Gentle,
    int Points = 5,
    bool Recommended = true,
    string Section = "");
