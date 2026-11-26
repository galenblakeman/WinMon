using System.Windows;
using System.Windows.Controls;
using MonCore.Monitoring;
using WinMon.Controls;

namespace WinMon.Preferences;

public partial class SensorReadingsView : UserControl
{
    public SensorReadingsView() => InitializeComponent();

    internal void Update(ResourceSample? sample, TemperatureUnit unit)
    {
        var rows = new List<SensorRow>();
        if (Monitoring.SensorSetupBroker.Supported)
            AddTemperature(Strings.DetailCpuTemperature, null, sample?.CpuTemperature?.Celsius);

        var adapters = sample?.GpuThermals ?? [];
        foreach (var adapter in adapters)
        {
            if (adapter.TemperatureCelsius is { } temperature)
                AddTemperature(Strings.SensorGpuTemperature, adapter.Name, temperature);
            if (adapter.PowerPercent is { } power)
                rows.Add(new SensorRow(Strings.SensorGpuPower, adapter.Name, MainWindow.Percent(power)));
        }

        if (!adapters.Any(adapter => adapter.TemperatureCelsius.HasValue))
            AddTemperature(Strings.SensorGpuTemperature, null, null);
        if (!adapters.Any(adapter => adapter.PowerPercent.HasValue))
            rows.Add(new SensorRow(Strings.SensorGpuPower, null, Strings.Unavailable));
        if (sample?.DiskTemperatureCelsius is { } primary)
            AddTemperature(Strings.DetailTemperature, Strings.Format(Strings.DiskLabel, sample.DiskName), primary);
        if (sample?.SecondaryDiskTemperatureCelsius is { } secondary)
            AddTemperature(Strings.DetailTemperature, Strings.Format(Strings.DiskLabel, sample.SecondaryDiskName), secondary);

        if (Readings.ItemsSource is not List<SensorRow> previous || !previous.SequenceEqual(rows))
            Readings.ItemsSource = rows;

        void AddTemperature(string label, string? adapter, double? celsius) =>
            rows.Add(new SensorRow(label, adapter, GaugeReadoutText.Temperature(celsius, unit)));
    }

    private sealed record SensorRow(string Label, string? Adapter, string Reading)
    {
        public Visibility AdapterVisibility => Adapter is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
