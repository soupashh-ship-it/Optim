using System.ComponentModel;
using System.Runtime.CompilerServices;
using Optim.App.Localization;
using Optim.Core.Tweaks;

namespace Optim.App.Models;

/// <summary>
/// Row view-model for a catalog tweak. Two-way bound to a ToggleSwitch;
/// the IsOn setter applies or reverts the tweak through the engine.
/// </summary>
public sealed class TweakRow : INotifyPropertyChanged
{
    private readonly RegistryTweakEngine _engine;
    private bool _isOn;
    private bool _initializing = true;

    public TweakRow(TweakDefinition definition, RegistryTweakEngine engine, bool? initialIsOn = null)
    {
        Definition = definition;
        _engine = engine;
        // Callers that pre-detect state off the UI thread pass it in so page
        // navigation never blocks on registry reads.
        _isOn = initialIsOn ?? engine.Detect(definition) == TweakState.Applied;
        _initializing = false;
    }

    public TweakDefinition Definition { get; }

    // Catalog text is the English fallback: a missing resource key degrades to
    // the original wording instead of an empty row, and a translated file
    // serves the localized strings without any code change.
    public string Title => Loc.Get(
        TweakResourceKeys.Title(Definition.Id), Definition.Title);

    public string Description => Loc.Get(
        TweakResourceKeys.Description(Definition.Id), Definition.Description);
    public string RestartNote => Definition.RestartRequired ? "Restart required" : string.Empty;
    public bool HasRestartNote => Definition.RestartRequired;
    public bool IsAdvanced => Definition.IsAdvanced;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_initializing || _isOn == value)
            {
                return;
            }

            try
            {
                if (value)
                {
                    _engine.Apply(Definition);
                }
                else
                {
                    _engine.Revert(Definition);
                }

                _isOn = value;
            }
            catch (Exception ex)
            {
                Optim.Core.Logging.FileLogger.Error($"Tweak {Definition.Id}: {ex.Message}");
            }

            OnPropertyChanged();
        }
    }

    /// <summary>Re-reads the live state (used after bulk or journal operations).</summary>
    public void ResnapFromSystem()
    {
        _isOn = _engine.Detect(Definition) == TweakState.Applied;
        OnPropertyChanged(nameof(IsOn));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? ""));
}
