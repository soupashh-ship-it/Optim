using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Optim.App.Models;
using Optim.App.Services;
using Optim.Core.Tweaks;

namespace Optim.App.Views;

/// <summary>
/// Base page for catalog-driven tuning pages rendered as settings-card rows:
/// icon | title+description | toggle, with a filter search box and match counts.
/// </summary>
public abstract class TweakListPage : Page
{
    protected abstract TweakCategory Category { get; }

    private readonly StackPanel _root;
    private readonly TextBlock _header;
    private readonly TextBlock _subtitle;
    private readonly TextBox _search;
    private readonly CheckBox _advanced;
    private readonly TextBlock _count;
    private readonly List<TweakCard> _cards = new();
    private readonly List<(TextBlock Header, string Section)> _sectionHeaders = new();
    private bool _loaded;

    /// <summary>One rendered tweak row with the handles bulk actions need.</summary>
    private sealed class TweakCard
    {
        public TweakCard(TweakRow row, Border card, ToggleSwitch toggle, Border savedTag, TextBlock stateLabel)
        {
            Row = row;
            Card = card;
            Toggle = toggle;
            SavedTag = savedTag;
            StateLabel = stateLabel;
        }

        public TweakRow Row { get; }
        public Border Card { get; }
        public ToggleSwitch Toggle { get; }
        public Border SavedTag { get; }
        public TextBlock StateLabel { get; }
        public string Section => Row.Definition.Section;
    }

    // Tune-Score hero handles (built once in the constructor).
    private ProgressRing? _scoreRing;
    private TextBlock? _scoreLabel;
    private TextBlock? _scoreMood;
    private Border? _enginePill;
    private TextBlock? _activeText;
    private TextBlock? _worthText;
    private Border? _worthPill;
    private TextBlock? _statsText;
    private Button? _applySuggestedButton;

    /// <summary>The built page body. Concrete XAML pages assign it to Content
    /// after InitializeComponent (the XAML shell exists so Frame.Navigate can
    /// activate the type; all UI is built here).</summary>
    protected FrameworkElement TweakContent { get; }

    protected TweakListPage()
    {
        // Keep the built page alive: tab switches reuse the instance instead
        // of re-detecting and rebuilding dozens of cards every visit.
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        try
        {
            _root = new StackPanel { Spacing = 4 };
            _header = new TextBlock
            {
                Style = (Style)Application.Current.Resources["TitleTextBlockStyle"],
                Margin = new Thickness(0, 0, 0, 4)
            };

            _subtitle = new TextBlock
            {
                Style = CardDescStyle,
                Margin = new Thickness(0, 0, 0, 12)
            };

            _search = new TextBox
            {
                PlaceholderText = "Search tweaks…",
                Width = 280,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12)
            };
            _search.TextChanged += (_, _) => ApplyFilter();

            _count = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.6
            };

            _advanced = new CheckBox
            {
                Content = "Advanced",
                VerticalAlignment = VerticalAlignment.Center
            };
            _advanced.Checked += (_, _) => ApplyFilter();
            _advanced.Unchecked += (_, _) => ApplyFilter();
            // Deliberately no Unloaded handler: the page instance is cached, so a
            // load still in flight when the user switches tabs finishes into the
            // detached tree. Aborting it here meant re-detecting and rebuilding
            // every card on the next visit, which is the cost caching exists to
            // avoid.

            // One parent per element: search + count live in the toolbar, and the
            // toolbar is the single child added to the page panel. Adding an
            // element to two parents throws during construction, which escapes
            // Frame.Navigate as a native crash (coreclr AV).
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            toolbar.Children.Add(_search);
            toolbar.Children.Add(_advanced);
            toolbar.Children.Add(_count);

            // Fluid like the window itself: no max width, content spans
            // edge to edge at any resolution.
            var panel = new StackPanel { Spacing = 0 };
            panel.Children.Add(_header);
            panel.Children.Add(_subtitle);
            panel.Children.Add(BuildHero());
            panel.Children.Add(toolbar);
            panel.Children.Add(_root);
            try
            {
                foreach (var extra in BuildFooter())
                {
                    panel.Children.Add(extra);
                }
            }
            catch (Exception ex)
            {
                Optim.Core.Logging.FileLogger.Error($"TweakListPage BuildFooter ({GetType().Name}): {ex}");
                throw;
            }

            TweakContent = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }
        catch (Exception ex)
        {
            // Construction failures escape Frame.Navigate as native crashes,
            // so log the managed stack while it still exists.
            Optim.Core.Logging.FileLogger.Error($"TweakListPage ctor FAILED ({GetType().Name}): {ex}");
            throw;
        }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        var title = string.Empty;
        string? highlightId = null;
        if (e.Parameter is TweakPageArgs args)
        {
            title = args.Title;
            highlightId = args.HighlightTweakId;
        }
        else if (e.Parameter is string s)
        {
            title = s;
        }

        // A cached page can be navigated to again right after an Unloaded pass.
        _alive = true;

        // Settings can revert changes from outside this page, so the journal view
        // is re-read on every visit instead of carried over from the last one.
        InvalidateJournal();

        Header = title;
        Subtitle = Category switch
        {
            TweakCategory.Optimize => "Toggle performance tweaks. Some changes apply after a restart.",
            TweakCategory.Privacy => "Reduce telemetry and tracking. Every change is reversible from Settings.",
            _ => "Turn Windows features on or off to fit how you use your PC."
        };

        if (!_loaded || !_fullyLoaded)
        {
            _loaded = true;
            _alive = true;
            _ = LoadTweaksAsync(highlightId);
        }
        else
        {
            // Journal can change from Settings (revert-all); resync the hero.
            RefreshHero();
            if (highlightId is not null)
            {
                HighlightTweak(highlightId);
            }
            else
            {
                // Landing on the page without a search hit: drop the accent the
                // previous jump left behind.
                ClearHighlight();
            }
        }
        base.OnNavigatedTo(e);
    }

    private string Header
    {
        set => _header.Text = string.IsNullOrWhiteSpace(value)
            ? Category switch
            {
                TweakCategory.Optimize => "System Optimizations",
                TweakCategory.Privacy => "Privacy",
                _ => "Windows Features"
            }
            : value;
    }

    private string Subtitle
    {
        set => _subtitle.Text = value;
    }

    private bool _bulkOp;

    private HashSet<string>? _journalCache;

    /// <summary>
    /// Cached view of the journal for the current page load. Reading it takes a
    /// global mutex and a JSON file read, and RefreshHero asks for it after every
    /// card batch, so an uncached read made one page load pay for that file a
    /// dozen times over on the UI thread. Anything that can write the journal
    /// drops the cache first.
    /// </summary>
    private HashSet<string> JournaledIds() => _journalCache ??= ReadJournalIds();

    private void InvalidateJournal() => _journalCache = null;

    private static HashSet<string> ReadJournalIds()
    {
        try
        {
            return App.Get<ChangeJournal>()
                .Snapshot()
                .Select(e => e.TweakId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Optim's category hero: tune-score ring, engine chips and the
    /// suggested/preview/rollback actions. Original composition and wording.
    /// </summary>
    private FrameworkElement BuildHero()
    {
        var ringGrid = TuneKit.TuneRing(0, out var ring, out var label);
        _scoreRing = ring;
        _scoreLabel = label;
        _scoreMood = new TextBlock
        {
            Text = TuneKit.ScoreMood(0),
            Style = CaptionStyle,
            Opacity = 0.75,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var scoreCaption = new TextBlock
        {
            Text = "Tune Score",
            Style = CaptionStyle,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ToolTipService.SetToolTip(scoreCaption,
            "Measures how many suggested tweaks in this category are applied — not overall system performance.");
        var left = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(ringGrid);
        left.Children.Add(scoreCaption);
        left.Children.Add(_scoreMood);

        var accent = Microsoft.UI.Colors.Teal;
        try
        {
            if (Application.Current.Resources.TryGetValue("OptimBrandBrush", out var brand) && brand is SolidColorBrush brandBrush)
            {
                accent = brandBrush.Color;
            }
        }
        catch
        {
        }
        _enginePill = TuneKit.Pill(TuneKit.EngineName(Category), accent, Microsoft.UI.Colors.White);

        _activeText = new TextBlock { Text = "0 of 0 on", FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
        var activePill = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Gray),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _activeText
        };
        _worthText = new TextBlock { Text = "+0", FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
        var worthPill = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Gray),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _worthText
        };
        _worthPill = worthPill;

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        chips.Children.Add(_enginePill);
        chips.Children.Add(activePill);
        chips.Children.Add(worthPill);

        var blurb = new TextBlock { Text = TuneKit.EngineBlurb(Category), Style = CardDescStyle, TextWrapping = TextWrapping.Wrap };
        _statsText = new TextBlock { Style = CaptionStyle, Opacity = 0.75 };
        var middle = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        middle.Children.Add(chips);
        middle.Children.Add(blurb);
        middle.Children.Add(_statsText);

        _applySuggestedButton = new Button { Content = "Apply suggested", MinWidth = 170 };
        try
        {
            if (Application.Current.Resources.TryGetValue("BrandButtonStyle", out var brandStyle) && brandStyle is Style brandButtonStyle)
            {
                _applySuggestedButton.Style = brandButtonStyle;
            }
        }
        catch
        {
        }
        _applySuggestedButton.Click += ApplySuggested_Click;

        var previewButton = new Button { Content = "Preview", MinWidth = 170 };
        previewButton.Click += Preview_Click;
        var rollbackButton = new Button { Content = "Roll back", MinWidth = 170 };
        rollbackButton.Click += RollbackCategory_Click;
        var right = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_applySuggestedButton);
        right.Children.Add(previewButton);
        right.Children.Add(rollbackButton);

        var grid = new Grid { ColumnSpacing = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(middle, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(middle);
        grid.Children.Add(right);

        return new Border
        {
            Style = CardStyle,
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12),
            Child = grid
        };
    }

    private void RefreshHero()
    {
        if (_scoreRing is null || _scoreLabel is null || _scoreMood is null
            || _activeText is null || _worthText is null || _statsText is null
            || _applySuggestedButton is null)
        {
            return;
        }

        var total = _cards.Count;
        var on = _cards.Count(c => c.Row.IsOn);
        var fraction = total == 0 ? 0 : (double)on / total;
        _scoreRing.Value = fraction * 100;
        _scoreLabel.Text = $"{fraction:P0}";
        _scoreMood.Text = TuneKit.ScoreMood(fraction);

        var journaled = JournaledIds();
        var kept = _cards.Count(c => journaled.Contains(c.Row.Definition.Id));
        var recommended = _cards.Count(c => c.Row.Definition.Recommended);
        var waiting = _cards.Count(c => c.Row.Definition.Recommended && !c.Row.IsOn);
        var worth = _cards.Where(c => c.Row.IsOn).Sum(c => c.Row.Definition.Points);

        _activeText.Text = $"{on} of {total} on";
        _worthText.Text = $"+{worth}";
        _statsText.Text = $"Suggested: {recommended} · Waiting: {waiting} · Kept safe: {kept}";
        _applySuggestedButton.Content = waiting == 0 ? "All suggested applied" : $"Apply suggested ({waiting})";
    }

    private void RefreshSavedTags()
    {
        var journaled = JournaledIds();
        foreach (var c in _cards)
        {
            c.SavedTag.Visibility = journaled.Contains(c.Row.Definition.Id)
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void ApplySuggested_Click(object sender, RoutedEventArgs e)
    {
        var targets = _cards.Where(c => c.Row.Definition.Recommended && !c.Row.IsOn).ToList();
        if (targets.Count == 0 || XamlRoot is null)
        {
            if (XamlRoot is not null)
            {
                await new ContentDialog
                {
                    Title = "Nothing to do",
                    Content = "Every suggested tweak on this page is already on.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                }.ShowAsync();
            }
            return;
        }

        var worth = targets.Sum(c => c.Row.Definition.Points);
        var confirm = new ContentDialog
        {
            Title = $"Apply {TuneKit.Count(targets.Count, "suggested tweak", "suggested tweaks")}?",
            Content = $"Worth +{worth}. Every change is journaled — Settings can revert them all.",
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _bulkOp = true;
        try
        {
            if (XamlRoot is null || !await SettingsPage.EnsureRestorePointOfferAsync(XamlRoot))
            {
                return;
            }

            var done = 0;
            var failedCount = 0;
            foreach (var tc in targets)
            {
                tc.Row.IsOn = true;
                // row.IsOn keeps the old value when the write fails; only
                // count it when the toggle truly flipped on.
                if (tc.Row.IsOn)
                {
                    done++;
                }
                else
                {
                    failedCount++;
                }
                tc.Toggle.IsOn = tc.Row.IsOn;
                tc.StateLabel.Text = tc.Row.IsOn ? "On" : "Off";
                if ((done + failedCount) % 5 == 0)
                {
                    await YieldToUiAsync();
                }
            }
            InvalidateJournal();
            RefreshSavedTags();
            var (applied, failed) = (done, failedCount);
            ToastService.Show(
                failed == 0
                    ? $"{TuneKit.Count(applied, "suggested tweak", "suggested tweaks")} applied."
                    : $"{TuneKit.Count(applied, "suggested tweak", "suggested tweaks")} applied, {TuneKit.Count(failed, "failure", "failures")} — see logs.",
                failed == 0 ? ToastKind.Success : ToastKind.Warning);
        }
        finally
        {
            _bulkOp = false;
            RefreshHero();
        }
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        var waiting = _cards.Where(c => c.Row.Definition.Recommended && !c.Row.IsOn).ToList();
        var list = new StackPanel { Spacing = 6 };
        if (waiting.Count == 0)
        {
            list.Children.Add(new TextBlock { Text = "Everything suggested is already on." });
        }
        else
        {
            foreach (var tc in waiting)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(new TextBlock { Text = tc.Row.Title, Width = 320, TextWrapping = TextWrapping.Wrap });
                row.Children.Add(TuneKit.WorthPill(tc.Row.Definition.Points));
                list.Children.Add(row);
            }
        }

        await new ContentDialog
        {
            Title = $"Suggested changes ({waiting.Count})",
            Content = new ScrollViewer { Content = list, MaxHeight = 400 },
            CloseButtonText = "Close",
            XamlRoot = XamlRoot
        }.ShowAsync();
    }

    private async void RollbackCategory_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        var engine = App.Get<RegistryTweakEngine>();
        var journaled = JournaledIds();
        var targets = _cards.Where(c => journaled.Contains(c.Row.Definition.Id)).ToList();
        if (targets.Count == 0)
        {
            await new ContentDialog
            {
                Title = "Nothing to roll back",
                Content = "No tweak on this page has a journaled change.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
            return;
        }

        var confirm = new ContentDialog
        {
            Title = $"Roll back {TuneKit.Count(targets.Count, "tweak", "tweaks")}?",
            Content = "Restores the exact values Optim saw before changing them — not the catalog defaults.",
            PrimaryButtonText = "Roll back",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _bulkOp = true;
        try
        {
            var ids = targets.Select(c => c.Row.Definition.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var replayed = await Task.Run(() => engine.RevertTweaksFromJournal(ids));
            InvalidateJournal();
            foreach (var tc in targets)
            {
                tc.Row.ResnapFromSystem();
                tc.Toggle.IsOn = tc.Row.IsOn;
                tc.StateLabel.Text = tc.Row.IsOn ? "On" : "Off";
            }
            RefreshSavedTags();
            ToastService.Show($"Rolled back {TuneKit.Count(replayed, "journaled change", "journaled changes")}.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Category rollback ({GetType().Name}): {ex.Message}");
            ToastService.Show($"Rollback incomplete: {ex.Message}", ToastKind.Error);
        }
        finally
        {
            _bulkOp = false;
            RefreshHero();
        }
    }

    private async void ShowTweakInfo(TweakRow row)
    {
        if (XamlRoot is null)
        {
            return;
        }

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = row.Description, TextWrapping = TextWrapping.Wrap });

        var facts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        facts.Children.Add(TuneKit.RiskPill(row.Definition.Risk));
        facts.Children.Add(TuneKit.WorthPill(row.Definition.Points));
        if (row.HasRestartNote)
        {
            facts.Children.Add(TuneKit.Pill("Restart required", Microsoft.UI.Colors.DarkOrange, Microsoft.UI.Colors.White));
        }
        body.Children.Add(facts);

        body.Children.Add(new TextBlock { Text = "Registry writes:", Style = CaptionStyle, Opacity = 0.75 });
        foreach (var op in row.Definition.Apply.Take(6))
        {
            body.Children.Add(new TextBlock
            {
                Text = $"{op.Hive}\\{op.KeyPath} [{op.ValueName ?? "(default)"}]",
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 11,
                Opacity = 0.85,
                TextWrapping = TextWrapping.Wrap
            });
        }
        if (row.Definition.Apply.Count > 6)
        {
            body.Children.Add(new TextBlock { Text = $"…and {row.Definition.Apply.Count - 6} more.", Style = CaptionStyle, Opacity = 0.75 });
        }

        await new ContentDialog
        {
            Title = row.Title,
            Content = new ScrollViewer { Content = body, MaxHeight = 420 },
            CloseButtonText = "Close",
            XamlRoot = XamlRoot
        }.ShowAsync();
    }

    /// <summary>
    /// Loads tweak cards without blocking navigation: detection runs on a
    /// background thread and cards are added in small batches so the UI thread
    /// stays responsive even with dozens of tweaks.
    /// </summary>
    private async Task LoadTweaksAsync(string? highlightId)
    {
        // Generation token: a later load (e.g. a search jump arriving while the
        // first load is still batching) supersedes this one, so batches from the
        // abandoned run never append cards or steal the highlight.
        // Note: navigating away does not abort a load. The grid is cached, so
        // finishing while detached is what makes the next visit instant.
        var generation = ++_loadGeneration;
        _fullyLoaded = false;
        _cards.Clear();
        _sectionHeaders.Clear();
        _highlightedCard = null;
        _root.Children.Clear();
        _count.Text = "Loading…";
        try
        {
            var engine = App.Get<RegistryTweakEngine>();
            // Grouped display: stable sort by section keeps catalog order inside each group.
            var defs = TweakCatalog.For(Category)
                .OrderBy(d => TweakCatalog.SectionIndex(Category, d.Section))
                .ToList();
            var journaled = JournaledIds();

            // Registry reads are thread-safe: detect everything off the UI thread.
            var states = await Task.Run(() => defs
                .Select(d => (Def: d, IsOn: engine.Detect(d) == TweakState.Applied))
                .ToList());

            const int BatchSize = 10;
            for (var i = 0; i < states.Count; i += BatchSize)
            {
                // Only a newer load supersedes this one.
                if (generation != _loadGeneration)
                {
                    return;
                }

                foreach (var (def, isOn) in states.Skip(i).Take(BatchSize))
                {
                    if (def.Section.Length > 0
                        && !_sectionHeaders.Any(h => h.Section == def.Section))
                    {
                        var header = TuneKit.SectionHeader(def.Section);
                        _sectionHeaders.Add((header, def.Section));
                        _root.Children.Add(header);
                    }

                    var row = new TweakRow(def, engine, isOn);
                    row.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(TweakRow.IsOn))
                        {
                            RefreshHero();
                        }
                    };
                    var built = BuildCard(row, journaled);
                    _cards.Add(built);
                    _root.Children.Add(built.Card);
                }

                ApplyFilter();
                RefreshHero();
                // Yield (not sleep: Task.Delay clamps to ~15ms on Windows) so
                // input and animations get a turn between batches.
                await YieldToUiAsync();
            }

            if (generation != _loadGeneration)
            {
                return;
            }

            ApplyFilter();
            RefreshHero();
            _fullyLoaded = true;
            if (highlightId is not null && _alive)
            {
                HighlightTweak(highlightId);
            }
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Tweak load ({GetType().Name}): {ex.Message}");
            _count.Text = "Could not load tweaks — see logs.";
        }
    }

    /// <summary>False between Unloaded and the next OnNavigatedTo. Gates only the
    /// arrival highlight: scrolling and pulsing a page the user is not looking at
    /// is pointless. It does not gate card building.</summary>
    private bool _alive = true;
    private bool _fullyLoaded;
    private int _loadGeneration;

    /// <summary>
    /// Hands the UI thread back at low priority. Task.Yield requeues at normal
    /// priority and competes with input and rendering; a low-priority enqueue
    /// lets the compositor and pointer handlers run first, so a batch of settings
    /// cards never lands in the middle of a scroll.
    /// </summary>
    private Task YieldToUiAsync()
    {
        var done = new TaskCompletionSource();
        if (!DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => done.TrySetResult()))
        {
            done.TrySetResult();
        }

        return done.Task;
    }

    // Resource lookups per card (dozens of tweaks per page) show up in
    // navigation traces; resolve once. The styles themselves reference theme
    // brushes internally, so caching the Style object is safe across
    // light/dark switches.
    private static Style? _cardTitleStyle;
    private static Style? _cardDescStyle;
    private static Style? _captionStyle;
    private static Style? _toggleStyle;
    private static Style? _cardStyle;

    // protected: derived pages (Features DISM section) compose rows with the same styles.
    protected static Style CardTitleStyle => _cardTitleStyle ??= Lookup("CardTitleStyle");
    protected static Style CardDescStyle => _cardDescStyle ??= Lookup("CardDescStyle");
    private static Style CaptionStyle => _captionStyle ??= Lookup("CaptionTextBlockStyle");
    protected static Style ToggleStyle => _toggleStyle ??= Lookup("RowToggleStyle");
    // Container style for the category hero (a panel, not a settings row).
    private static Style CardStyle => _cardStyle ??= Lookup("CardBorderStyle");

    private static Style Lookup(string key) => (Style)Application.Current.Resources[key];

    private TweakCard BuildCard(TweakRow row, HashSet<string> journaled)
    {
            var icon = new FontIcon
            {
                // Same glyphs the nav already proves render correctly.
                Glyph = Category switch
                {
                    TweakCategory.Optimize => "\uF259",
                    TweakCategory.Privacy => "\uE7B3",
                    _ => "\uE74C"
                },
                FontSize = 18,
                Opacity = 0.85,
                VerticalAlignment = VerticalAlignment.Center
            };

            var title = new TextBlock { Text = row.Title, Style = CardTitleStyle };
            var desc = new TextBlock { Text = row.Description, Style = CardDescStyle };
            var restart = new TextBlock
            {
                Text = "Restart required",
                Style = CaptionStyle,
                Foreground = TuneKit.WarningBrush,
                Visibility = row.HasRestartNote ? Visibility.Visible : Visibility.Collapsed
            };
            var advancedTag = new TextBlock
            {
                Text = "Advanced",
                Style = CaptionStyle,
                Opacity = 0.75,
                Visibility = row.IsAdvanced ? Visibility.Visible : Visibility.Collapsed
            };

            var notes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            notes.Children.Add(restart);
            notes.Children.Add(advancedTag);

            var savedTag = TuneKit.KeptSafePill();
            savedTag.Visibility = journaled.Contains(row.Definition.Id)
                ? Visibility.Visible : Visibility.Collapsed;

            var infoButton = new Button
            {
                Content = new FontIcon { Glyph = "\uE946", FontSize = 12 },
                Padding = new Thickness(8, 2, 8, 2),
                Background = TuneKit.TransparentBrush,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            infoButton.Click += (_, _) => ShowTweakInfo(row);

            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(title);
            text.Children.Add(desc);
            text.Children.Add(notes);

            var stateLabel = new TextBlock
            {
                Text = row.IsOn ? "On" : "Off",
                Style = CaptionStyle,
                Opacity = 0.8,
                VerticalAlignment = VerticalAlignment.Center
            };

            var toggle = new ToggleSwitch
            {
                IsOn = row.IsOn,
                Style = ToggleStyle,
                VerticalAlignment = VerticalAlignment.Center
            };
            toggle.Toggled += (_, _) =>
            {
                var wanted = toggle.IsOn;
                if (!_bulkOp)
                {
                    // First user change this session: offer a restore point.
                    // Fire-and-continue: the dialog chain must never deadlock a
                    // synchronous toggle event, so gate asynchronously and only
                    // then proceed with the write.
                    _ = ToggleWithSafetyAsync(row, toggle, wanted);
                    return;
                }

                ApplyToggleNow(row, toggle, wanted);
            };

            // Right-aligned flow: impact, worth, kept-safe, details, state, switch.
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };
            actions.Children.Add(TuneKit.RiskPill(row.Definition.Risk));
            actions.Children.Add(TuneKit.WorthPill(row.Definition.Points));
            actions.Children.Add(savedTag);
            actions.Children.Add(infoButton);
            actions.Children.Add(stateLabel);
            actions.Children.Add(toggle);

            // The row itself is the real CommunityToolkit SettingsCard, so it
            // gets the platform hover/press animation instead of a hand-rolled
            // pointer hack. The wrapper Border owns the arrival accent: its
            // transparent border always reserves its width, so highlighting a
            // card never shifts the layout.
            var card = new Border
            {
                BorderThickness = new Thickness(2),
                BorderBrush = TuneKit.TransparentBrush,
                CornerRadius = new CornerRadius(8),
                Child = new SettingsCard
                {
                    Header = text,
                    HeaderIcon = icon,
                    Content = actions,
                    IsClickEnabled = false
                }
            };

            return new TweakCard(row, card, toggle, savedTag, stateLabel);
    }

    /// <summary>Gate a single user toggle behind the first-change restore offer.</summary>
    private async Task ToggleWithSafetyAsync(TweakRow row, ToggleSwitch toggle, bool wanted)
    {
        try
        {
            if (!await SettingsPage.EnsureRestorePointOfferAsync(XamlRoot))
            {
                // User cancelled at the safety gate: resnap the visual.
                toggle.IsOn = row.IsOn;
                return;
            }
        }
        catch
        {
        }

        ApplyToggleNow(row, toggle, wanted);
    }

    /// <summary>Writes one toggle and resyncs its row visuals.</summary>
    private void ApplyToggleNow(TweakRow row, ToggleSwitch toggle, bool wanted)
    {
        row.IsOn = wanted;
        // If the registry write failed, row.IsOn kept its old value —
        // resnap the visual so the switch never lies.
        if (toggle.IsOn != row.IsOn)
        {
            toggle.IsOn = row.IsOn;
            if (!_bulkOp)
            {
                ToastService.Show($"Could not apply '{row.Title}' — see logs.", ToastKind.Error);
            }
        }
        else if (!_bulkOp)
        {
            ToastService.Show($"'{row.Title}' {(wanted ? "enabled" : "disabled")}.", ToastKind.Success);
        }

        var card = _cards.FirstOrDefault(c => ReferenceEquals(c.Row, row));
        if (card is not null)
        {
            // The write above may have journaled this tweak: drop the cached view.
            InvalidateJournal();
            card.SavedTag.Visibility = JournaledIds().Contains(row.Definition.Id)
                ? Visibility.Visible : Visibility.Collapsed;
            card.StateLabel.Text = row.IsOn ? "On" : "Off";
        }
        RefreshHero();
    }

    private void ApplyFilter()
    {
        var query = _search.Text?.Trim() ?? string.Empty;
        var showAdvanced = _advanced.IsChecked == true;
        var visible = 0;

        foreach (var c in _cards)
        {
            var (row, card) = (c.Row, c.Card);
            var matches = (query.Length == 0
                || row.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                && (showAdvanced || !row.Definition.IsAdvanced);

            card.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
            if (matches)
            {
                visible++;
            }
        }

        // Orphaned section headers (all children filtered out) hide too.
        foreach (var (header, section) in _sectionHeaders)
        {
            header.Visibility = _cards.Any(c => c.Section == section && c.Card.Visibility == Visibility.Visible)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        _count.Text = query.Length == 0 && showAdvanced
            ? $"{_cards.Count} tweaks"
            : $"{visible} of {_cards.Count} shown";
    }

    /// <summary>Optional extra UI below the tweak list (e.g. power plans).</summary>
    protected virtual IEnumerable<FrameworkElement> BuildFooter() =>
        Enumerable.Empty<FrameworkElement>();

    private Border? _highlightedCard;

    /// <summary>
    /// Brings one specific tweak into focus after a global-search jump: clears
    /// any page filter that would hide it, scrolls it into view and accents it
    /// with a short pulse, so the arrival target is unmistakable.
    /// </summary>
    public void HighlightTweak(string tweakId)
    {
        var match = _cards.FirstOrDefault(c =>
            string.Equals(c.Row.Definition.Id, tweakId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return;
        }

        // Reset anything that could keep the target off screen. An advanced
        // target stays hidden while the box is off, and a leftover filter from
        // earlier typing would hide it too. Enable Advanced first: its Checked
        // event re-runs the filter.
        if (match.Row.IsAdvanced && _advanced.IsChecked != true)
        {
            _advanced.IsChecked = true;
        }
        if (!string.IsNullOrEmpty(_search.Text))
        {
            _search.Text = string.Empty; // TextChanged re-runs ApplyFilter
        }
        else
        {
            ApplyFilter();
        }

        ClearHighlight();
        _highlightedCard = match.Card;

        try
        {
            match.Card.BorderBrush = Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent)
                && accent is Brush accentBrush
                ? accentBrush
                : new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
        }
        catch
        {
            match.Card.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
        }
        match.Card.BorderThickness = new Thickness(2);

        // Defer until layout: from OnNavigatedTo the card may not be measured
        // yet, and the pulse must not fight the scroll.
        var target = match.Card;
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                target.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.3 });
                var pulse = new Storyboard();
                var fade = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.45,
                    Duration = new Duration(TimeSpan.FromMilliseconds(320)),
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(2)
                };
                Storyboard.SetTarget(fade, target);
                Storyboard.SetTargetProperty(fade, "Opacity");
                pulse.Children.Add(fade);
                pulse.Begin();
            }
            catch (Exception ex)
            {
                Optim.Core.Logging.FileLogger.Warn($"Highlight pulse: {ex.Message}");
            }
        });
    }

    /// <summary>Removes the arrival accent from the previously highlighted card.</summary>
    private void ClearHighlight()
    {
        if (_highlightedCard is null)
        {
            return;
        }

        try
        {
            // Back to a transparent accent slot: the reserved 2px border keeps
            // the row's layout identical to its highlighted state.
            _highlightedCard.BorderBrush = TuneKit.TransparentBrush;
            _highlightedCard.BorderThickness = new Thickness(2);
        }
        catch
        {
        }

        _highlightedCard = null;
    }
}
