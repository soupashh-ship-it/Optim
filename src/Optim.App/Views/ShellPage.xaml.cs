using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Optim.App.Localization;
using Optim.App.Models;
using Optim.App.Services;

namespace Optim.App.Views;

/// <summary>Shell with custom title bar, global search, and route navigation.</summary>
public sealed partial class ShellPage : Page
{
    private static readonly Dictionary<string, Type> Routes = new()
    {
        ["home"] = typeof(HomePage),
        ["quicktweaks"] = typeof(QuickTweaksPage),
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

    /// <summary>
    /// Page headings, resolved from resources. The English text is the fallback
    /// so a missing key degrades to today's wording instead of a blank header.
    /// </summary>
    private static readonly Dictionary<string, string> PageTitles = new()
    {
        ["home"] = Loc.Get("Page_Home", "Home"),
        ["quicktweaks"] = Loc.Get("Page_QuickTweaks", "Quick Tweaks"),
        ["optimize"] = Loc.Get("Page_Optimize", "System Optimizations"),
        ["privacy"] = Loc.Get("Page_Privacy", "Privacy"),
        ["features"] = Loc.Get("Page_Features", "Windows Features"),
        ["debloat"] = Loc.Get("Page_Debloat", "Debloat"),
        ["services"] = Loc.Get("Page_Services", "Services"),
        ["processes"] = Loc.Get("Page_Processes", "Processes"),
        ["startup"] = Loc.Get("Page_Startup", "Startup Apps"),
        ["packages"] = Loc.Get("Page_Packages", "Packages"),
        ["network"] = Loc.Get("Page_Network", "Network"),
        ["security"] = Loc.Get("Page_Security", "Security"),
        ["policies"] = Loc.Get("Page_Policies", "Group Policies"),
        ["repair"] = Loc.Get("Page_Repair", "Repair"),
        ["device"] = Loc.Get("Page_Device", "Device"),
        ["settings"] = Loc.Get("Page_Settings", "Settings"),
        ["about"] = Loc.Get("Page_About", "About")
    };

    /// <summary>Set while <see cref="NavigateTo"/> moves the selection, so the
    /// selection handler does not navigate a second time without the payload.</summary>
    private bool _suppressNav;

    public ShellPage()
    {
        InitializeComponent();

        // Ctrl+F focuses the global search box: the one shortcut a tweak
        // catalog-style app needs to be keyboard-complete.
        var ctrlF = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.F,
            Modifiers = Windows.System.VirtualKeyModifiers.Control
        };
        ctrlF.Invoked += (_, args) =>
        {
            TitleBarSearchBox.Focus(FocusState.Programmatic);
            args.Handled = true;
        };
        KeyboardAccelerators.Add(ctrlF);

        // Defer window wiring to Loaded: the window instance does not exist while
        // this page is being constructed inside its content tree.
        Loaded += (_, _) => WireTitleBar();
        Unloaded += (_, _) => ToastService.ToastRequested -= ShowToast;
        ToastService.ToastRequested += ShowToast;

        // Honor the --page startup tag (docs/screenshots tooling); anything
        // unknown or absent lands on Home.
        var startup = App.StartupPageTag;
        var target = startup is not null
            ? Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == startup)
            : null;
        Nav.SelectedItem = target ?? Nav.MenuItems[0];
    }

    /// <summary>Toasts currently animating out, so a second dismissal (the timer
    /// and a click racing) cannot run the exit twice.</summary>
    private readonly HashSet<Border> _dismissing = new();

    /// <summary>Renders a transient toast notification. Click dismisses early.</summary>
    private void ShowToast(string message, ToastKind kind)
    {
        try
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                while (ToastLayer.Children.Count >= 3)
                {
                    if (ToastLayer.Children[0] is Border oldest)
                    {
                        Dismiss(oldest);
                    }
                    else
                    {
                        ToastLayer.Children.RemoveAt(0);
                    }
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

                // Slid in from the right edge it is anchored to, so a toast reads
                // as something arriving rather than a block appearing.
                var slide = new CompositeTransform { TranslateX = 40 };
                toast.RenderTransform = slide;
                toast.Opacity = 0;

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
                toast.PointerPressed += (_, _) =>
                {
                    timer.Stop();
                    Dismiss(toast);
                };

                ToastLayer.Children.Add(toast);
                AnimateIn(toast, slide);

                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    Dismiss(toast);
                };
                timer.Start();
            });
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Toast: {ex.Message}");
        }
    }

    /// <summary>Slides and fades a toast in. Both properties animate on the
    /// compositor, so this costs the UI thread nothing while it runs.</summary>
    private static void AnimateIn(Border toast, CompositeTransform slide)
    {
        try
        {
            var fade = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, toast);
            Storyboard.SetTargetProperty(fade, "Opacity");

            var glide = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(260)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(glide, slide);
            Storyboard.SetTargetProperty(glide, "TranslateX");

            var storyboard = new Storyboard();
            storyboard.Children.Add(fade);
            storyboard.Children.Add(glide);
            storyboard.Begin();
        }
        catch (Exception ex)
        {
            // Motion is optional: a toast that appears without it is fine.
            toast.Opacity = 1;
            slide.TranslateX = 0;
            Optim.Core.Logging.FileLogger.Warn($"Toast animation: {ex.Message}");
        }
    }

    /// <summary>Fades a toast out, then removes it.</summary>
    private void Dismiss(Border toast)
    {
        if (!_dismissing.Add(toast) || !ToastLayer.Children.Contains(toast))
        {
            _dismissing.Remove(toast);
            return;
        }

        try
        {
            var fade = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(170)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(fade, toast);
            Storyboard.SetTargetProperty(fade, "Opacity");

            var storyboard = new Storyboard();
            storyboard.Children.Add(fade);
            storyboard.Completed += (_, _) =>
            {
                ToastLayer.Children.Remove(toast);
                _dismissing.Remove(toast);
            };
            storyboard.Begin();
        }
        catch (Exception ex)
        {
            // Never leave a toast on screen because its exit animation failed.
            ToastLayer.Children.Remove(toast);
            _dismissing.Remove(toast);
            Optim.Core.Logging.FileLogger.Warn($"Toast dismiss: {ex.Message}");
        }
    }

    /// <summary>
    /// Fades freshly navigated content in. Tab switching stays instant - there is
    /// no transition to sit through - but the arrival is soft instead of a hard
    /// cut, which is what makes a page feel like it settled rather than snapped.
    /// </summary>
    private void FadeInContent()
    {
        if (ContentFrame.Content is not UIElement content)
        {
            return;
        }

        try
        {
            content.Opacity = 0;
            var fade = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(140)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, content);
            Storyboard.SetTargetProperty(fade, "Opacity");

            var storyboard = new Storyboard();
            storyboard.Children.Add(fade);
            // Safety net: content must never stay invisible if the animation is
            // interrupted (a second navigation landing mid-fade).
            storyboard.Completed += (_, _) => content.Opacity = 1;
            storyboard.Begin();
        }
        catch (Exception ex)
        {
            content.Opacity = 1;
            Optim.Core.Logging.FileLogger.Warn($"Content fade: {ex.Message}");
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
        // Programmatic navigation sets SelectedItem itself and then navigates
        // with the full payload; letting the selection event navigate too would
        // start a second, parameterless load of the same page.
        if (_suppressNav)
        {
            return;
        }

        if (args.SelectedItem is NavigationViewItem item
            && item.Tag is string tag
            && Routes.TryGetValue(tag, out var pageType)
            && (ContentFrame.Content is null || ContentFrame.Content.GetType() != pageType))
        {
            ContentFrame.Navigate(pageType, PageTitles.GetValueOrDefault(tag, ""));
            FadeInContent();
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
                // Suppress the selection handler: this method performs the one
                // navigation below, carrying the highlight payload with it.
                _suppressNav = true;
                try
                {
                    Nav.SelectedItem = target;
                }
                finally
                {
                    _suppressNav = false;
                }
            }

            // When highlighting a specific tweak, always (re)navigate so the
            // target page receives the highlight id even if already selected.
            // Otherwise keep the old guard to avoid reloading the same page.
            if (highlightTweakId is not null
                || ContentFrame.Content is null
                || ContentFrame.Content.GetType() != pageType)
            {
                ContentFrame.Navigate(pageType, param);
                FadeInContent();
            }
            else if (target is null)
            {
                ContentFrame.Navigate(pageType, param);
                FadeInContent();
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
