using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using NUnit.Framework;
using WinMon.Controls;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class ActivityPanelButtonTests
{
    [Test]
    public void AutomationReportsStateAndUsesTheButtonWithoutTogglingAnAlreadyMatchingState()
    {
        var button = new ActivityPanelButton();
        var expanded = true;
        var clicks = 0;
        button.Click += (_, _) =>
        {
            clicks++;
            expanded = !expanded;
            button.SetExpanded(expanded);
        };
        var peer = UIElementAutomationPeer.CreatePeerForElement(button)!;
        var provider = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)!;
        Assert.That(provider.ExpandCollapseState, Is.EqualTo(ExpandCollapseState.Expanded));
        provider.Collapse();
        provider.Collapse();
        Assert.That(provider.ExpandCollapseState, Is.EqualTo(ExpandCollapseState.Collapsed));
        provider.Expand();
        provider.Expand();
        Assert.That(provider.ExpandCollapseState, Is.EqualTo(ExpandCollapseState.Expanded));
        Assert.That(clicks, Is.EqualTo(2));
        button.IsEnabled = false;
        Assert.Throws<ElementNotEnabledException>(() => provider.Collapse());
    }
}