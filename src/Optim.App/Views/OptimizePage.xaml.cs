using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optim.App.Services;
using Optim.Core.Power;
using Optim.Core.Tweaks;

namespace Optim.App.Views;

public sealed partial class OptimizePage : TweakListPage
{
    public OptimizePage()
    {
        InitializeComponent();
        Content = TweakContent;
    }

    protected override TweakCategory Category => TweakCategory.Optimize;

    protected override IEnumerable<FrameworkElement> BuildFooter()
    {
        var engine = App.Get<PowerEngine>();

        var title = new TextBlock
        {
            Text = "Power plan",
            Style = (Style)Application.Current.Resources["CardTitleStyle"]
        };
        var desc = new TextBlock
        {
            Text = "High performance favors speed over battery life.",
            Style = (Style)Application.Current.Resources["CardDescStyle"]
        };
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(desc);

        var plans = new ComboBox { MinWidth = 220, PlaceholderText = "Loading plans…" };
        var apply = new Button { Content = "Apply", IsEnabled = false };
        var unlock = new Button { Content = "Unlock Ultimate Performance" };
        var status = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        };

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(plans);
        controls.Children.Add(apply);
        controls.Children.Add(unlock);

        var right = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(controls);
        right.Children.Add(status);

        // Real SettingsCard: the power-plan row should hover and animate like
        // every other settings row on the platform.
        var card = new SettingsCard
        {
            Margin = new Thickness(0, 8, 0, 0),
            Header = text,
            Content = right,
            IsClickEnabled = false
        };

        apply.Click += (_, _) => _ = ApplyPlanAsync(engine, plans, status, apply);
        unlock.Click += (_, _) => _ = UnlockUltimateAsync(engine, plans, status);
        var plansLoaded = false;
        card.Loaded += (_, _) =>
        {
            // Cached page: enumerate plans once, Apply/Unlock refresh explicitly.
            if (!plansLoaded)
            {
                plansLoaded = true;
                _ = LoadPlansAsync(engine, plans, status, apply);
            }
        };

        yield return card;
    }

    private static async Task LoadPlansAsync(PowerEngine engine, ComboBox plans, TextBlock status, Button apply)
    {
        try
        {
            var list = await engine.ListAsync();
            plans.Items.Clear();
            foreach (var p in list)
            {
                plans.Items.Add(p);
            }

            plans.DisplayMemberPath = "Name";
            var active = list.FirstOrDefault(p => p.IsActive);
            if (active is not null)
            {
                plans.SelectedItem = active;
            }

            plans.PlaceholderText = list.Count == 0 ? "No plans found" : "Choose a plan";
            apply.IsEnabled = plans.SelectedItem is not null;
            plans.SelectionChanged += (_, _) => apply.IsEnabled = plans.SelectedItem is not null;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Power plans: {ex.Message}");
            status.Text = "Could not list power plans — see logs.";
        }
    }

    private static async Task ApplyPlanAsync(PowerEngine engine, ComboBox plans, TextBlock status, Button apply)
    {
        if (plans.SelectedItem is not PowerPlan plan)
        {
            return;
        }

        try
        {
            apply.IsEnabled = false;
            var ok = await engine.SetActiveAsync(plan.Guid);
            status.Text = ok ? $"Active plan: {plan.Name}." : "Failed to switch plan — run elevated.";
            ToastService.Show(ok ? $"Power plan: {plan.Name}." : "Power plan switch failed.",
                ok ? ToastKind.Success : ToastKind.Error);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Set power plan: {ex.Message}");
            status.Text = "Failed to switch plan — see logs.";
        }
        finally
        {
            apply.IsEnabled = true;
        }
    }

    private static async Task UnlockUltimateAsync(PowerEngine engine, ComboBox plans, TextBlock status)
    {
        try
        {
            var ok = await engine.EnableUltimateAsync();
            status.Text = ok ? "Ultimate Performance unlocked — pick it above." : "Could not unlock — run elevated.";
            if (ok)
            {
                var dummy = new Button();
                await LoadPlansAsync(engine, plans, status, dummy);
            }
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Unlock ultimate: {ex.Message}");
            status.Text = "Could not unlock — see logs.";
        }
    }
}
