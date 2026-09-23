using Optim.Core.Tweaks;

namespace Optim.App.Views;

public sealed partial class FeaturesPage : TweakListPage
{
    public FeaturesPage()
    {
        InitializeComponent();
        Content = TweakContent;
    }

    protected override TweakCategory Category => TweakCategory.Features;
}
