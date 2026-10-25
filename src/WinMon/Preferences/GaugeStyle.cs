namespace WinMon.Preferences;

/// <summary>How each Full-view resource readout draws its live level and two-minute peak, in Settings order.</summary>
/// <remarks>Saved by name, so the order can change; the first value is the default.</remarks>
public enum GaugeStyle
{
    Bowl,
    Needle,
    SegmentedArc,
    LeftBar,
    BottomBar,
    SegmentedBar,
    None
}
