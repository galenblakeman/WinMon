using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Preferences;

/// <summary>A static sample of the selected theme, palette and gauge style, isolated from the saved app resources.</summary>
public sealed partial class AppearancePreview : UserControl
{
    internal MetricReadout GaugePreview => MemoryReadout;

    internal CpuGraph ChartPreview => Chart;

    internal AppearancePreview()
    {
        InitializeComponent();
        Configure(CpuReadout, Strings.CPU, "CpuAccent", 32.8, "4.30 GHz");
        Configure(MemoryReadout, Strings.Memory, "MemoryAccent", 51.6,
            Strings.Format(Strings.GiB, 32.2, 62.5));
        Configure(DiskReadout, Strings.Disk, "DiskAccent", 42.0, Strings.Format(Strings.MBS, 0.2));
        Chart.ShowGrid = true;
        Chart.MetricLabels = [Strings.CPU, Strings.Memory, Strings.Disk, Strings.Network, Strings.GPU];
        Chart.SetData(Samples(), 120, null);
    }

    internal void Update(string theme, string palette, GaugeStyle style)
    {
        var light = theme == "Light" || (theme == "System" && MainWindow.IsSystemLight()) ||
                    theme is "BrushedSteel" or "BrushedNickel";
        var metal = MetalThemes.IsMetal(theme) ? MetalThemes.For(theme) : null;
        if (SystemParameters.HighContrast)
        {
            Resources["PanelSurface"] = SystemColors.WindowBrush;
            Resources["SectionHeaderSurface"] = SystemColors.WindowBrush;
            Resources["PlotSurface"] = SystemColors.WindowBrush;
            Resources["Text"] = SystemColors.WindowTextBrush;
            Resources["Muted"] = SystemColors.WindowTextBrush;
            Resources["Line"] = SystemColors.WindowTextBrush;
            Resources["GaugeTrack"] = SystemColors.GrayTextBrush;
            foreach (var key in AccentKeys) Resources[key] = SystemColors.WindowTextBrush;
            Resources["Accent"] = SystemColors.HighlightBrush;
        }
        else
        {
            Resources["PanelSurface"] = metal is null ? Brush(light ? "#F0F5FA" : "#15191E")
                : MetalThemes.PanelBrush(metal);
            Resources["SectionHeaderSurface"] = metal is null ? Brush(light ? "#E5EEF7" : "#252A31")
                : MetalThemes.ChromeBrush(metal, true);
            Resources["PlotSurface"] = metal is not null && theme is ("BrushedSteel" or "BrushedNickel")
                ? MetalThemes.PlotBrush(metal)
                : Brush(metal?.Plot ?? (light ? "#FFFFFF" : "#15191E"));
            Resources["Text"] = Brush(metal?.Text ?? (light ? "#20334A" : "#EDF2F6"));
            Resources["Muted"] = Brush(metal?.Muted ?? (light ? "#465D78" : "#B3BDC8"));
            Resources["Line"] = Brush(metal?.Line ?? (light ? "#CCD9E5" : "#35404B"));
            Resources["GaugeTrack"] = Brush(metal?.GaugeTrack ?? (light ? "#CCD9E5" : "#35404B"));
            var colors = ChartPalettes.Colors(palette, light);
            for (var i = 0; i < AccentKeys.Length; i++) Resources[AccentKeys[i]] = Brush(colors[i]);
            Resources["Accent"] = Brush(light ? "#1469B5" : colors[0]);
        }

        foreach (var readout in new[] { CpuReadout, MemoryReadout, DiskReadout })
        {
            readout.GaugeStyle = style;
            readout.ShowGauge(readout == CpuReadout ? 32.8 : readout == MemoryReadout ? 51.6 : 42.0, 68, 100);
        }

        Chart.UseSeriesPatterns = palette == "Colorblind";
        Chart.InvalidateVisual();
    }

    private static readonly string[] AccentKeys =
        ["CpuAccent", "MemoryAccent", "DiskAccent", "NetworkAccent", "GpuAccent", "SecondaryDiskAccent"];

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static void Configure(MetricReadout readout, string label, string accentKey, double value, string detail)
    {
        readout.IsHitTestVisible = false;
        readout.Label = label;
        readout.AccentKey = accentKey;
        readout.Toggle.Focusable = false;
        readout.ValueLabel.Text = Strings.Format(Strings.PercentOneDecimal, value);
        readout.DetailLabel.Text = detail;
    }

    private static MonitorSample[] Samples()
    {
        var now = DateTimeOffset.UtcNow;
        return Enumerable.Range(0, 121).Select(i =>
        {
            var pulse = Math.Exp(-Math.Pow((i - 87) / 7d, 2));
            return new MonitorSample(now.AddSeconds(i - 120), 32.8 + 40 * pulse, [], 0)
            {
                Resources = new ResourceSample
                {
                    TotalMemoryBytes = 1000, UsedMemoryBytes = (ulong)(516 + 140 * pulse),
                    DiskName = "C:", DiskActivePercent = 15 + 60 * pulse,
                    NetworkId = "preview", ReceiveBytesPerSecond = 100_000 + 5_000_000 * pulse,
                    SendBytesPerSecond = 80_000,
                    GpuId = "preview", GpuPercent = 9 + 55 * pulse
                }
            };
        }).ToArray();
    }
}
