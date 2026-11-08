namespace WinMon.Preferences;

/// <summary>How each resource readout draws its live level and recent peak, in Settings order.</summary>
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