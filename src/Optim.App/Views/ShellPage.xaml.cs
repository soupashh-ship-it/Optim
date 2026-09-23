using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Optim.App.Models;
using Optim.App.Services;

namespace Optim.App.Views;

/// <summary>Shell with custom title bar, global search, and route navigation.</summary>
public sealed partial class ShellPage : Page
{
    private static readonly Dictionary<string, Type> Routes = new()
    {
        ["home"] = typeof(HomePage),
        ["optimize"] = typeof(OptimizePage),
        ["privacy"] = typeof(PrivacyPage),
        ["features"] = typeof(FeaturesPage),
        ["debloat"] = typeof(DebloatPage),
        ["services"] = typeof(ServicesPage),
        ["processes"] = typeof(ProcessesPage),
        ["startup"] = typeof(StartupPage),
        ["packages"] = typeof(PackagesPage),
        ["network"] = typeof(NetworkPage),
        ["security"] = typeof(SecurityPage),
        ["policies"] = typeof(PoliciesPage),
        ["repair"] = typeof(RepairPage),
        ["device"] = typeof(DevicePage),
        ["settings"] = typeof(SettingsPage),
        ["about"] = typeof(AboutPage)
    };

    private static readonly Dictionary<string, string> PageTitles = new()
    {
        ["home"] = "Home",
        ["optimize"] = "System Optimizations",
        ["privacy"] = "Privacy",
        ["features"] = "Windows Features",
        ["debloat"] = "Debloat",
        ["services"] = "Services",
        ["processes"] = "Processes",
        ["startup"] = "Startup Apps",
        ["packages"] = "Packages",
        ["network"] = "Network",
        ["security"] = "Security",
        ["policies"] = "Group Policies",
        ["repair"] = "Repair",
        ["device"] = "Device",
        ["settings"] = "Settings",
        ["about"] = "About"
    };

    public ShellPage()
    {
        InitializeComponent();

        // Defer window wiring to Loaded: the window instance does not exist while
        // this page is being constructed inside its content tree.
        Loaded += (_, _) => WireTitleBar();
        Unloaded += (_, _) => ToastService.ToastRequested -= ShowToast;
        ToastService.ToastRequested += ShowToast;

        Nav.SelectedItem = Nav.MenuItems[0];
    }

    /// <summary>Renders a transient toast notification. Click dismisses early.</summary>
    private void ShowToast(string message, ToastKind kind)
    {
        try
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                while (ToastLayer.Children.Count >= 3)
                {
                    ToastLayer.Children.RemoveAt(0);
                }

                var accent = kind switch
                {
                    ToastKind.Success => Microsoft.UI.Colors.ForestGreen,
                    ToastKind.Warning => Microsoft.UI.Colors.DarkOrange,
                    ToastKind.Error => Microsoft.UI.Colors.OrangeRed,
                    _ => Microsoft.UI.Colors.DodgerBlue
                };

                var bar = new Border
                {
                    Width = 4,
                    CornerRadius = new CornerRadius(2, 0, 0, 2),
                    Background = new SolidColorBrush(accent)
                };

                var text = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                    VerticalAlignment = VerticalAlignment.Center
                };

                var inner = new Grid { ColumnSpacing = 10, Padding = new Thickness(10) };
                inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(bar, 0);
                Grid.SetColumn(text, 1);
                inner.Children.Add(bar);
                inner.Children.Add(text);

                var toast = new Border
                {
                    Style = (Style)Application.Current.Resources["CardBorderStyle"],
                    Padding = new Thickness(0),
                    Child = inner
                };
                toast.PointerPressed += (_, _) => ToastLayer.Children.Remove(toast);

                ToastLayer.Children.Add(toast);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    ToastLayer.Children.Remove(toast);
                };
                timer.Start();
            });
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Toast: {ex.Message}");
        }
    }

    private void WireTitleBar()
    {
        var window = App.CurrentWindow;
        if (window is null)
        {
            return;
        }

        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(AppTitleBar);
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item
            && item.Tag is string tag
            && Routes.TryGetValue(tag, out var pageType)
            && (ContentFrame.Content is null || ContentFrame.Content.GetType() != pageType))
        {
            ContentFrame.Navigate(pageType, PageTitles.GetValueOrDefault(tag, ""));
        }
    }

    /// <summary>Navigates programmatically (used by search and Home quick links).</summary>
    public void NavigateTo(string tag, string? highlightTweakId = null)
    {
        if (Routes.TryGetValue(tag, out var pageType))
        {
            object param = highlightTweakId is null
                ? PageTitles.GetValueOrDefault(tag, "")
                : new TweakPageArgs(PageTitles.GetValueOrDefault(tag, ""), highlightTweakId);

            var target = Nav.MenuItems.OfType<NavigationViewItem>()
                .Concat(Nav.FooterMenuItems.OfType<NavigationViewItem>())
                .FirstOrDefault(i => (string?)i.Tag == tag);
            if (target is not null)
            {
                Nav.SelectedItem = target;
            }

            // When highlighting a specific tweak, always (re)navigate so the
            // target page receives the highlight id even if already selected.
            // Otherwise keep the old guard to avoid reloading the same page.
            if (highlightTweakId is not null
                || ContentFrame.Content is null
                || ContentFrame.Content.GetType() != pageType)
            {
                ContentFrame.Navigate(pageType, param);
            }
            else if (target is null)
            {
                ContentFrame.Navigate(pageType, param);
            }
        }
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            sender.ItemsSource = SearchRouter.Query(sender.Text);
        }
    }

    private void Search_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SearchItem item)
        {
            NavigateTo(item.Route, item.TweakId);
            sender.Text = item.DisplayName;
            sender.IsSuggestionListOpen = false;
        }
    }

    private void Search_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is SearchItem item)
        {
            NavigateTo(item.Route, item.TweakId);
            return;
        }

        // Typed text + Enter with no chosen suggestion: jump to the top match.
        if (!string.IsNullOrWhiteSpace(args.QueryText))
        {
            var top = SearchRouter.Query(args.QueryText).FirstOrDefault();
            if (top is not null)
            {
                NavigateTo(top.Route, top.TweakId);
                sender.Text = top.DisplayName;
            }
        }
    }
}
