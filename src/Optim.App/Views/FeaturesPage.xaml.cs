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
    private bool _featuresAlive = true;
    private bool _featureOpRunning;
    private TextBox? _featureSearch;
    private TextBlock? _featureStatus;
    private StackPanel? _featureList;
    private List<OptionalFeature> _features = new();

    public FeaturesPage()
    {
        InitializeComponent();
        Content = TweakContent;
    }

    protected override TweakCategory Category => TweakCategory.Features;

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        // Cached page: Unloaded fires on every tab switch; re-arm here.
        _featuresAlive = true;
        base.OnNavigatedTo(e);
    }

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
        _featureSearch.TextChanged += (_, _) => RenderFeatureList();

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

        // The base class keeps its own alive flag; this one guards the footer's async loads.
        Unloaded += (_, _) => _featuresAlive = false;

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
            // DISM enumeration takes seconds: background thread, then render
            // in batches so the page stays responsive.
            var features = await Task.Run(DismFeatureEngine.ListFeatures);
            if (!_featuresAlive)
            {
                return;
            }

            _features = features.ToList();
            _featureStatus!.Text = TuneKit.Count(_features.Count, "optional feature", "optional features")
                + $" · {_features.Count(f => f.IsEnabled)} enabled";
            RenderFeatureList();
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"DISM list: {ex.Message}");
            if (_featureStatus is not null && _featuresAlive)
            {
                _featureStatus.Text = "Could not enumerate optional features — run Optim elevated and check logs.";
            }
        }
        finally
        {
            _featureOpRunning = false;
        }
    }

    /// <summary>Renders the feature cards, honoring the search box. Rebuilt per call: cheap at ~150 rows.</summary>
    private void RenderFeatureList()
    {
        if (_featureList is null)
        {
            return;
        }

        _featureList.Children.Clear();
        var query = _featureSearch?.Text?.Trim() ?? string.Empty;
        var visible = 0;
        foreach (var feature in _features)
        {
            if (query.Length > 0
                && !feature.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !feature.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !feature.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _featureList.Children.Add(BuildFeatureCard(feature));
            visible++;
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
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
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
