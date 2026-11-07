using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace WinMon.Controls;

/// <summary>A normal button that also exposes the activity panel's disclosure state to UI Automation.</summary>
public sealed class ActivityPanelButton : Button
{
    private bool _expanded = true;

    internal void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        var previous = _expanded;
        _expanded = expanded;
        UIElementAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(
            ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
            previous ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
            expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PanelPeer(this);

    private sealed class PanelPeer(ActivityPanelButton button) : ButtonAutomationPeer(button), IExpandCollapseProvider
    {
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

        public ExpandCollapseState ExpandCollapseState => button._expanded
            ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public void Expand() => SetExpanded(true);
        public void Collapse() => SetExpanded(false);

        private void SetExpanded(bool expanded)
        {
            if (!button.IsEnabled) throw new ElementNotEnabledException();
            if (button._expanded != expanded) button.OnClick();
        }
    }
}
