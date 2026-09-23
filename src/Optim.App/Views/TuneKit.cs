using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Optim.Core.Tweaks;
using Windows.UI;

namespace Optim.App.Views;

/// <summary>
/// Optim's own visual language: pills, section headers and score rings built
/// from WinUI primitives. Deliberately distinct wording and styling from any
/// reference — Gentle/Moderate/Bold impact, "Tune Score", "+N" worth chips.
/// </summary>
internal static class TuneKit
{
    public static Border Pill(string text, Color background, Color foreground)
    {
        return new Border
        {
            Background = new SolidColorBrush(background),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(foreground)
            }
        };
    }

    public static Border RiskPill(TweakRisk risk) => risk switch
    {
        TweakRisk.Bold => Pill("Bold", Color.FromArgb(0xFF, 0xC0, 0x39, 0x2B), Colors.White),
        TweakRisk.Moderate => Pill("Moderate", Color.FromArgb(0xFF, 0xB2, 0x6A, 0x00), Colors.White),
        _ => Pill("Gentle", Color.FromArgb(0xFF, 0x2F, 0x7D, 0x3B), Colors.White)
    };

    public static Border WorthPill(int points) =>
        Pill($"+{points}", Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A), Colors.White);

    /// <summary>Marks a tweak that has a journaled change ready to roll back.</summary>
    public static Border KeptSafePill()
    {
        var pill = Pill("Kept safe", Colors.Transparent, Color.FromArgb(0xFF, 0x99, 0x99, 0x99));
        pill.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x99, 0x99, 0x99));
        pill.BorderThickness = new Thickness(1);
        return pill;
    }

    public static TextBlock SectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            Margin = new Thickness(0, 16, 0, 4)
        };
    }

    /// <summary>Score ring: a determinate ProgressRing with a centered percent label.</summary>
    public static Grid TuneRing(double fraction, out ProgressRing ring, out TextBlock label)
    {
        ring = new ProgressRing
        {
            Width = 84,
            Height = 84,
            Minimum = 0,
            Maximum = 100,
            Value = fraction * 100,
            IsIndeterminate = false
        };
        label = new TextBlock
        {
            Text = $"{fraction:P0}",
            Style = (Style)Application.Current.Resources["TitleTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var grid = new Grid();
        grid.Children.Add(ring);
        grid.Children.Add(label);
        return grid;
    }

    /// <summary>"1 tweak" / "14 tweaks" without (s) shorthand.</summary>
    public static string Count(int n, string one, string many) =>
        n == 1 ? $"1 {one}" : $"{n} {many}";

    public static string EngineName(TweakCategory category) => category switch    {
        TweakCategory.Optimize => "Performance Kit",
        TweakCategory.Privacy => "Privacy Shield",
        _ => "Feature Lab"
    };

    public static string EngineBlurb(TweakCategory category) => category switch
    {
        TweakCategory.Optimize => "Trims delays, background drag and latency across the system.",
        TweakCategory.Privacy => "Quiets telemetry, tracking and cloud uploads. Nothing here is permanent.",
        _ => "Shapes Windows features and shell behavior around how you work."
    };

    public static string ScoreMood(double fraction) => fraction switch
    {
        < 0.25 => "Getting started",
        < 0.5 => "Warming up",
        < 0.75 => "Well tuned",
        _ => "Fully tuned"
    };
}
