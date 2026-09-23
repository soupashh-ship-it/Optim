using Optim.Core.Tweaks;

namespace Optim.App.Views;

public sealed partial class PrivacyPage : TweakListPage
{
    public PrivacyPage()
    {
        InitializeComponent();
        Content = TweakContent;
    }

    protected override TweakCategory Category => TweakCategory.Privacy;
}
