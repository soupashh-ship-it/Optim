using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Optim.App.Services;
using Optim.Core.SystemDeployment;
using Optim.Core.Tweaks;

namespace Optim.App.Views;

/// <summary>
/// Registry feature tweaks (catalog) plus real DISM optional-feature toggles
/// (the classic "Turn Windows features on or off" surface).
/// </summary>
public sealed partial class FeaturesPage : TweakListPage
{
    private bool _featureOpRunning;
    private TextBox? _featureSearch;
    private TextBlock? _featureStatus;
    private StackPanel? _featureList;
    private List<OptionalFeature> _features = new();

    /// <summary>
    /// One built card per feature, kept so the search box filters by visibility.
    /// Rebuilding ~150 settings cards on every keystroke was the most expensive
    /// thing this page did.
    /// </summary>
    private readonly List<(OptionalFeature Feature, FrameworkElement Card)> _featureCards = new();

    public FeaturesPage()
    {
        InitializeComponent();
        Content = TweakContent;
    }

    protected override TweakCategory Category => TweakCategory.Features;

    protected override IEnumerable<FrameworkElement> BuildFooter()
    {
        var header = new TextBlock
        {
            Text = "Optional features (DISM)",
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            Margin = new Thickness(0, 16, 0, 4)
        };
        var desc = new TextBlock
        {
            Text = "The classic \"Turn Windows features on or off\" surface. Changes apply immediately; some need a restart.",
            Style = (Style)Application.Current.Resources["CardDescStyle"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };

        _featureSearch = new TextBox
        {
            PlaceholderText = "Search features…",
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _featureSearch.TextChanged += (_, _) => ApplyFeatureFilter();

        _featureStatus = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.7,
            Margin = new Thickness(0, 0, 0, 8),
            Text = "Loading optional features…"
        };

        _featureList = new StackPanel { Spacing = 0 };

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(desc);
        panel.Children.Add(_featureSearch);
        panel.Children.Add(_featureStatus);
        panel.Children.Add(_featureList);

        _ = LoadFeaturesAsync();

        yield return panel;
    }

    private async Task LoadFeaturesAsync()
    {
        if (_featureOpRunning || _featureStatus is null)
        {
            return;
        }

        _featureOpRunning = true;
        try
        {
            // DISM enumeration takes seconds: keep it off the UI thread. The
            // result is kept even if the user switched tabs while it ran, since
            // the page is cached: discarding it here meant paying those seconds
            // again on the next visit.
            var features = await Task.Run(DismFeatureEngine.ListFeatures);

            _features = features.ToList();
            _featureStatus!.Text = TuneKit.Count(_features.Count, "optional feature", "optional features")
                + $" · {_features.Count(f => f.IsEnabled)} enabled";
            BuildFeatureCards();
            ApplyFeatureFilter();
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"DISM list: {ex.Message}");
            if (_featureStatus is not null)
            {
                _featureStatus.Text = "Could not enumerate optional features — run Optim elevated and check logs.";
            }
        }
        finally
        {
            _featureOpRunning = false;
        }
    }

    /// <summary>
    /// Builds one card per feature. Called only when the feature list itself
    /// changes (load, or a re-read after a DISM toggle) - never per keystroke.
    /// </summary>
    private void BuildFeatureCards()
    {
        if (_featureList is null)
        {
            return;
        }

        _featureList.Children.Clear();
        _featureCards.Clear();
        foreach (var feature in _features)
        {
            var card = BuildFeatureCard(feature);
            _featureCards.Add((feature, card));
            _featureList.Children.Add(card);
        }
    }

    /// <summary>
    /// Applies the search box by toggling card visibility. Hiding a card is a
    /// property change; rebuilding it means a fresh SettingsCard template, icon
    /// and toggle for every one of ~150 rows while the user is typing.
    /// </summary>
    private void ApplyFeatureFilter()
    {
        var query = _featureSearch?.Text?.Trim() ?? string.Empty;
        foreach (var (feature, card) in _featureCards)
        {
            var matches = query.Length == 0
                || feature.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || feature.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || feature.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
            card.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private FrameworkElement BuildFeatureCard(OptionalFeature feature)
    {
        var title = new TextBlock { Text = feature.DisplayName, Style = CardTitleStyle };
        var desc = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(feature.Description) ? feature.Name : feature.Description,
            Style = CardDescStyle,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(desc);

        var state = new TextBlock
        {
            Text = feature.StateText,
            Style = CardDescStyle,
            Opacity = 0.8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };

        var toggle = new ToggleSwitch
        {
            IsOn = feature.IsEnabled,
            Style = ToggleStyle,
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Toggled += (_, _) => _ = ToggleFeatureAsync(feature, toggle);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(state);
        actions.Children.Add(toggle);

        return new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush = TuneKit.TransparentBrush,
            CornerRadius = new CornerRadius(8),
            Child = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header = text,
                Content = actions,
                IsClickEnabled = false
            }
        };
    }

    private async Task ToggleFeatureAsync(OptionalFeature feature, ToggleSwitch toggle)
    {
        if (XamlRoot is null)
        {
            toggle.IsOn = feature.IsEnabled;
            return;
        }

        if (_featureOpRunning)
        {
            // One DISM operation at a time; snap the visual back.
            toggle.IsOn = feature.IsEnabled;
            return;
        }

        var wanted = toggle.IsOn;
        if (wanted == feature.IsEnabled)
        {
            return; // render pass, not a user action
        }

        var verb = wanted ? "Enable" : "Disable";
        var confirm = new ContentDialog
        {
            Title = $"{verb} '{feature.DisplayName}'?",
            Content = feature.Name
                + (wanted
                    ? "\n\nWindows may need to download files or restart to finish."
                    : "\n\nApps or server roles depending on this feature may stop working."),
            PrimaryButtonText = verb,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            toggle.IsOn = feature.IsEnabled;
            return;
        }

        _featureOpRunning = true;
        try
        {
            if (_featureStatus is not null)
            {
                _featureStatus.Text = $"{(wanted ? "Enabling" : "Disabling")} '{feature.DisplayName}'… (DISM can take a while)";
            }

            var (ok, detail, restart) = await Task.Run(() =>
                wanted
                    ? DismFeatureEngine.Enable(feature.Name)
                    : DismFeatureEngine.Disable(feature.Name));

            if (ok)
            {
                feature = feature with { IsEnabled = wanted, RestartRequired = restart };
                ToastService.Show(detail, restart ? ToastKind.Warning : ToastKind.Success);
                // Re-read the real state rather than trusting the toggle.
                _ = LoadFeaturesAsync();
            }
            else
            {
                toggle.IsOn = feature.IsEnabled;
                ToastService.Show(detail, ToastKind.Error);
                if (_featureStatus is not null)
                {
                    _featureStatus.Text = detail;
                }
            }
        }
        finally
        {
            _featureOpRunning = false;
        }
    }
}
