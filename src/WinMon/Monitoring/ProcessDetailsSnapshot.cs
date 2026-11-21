using System.Text;

namespace WinMon.Monitoring;

/// <summary>The row captured when its menu opened, independent of subsequent ranking updates.</summary>
internal sealed record ProcessDetailsSnapshot(
    ProcessUsage Process,
    bool Grouped,
    DateTimeOffset? Timestamp,
    bool Historical,
    bool Available,
    string State,
    SampleKey? Sample = null,
    ProcessInspection? Inspection = null)
{
    internal string Format(bool reading = false)
    {
        var text = new StringBuilder();
        var exited = Inspection?.Exited == true;
        Add(Strings.ProcessNameLabel, Process.Name);
        Add(Strings.ProcessApplicationLabel, Process.AppName);
        Add(Strings.ProcessProductLabel, Process.ProductName);
        Add(Strings.ProcessFileVersionLabel, Process.FileVersion);
        Add(Strings.ProcessProductVersionLabel, Process.ProductVersion);
        Add(Strings.ProcessPublisherLabel, Process.Publisher);
        Add(Strings.ProcessPathLabel, Process.ImagePath);
        Add(Strings.ProcessObservationLabel, Timestamp?.ToLocalTime().ToString("G"));
        Add(Strings.ProcessViewLabel, Historical ? Strings.ProcessHistoricalView : Strings.ProcessCapturedView);
        if (!Available || exited) Add(Strings.ProcessStateLabel, exited ? Strings.ProcessExited : State);
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

        if (Grouped || Historical || !Available || exited)
        {
            Add(Strings.ProcessCommandLineLabel, Strings.ProcessDetailsUnavailableForView);
            Add(Strings.ProcessFileSignedByLabel, Strings.ProcessDetailsUnavailableForView);
            Add(Strings.ProcessUserLabel, Strings.ProcessDetailsUnavailableForView);
        }
        else if (reading)
        {
            Add(Strings.ProcessCommandLineLabel, Strings.ProcessReadingDetails);
            Add(Strings.ProcessFileSignedByLabel, Strings.ProcessReadingDetails);
            Add(Strings.ProcessUserLabel, Strings.ProcessReadingDetails);
        }
        else if (Inspection is { } inspection)
        {
            Add(Strings.ProcessCommandLineLabel, inspection.CommandLine);
            Add(Strings.ProcessFileSignedByLabel, Signature(inspection.Signature));
            Add(Strings.ProcessUserLabel, Owner(inspection.Owner));
        }
        else
        {
            Add(Strings.ProcessCommandLineLabel, null);
            Add(Strings.ProcessFileSignedByLabel, null);
            Add(Strings.ProcessUserLabel, null);
        }

        Add("CPU", Available ? Strings.Format(Strings.PercentOneDecimal, Process.CpuPercent) : null);
        Add("GPU", Available && Process.GpuPercent is { } gpu ? Strings.Format(Strings.PercentOneDecimal, gpu) : null);
        Add(Strings.ProcessWorkingSetLabel, !Grouped && Available ? Bytes(Process.WorkingSetBytes) : null);
        Add(Strings.ProcessPrivateMemoryLabel, Available ? Bytes(Process.PrivateBytes) : null);
        Add(Strings.ProcessIoLabel,
            Available && Process.IoBytesPerSecond is { } io ? $"{io / 1_000_000:0.0} MB/s" : null);
        Add(Strings.Threads, Available ? Process.ThreadCount?.ToString() : null);
        Add(Strings.Handles, Available ? Process.HandleCount?.ToString() : null);
        text.AppendLine().Append(Strings.ProcessMetricsNote);
        return text.ToString();

        void Add(string label, string? value)
        {
            text.AppendLine($"{label}: {(string.IsNullOrWhiteSpace(value) ? Strings.MissingReading : value)}");
        }

        static string? Signature(ProcessSignature signature)
        {
            return signature.Status switch
            {
                ProcessSignatureStatus.Verified when !string.IsNullOrWhiteSpace(signature.Signer) =>
                    Strings.Format(Strings.ProcessSignatureVerified, signature.Signer),
                ProcessSignatureStatus.NotSigned => Strings.ProcessSignatureNotSigned,
                ProcessSignatureStatus.Invalid => Strings.ProcessSignatureInvalid,
                _ => null
            };
        }

        static string? Owner(ProcessOwner? owner)
        {
            if (owner is null) return null;
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(owner.AccountName)) parts.Add(owner.AccountName);
            if (owner.SessionId is { } session) parts.Add(Strings.Format(Strings.ProcessSession, session));
            if (owner.Elevated is { } elevated) parts.Add(elevated ? Strings.ProcessElevated : Strings.ProcessNotElevated);
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }
    }

    private static string? Bytes(ulong? bytes)
    {
        return bytes is { } value ? Strings.Format(Strings.MiB, value / 1_048_576d) : null;
    }
}
