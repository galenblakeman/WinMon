using System.Text;

namespace WinMon.Monitoring;

/// <summary>The row captured when its menu opened, independent of subsequent ranking updates.</summary>
internal sealed record ProcessDetailsSnapshot(
    ProcessUsage Process,
    bool Grouped,
    DateTimeOffset? Timestamp,
    bool Historical,
    bool Available,
    string State)
{
    internal string Format()
    {
        var text = new StringBuilder();
        Add(Strings.ProcessNameLabel, Process.Name);
        Add(Strings.ProcessApplicationLabel, Process.AppName);
        Add(Strings.ProcessProductLabel, Process.ProductName);
        Add(Strings.ProcessPublisherLabel, Process.Publisher);
        Add(Strings.ProcessPathLabel, Process.ImagePath);
        Add(Strings.ProcessObservationLabel, Timestamp?.ToLocalTime().ToString("G"));
        Add(Strings.ProcessViewLabel, Historical ? Strings.ProcessHistoricalView : Strings.ProcessCapturedView);
        if (!Available) Add(Strings.ProcessStateLabel, State);
        if (Grouped)
        {
            Add(Strings.ProcessMembersLabel, string.Join(", ", Process.MemberIds));
        }
        else
        {
            Add("PID", Process.ProcessId.ToString());
            string? started = null;
            try
            {
                if (Process.CreationTicks > 0)
                    started = DateTime.FromFileTimeUtc(Process.CreationTicks).ToLocalTime().ToString("G");
            }
            catch (ArgumentOutOfRangeException)
            {
            }

            Add(Strings.ProcessStartedLabel, started);
        }

        Add("CPU", Available ? Strings.Format(Strings.PercentOneDecimal, Process.CpuPercent) : null);
        Add("GPU", Available && Process.GpuPercent is { } gpu ? Strings.Format(Strings.PercentOneDecimal, gpu) : null);
        Add(Strings.ProcessWorkingSetLabel, !Grouped && Available ? Bytes(Process.WorkingSetBytes) : null);
        Add(Strings.ProcessPrivateMemoryLabel, Available ? Bytes(Process.PrivateBytes) : null);
        Add(Strings.ProcessIoLabel,
            Available && Process.IoBytesPerSecond is { } io ? $"{io / 1_000_000:0.00} MB/s" : null);
        Add(Strings.Threads, Available ? Process.ThreadCount?.ToString() : null);
        Add(Strings.Handles, Available ? Process.HandleCount?.ToString() : null);
        text.AppendLine().Append(Strings.ProcessMetricsNote);
        return text.ToString();

        void Add(string label, string? value)
        {
            text.AppendLine($"{label}: {(string.IsNullOrWhiteSpace(value) ? Strings.MissingReading : value)}");
        }
    }

    private static string? Bytes(ulong? bytes)
    {
        return bytes is { } value ? Strings.Format(Strings.MiB, value / 1_048_576d) : null;
    }
}