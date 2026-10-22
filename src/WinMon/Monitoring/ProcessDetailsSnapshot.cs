using System.Text;

namespace WinMon.Monitoring;

/// <summary>The row captured when its menu opened, independent of subsequent ranking updates.</summary>
internal sealed record ProcessDetailsSnapshot(ProcessUsage Process, bool Grouped, DateTimeOffset? Timestamp,
    bool Historical, bool Available, string State)
{
    internal string Format()
    {
        var p = Process;
        var text = new StringBuilder();
        void Add(string label, string? value) => text.AppendLine($"{label}: {(string.IsNullOrWhiteSpace(value) ? Strings.MissingReading : value)}");
        Add(Strings.ProcessNameLabel, p.Name);
        Add(Strings.ProcessApplicationLabel, p.AppName);
        Add(Strings.ProcessProductLabel, p.ProductName);
        Add(Strings.ProcessPublisherLabel, p.Publisher);
        Add(Strings.ProcessPathLabel, p.ImagePath);
        Add(Strings.ProcessObservationLabel, Timestamp?.ToLocalTime().ToString("G"));
        Add(Strings.ProcessViewLabel, Historical ? Strings.ProcessHistoricalView : Strings.ProcessCapturedView);
        if (!Available) Add(Strings.ProcessStateLabel, State);
        if (Grouped)
            Add(Strings.ProcessMembersLabel, string.Join(", ", p.MemberIds));
        else
        {
            Add("PID", p.ProcessId.ToString());
            string? started = null;
            try { if (p.CreationTicks > 0) started = DateTime.FromFileTimeUtc(p.CreationTicks).ToLocalTime().ToString("G"); }
            catch (ArgumentOutOfRangeException) { }
            Add(Strings.ProcessStartedLabel, started);
        }
        Add("CPU", Available ? Strings.Format(Strings.PercentOneDecimal, p.CpuPercent) : null);
        Add("GPU", Available && p.GpuPercent is { } gpu ? Strings.Format(Strings.PercentOneDecimal, gpu) : null);
        Add(Strings.ProcessWorkingSetLabel, !Grouped && Available ? Bytes(p.WorkingSetBytes) : null);
        Add(Strings.ProcessPrivateMemoryLabel, Available ? Bytes(p.PrivateBytes) : null);
        Add(Strings.ProcessIoLabel, Available && p.IoBytesPerSecond is { } io ? $"{io / 1_000_000:0.00} MB/s" : null);
        Add(Strings.Threads, Available ? p.ThreadCount?.ToString() : null);
        Add(Strings.Handles, Available ? p.HandleCount?.ToString() : null);
        text.AppendLine().Append(Strings.ProcessMetricsNote);
        return text.ToString();
    }

    private static string? Bytes(ulong? bytes) => bytes is { } value ? Strings.Format(Strings.MiB, value / 1_048_576d) : null;
}
