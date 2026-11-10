using System.Globalization;
using WinMon.Monitoring;
using WinMon.Resources;
using static WinMon.Cli.SnapshotReport;

namespace WinMon.Cli;

public static class ProcessesTab
{
    public static TerminalTable Build(IReadOnlyList<MonitorSample> history, ProcessSort sort, string filter,
        bool grouped, bool appsOnly, bool includeInactive, IReadOnlyList<TerminalRow>? pins = null, bool historical = false,
        IReadOnlySet<string>? savedPins = null)
    {
        var ranking = ProcessRanking.Rank(history, sort: sort, grouped: grouped, includeInactive: includeInactive);
        var store = new ProcessPins();
        foreach (var pin in pins ?? [])
            if (pin.Process is { } process) store.Toggle(process, pin.Grouped);
        var inclusive = store.Count(grouped) == 0 && savedPins?.Count is not > 0 ? ranking :
            ProcessRanking.Rank(history, sort: sort, grouped: grouped, includeInactive: true);
        var resolved = store.Resolve(history.LastOrDefault(), inclusive.Processes, grouped, historical);
        var pinStates = resolved.ToDictionary(pin => Key(pin.Usage, grouped), pin => pin.State, StringComparer.OrdinalIgnoreCase);
        var processes = ProcessSearch.Filter(ranking.Processes, filter, appsOnly)
            .Concat(savedPins?.Count is > 0 ? ProcessSearch.Filter(inclusive.Processes.Where(process =>
                CliPin.Executable(process.ImagePath) is { } identity && savedPins.Contains(identity)), filter, appsOnly) : [])
            .Concat(ProcessSearch.Filter(resolved.Select(pin => pin.Usage), filter, appsOnly))
            .DistinctBy(process => Key(process, grouped), StringComparer.OrdinalIgnoreCase);
        var observedAt = history.LastOrDefault()?.Timestamp;
        var rows = processes.Select(process =>
        {
            var name = string.IsNullOrWhiteSpace(process.AppName) ? process.Name : process.AppName;
            if (pinStates.TryGetValue(Key(process, grouped), out var state) && state != PinnedProcessState.Present)
                name += " · " + (state == PinnedProcessState.Exited ? Strings.ProcessExited :
                    state == PinnedProcessState.NotRecorded ? Strings.ProcessNotRecorded : Strings.Unavailable);
            var ids = process.MemberIds.Count > 0 ? string.Join(", ", process.MemberIds) : process.ProcessId.ToString(CultureInfo.InvariantCulture);
            var memory = grouped ? process.PrivateBytes : process.WorkingSetBytes;
            return new TerminalRow(Key(process, grouped), () => [name, process.MemberIds.Count > 1 ? "×" + process.MemberIds.Count.ToString(CultureInfo.CurrentCulture) : ids, Percent(process.ObservedCpuPercent), Memory(memory), Percent(process.GpuPercent),
                Rate(process.IoBytesPerSecond), Number(process.ThreadCount), Number(process.HandleCount)],
                () => Details(process, ids, observedAt), process, grouped) { PinIdentity = CliPin.Executable(process.ImagePath) };
        }).ToArray();
        return new TerminalTable([Strings.ProcessColumn, grouped ? "PIDs" : "PID", "CPU %",
            grouped ? Strings.Private : Strings.Memory, "GPU %", Strings.IO, Strings.Threads, Strings.Handles], rows);
    }

    private static string Details(ProcessUsage process, string ids, DateTimeOffset? observedAt) =>
        $"{Strings.ProcessObservationLabel}: {observedAt:O}\n" +
        $"{Strings.ProcessNameLabel}: {process.Name}\n{Strings.ProcessApplicationLabel}: {process.AppName}\n" +
        $"{Strings.ProcessPathLabel}: {process.ImagePath}\n{Strings.ProcessMembersLabel}: {ids}\n" +
        $"{Strings.ProcessStartedLabel}: {Started(process)}\n" +
        $"{Strings.CPU}: {Percent(process.ObservedCpuPercent)}\n{Strings.GPU}: {Percent(process.GpuPercent)}\n" +
        $"{Strings.ProcessWorkingSetLabel}: {Memory(process.WorkingSetBytes)}\n" +
        $"{Strings.ProcessPrivateMemoryLabel}: {Memory(process.PrivateBytes)}\n" +
        $"{Strings.ReadWriteIO}: {Rate(process.IoBytesPerSecond)}\n" +
        $"{Strings.Threads}: {Number(process.ThreadCount)}\n{Strings.Handles}: {Number(process.HandleCount)}\n" +
        $"{Strings.ProcessPublisherLabel}: {process.Publisher}\n" +
        $"{Strings.ProcessProductLabel}: {process.ProductName}\n" +
        $"{Strings.ProcessFileVersionLabel}: {process.FileVersion}\n" +
        $"{Strings.ProcessProductVersionLabel}: {process.ProductVersion}";

    private static string Key(ProcessUsage process, bool grouped) => grouped ? "app:" + ProcessRanking.GroupKey(process) :
        $"process:{process.LifetimeDomain}:{process.ProcessId}:{process.CreationTicks}";

    private static string Started(ProcessUsage process)
    {
        var timestamp = process.StartedAtUtc;
        if (timestamp is null && process.MemberCount == 1 && process.LifetimeDomain == ProcessLifetimeIdentity.WindowsFileTime && process.CreationTicks > 0)
            try { timestamp = DateTimeOffset.FromFileTime(process.CreationTicks).ToUniversalTime(); }
            catch (ArgumentOutOfRangeException) { }
        return timestamp?.ToString("O", CultureInfo.InvariantCulture) ?? Strings.Unavailable;
    }

    private static string Number(long? value) => value?.ToString(CultureInfo.CurrentCulture) ?? Strings.Unavailable;
}
