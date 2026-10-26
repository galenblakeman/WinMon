namespace WinMon.Monitoring;

/// <summary>One executable's totals since the ledger started or was reset.</summary>
/// <param name="Key">The Grouped-mode identity, <see cref="ProcessRanking.GroupKey"/>, or <see cref="UsageLedger.OtherAppsKey"/>.</param>
/// <param name="CpuTicks">Processor time across all its processes, in 100-ns ticks.</param>
/// <param name="IoBytes">Read and write bytes, the same counters as the process I/O column.</param>
/// <param name="GpuSeconds">Seconds its busiest GPU engine was busy; null when no GPU reading was ever available.</param>
/// <param name="Lifetimes">Process lifetimes seen.</param>
/// <param name="Running">Lifetimes in the latest valid sample.</param>
/// <param name="IsApp">Whether any of its processes was observed with an app window.</param>
/// <param name="LastActive">The latest valid sample with any CPU, I/O or GPU activity, or null when never active.</param>
public sealed record AppUsage(
    string Key,
    string Name,
    string ImagePath,
    string AppName,
    string Publisher,
    long CpuTicks,
    double IoBytes,
    double? GpuSeconds,
    int Lifetimes,
    int Running,
    bool IsApp,
    SampleKey? LastActive);

/// <summary>Everything a ledger needs to continue after an update restart, including the lifetimes it has counted.</summary>
public sealed record UsageLedgerState(
    DateTimeOffset Since,
    double MachineBusyTicks,
    double AttributedTicks,
    IReadOnlyList<UsageAppState> Apps,
    IReadOnlyList<UsageLifetimeState> Lifetimes);

/// <summary>One executable's saved totals; see <see cref="AppUsage"/>.</summary>
public sealed record UsageAppState(
    string Key,
    string Name,
    string ImagePath,
    string AppName,
    string Publisher,
    double CpuTicks,
    double IoBytes,
    double? GpuSeconds,
    bool IsApp,
    SampleKey? LastActive,
    int Lifetimes);

/// <summary>A counted lifetime, its app as an index into <see cref="UsageLedgerState.Apps"/>, and its absences.</summary>
public readonly record struct UsageLifetimeState(int ProcessId, long CreationTicks, int App, int Absences);

/// <summary>
/// Totals CPU time, I/O and GPU time per executable from live, uncompacted samples, for the Usage tab.
/// </summary>
/// <remarks>
/// Only valid samples count. A gap, a first capture or a bad CPU delta arrives with an empty process list, and
/// reading that as "every process exited" would count still-running processes again on the next sample.
/// A lifetime is identified by (PID, creation time), which is unique for the boot, and is retired only after
/// <see cref="RetireAfterAbsences"/> consecutive valid samples without it. Gaps do not advance that count.
/// </remarks>
public sealed class UsageLedger(DateTimeOffset since)
{
    /// <summary>Consecutive valid samples a lifetime may be missing from before it is forgotten.</summary>
    public const int RetireAfterAbsences = 300;

    /// <summary>Executables tracked individually; later ones share the <see cref="OtherAppsKey"/> row.</summary>
    public const int MaxApps = 4096;

    /// <summary>The key of the row holding executables beyond <see cref="MaxApps"/>; never a group key.</summary>
    public const string OtherAppsKey = "\0other";

    private readonly Dictionary<string, Entry> _apps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int, long), Lifetime> _lifetimes = [];
    private readonly List<(int, long)> _retired = [];
    private long _generation;
    private double _machineBusyTicks;
    private double _attributedTicks;
    private AppUsage[]? _snapshot;

    /// <summary>When the totals started: WinMon's start, or the last <see cref="Reset"/>.</summary>
    public DateTimeOffset Since { get; private set; } = since;

    /// <summary>Machine busy CPU time over the same valid samples; the denominator for every share.</summary>
    public long MachineBusyTicks => (long)Math.Round(_machineBusyTicks);

    /// <summary>Machine busy time not attributed to any app; never negative.</summary>
    public long NotAttributedCpuTicks => (long)Math.Round(Math.Max(0, _machineBusyTicks - _attributedTicks));

    /// <summary>Lifetimes currently remembered, running or awaiting retirement.</summary>
    public int RememberedLifetimes => _lifetimes.Count;

    /// <summary>A stable copy of every app's totals, shared until the next change.</summary>
    public IReadOnlyList<AppUsage> Apps =>
        _snapshot ??= [.. _apps.Values.Select(entry => entry.ToUsage(_generation))];

    /// <summary>Whether a sample's process list describes the machine, rather than a baseline or gap.</summary>
    public static bool IsValid(MonitorSample sample)
    {
        return sample is { IsGap: false, TotalCpuPercent: not null };
    }

    /// <summary>Add one live, uncompacted sample. Invalid samples change nothing.</summary>
    public void Add(MonitorSample full)
    {
        ArgumentNullException.ThrowIfNull(full);
        if (!IsValid(full)) return;
        _generation++;
        _snapshot = null;
        var key = SampleKey.Of(full);
        _machineBusyTicks += full.TotalCpuPercent!.Value / 100 * full.IntervalCpuTicks;
        foreach (var process in full.Processes)
        {
            var id = (process.ProcessId, process.CreationTicks);
            if (!_lifetimes.TryGetValue(id, out var lifetime))
            {
                // Hash the path once per lifetime, not once per second.
                lifetime = new Lifetime(EntryFor(process));
                _lifetimes.Add(id, lifetime);
                lifetime.Entry.Lifetimes++;
            }

            lifetime.Seen = _generation;
            lifetime.Absences = 0;
            var cpu = process.CpuPercent / 100 * full.IntervalCpuTicks;
            _attributedTicks += cpu;
            lifetime.Entry.Observe(process, _generation, key, cpu, full.IntervalSeconds);
        }

        foreach (var (id, lifetime) in _lifetimes)
            if (lifetime.Seen != _generation && ++lifetime.Absences >= RetireAfterAbsences)
                _retired.Add(id);
        foreach (var id in _retired) _lifetimes.Remove(id);
        _retired.Clear();
    }

    /// <summary>Clear every total and start again from <paramref name="now"/>.</summary>
    /// <remarks>Running processes count as new lifetimes again, because the period they are counted in restarted.</remarks>
    public void Reset(DateTimeOffset now)
    {
        _apps.Clear();
        _lifetimes.Clear();
        _machineBusyTicks = 0;
        _attributedTicks = 0;
        _snapshot = null;
        Since = now;
    }

    /// <summary>Copy the totals and counted lifetimes, for an update restart.</summary>
    public UsageLedgerState Export()
    {
        var entries = _apps.Values.ToArray();
        var index = new Dictionary<Entry, int>(entries.Length, ReferenceEqualityComparer.Instance);
        for (var i = 0; i < entries.Length; i++) index[entries[i]] = i;
        return new UsageLedgerState(Since, _machineBusyTicks, _attributedTicks,
            [.. entries.Select(entry => entry.ToState())],
            [
                .. _lifetimes.Select(pair => new UsageLifetimeState(pair.Key.Item1, pair.Key.Item2,
                    index[pair.Value.Entry], pair.Value.Absences))
            ]);
    }

    /// <summary>Replace everything with a saved state, so lifetimes still running are not counted again.</summary>
    /// <remarks>Running counts start at zero; the first valid sample after the restart sets them.</remarks>
    /// <exception cref="ArgumentException">A lifetime refers to an app the state does not contain.</exception>
    public void Restore(UsageLedgerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var entries = state.Apps.Select(Entry.FromState).ToArray();
        // Developer-facing argument validation; this message is not displayed in the UI.
        // ReSharper disable once LocalizableElement
        if (state.Lifetimes.Any(lifetime => (uint)lifetime.App >= (uint)entries.Length))
            throw new ArgumentException("A lifetime refers to a missing app.", nameof(state));
        Reset(state.Since);
        foreach (var entry in entries) _apps[entry.Key] = entry;
        foreach (var lifetime in state.Lifetimes)
            _lifetimes[(lifetime.ProcessId, lifetime.CreationTicks)] =
                new Lifetime(entries[lifetime.App]) { Seen = _generation, Absences = lifetime.Absences };
        _machineBusyTicks = state.MachineBusyTicks;
        _attributedTicks = state.AttributedTicks;
    }

    private Entry EntryFor(ProcessUsage process)
    {
        var key = ProcessRanking.GroupKey(process);
        if (_apps.TryGetValue(key, out var entry)) return entry;
        if (_apps.Count - (_apps.ContainsKey(OtherAppsKey) ? 1 : 0) >= MaxApps)
        {
            if (!_apps.TryGetValue(OtherAppsKey, out var other))
                _apps.Add(OtherAppsKey, other = new Entry(OtherAppsKey, true));
            return other;
        }

        _apps.Add(key, entry = new Entry(key, false));
        return entry;
    }

    private sealed class Lifetime(Entry entry)
    {
        public Entry Entry { get; } = entry;
        public long Seen { get; set; }
        public int Absences { get; set; }
    }

    private sealed class Entry(string key, bool other)
    {
        private string _name = "";
        private string _imagePath = "";
        private string _appName = "";
        private string _publisher = "";
        private double _cpuTicks;
        private double _ioBytes;
        private double? _gpuSeconds;
        private bool _isApp;
        private SampleKey? _lastActive;
        private long _runningGeneration;
        private int _running;

        public string Key => key;

        public int Lifetimes { get; set; }

        public static Entry FromState(UsageAppState state)
        {
            return new Entry(state.Key, state.Key == OtherAppsKey)
            {
                _name = state.Name, _imagePath = state.ImagePath, _appName = state.AppName,
                _publisher = state.Publisher, _cpuTicks = state.CpuTicks, _ioBytes = state.IoBytes,
                _gpuSeconds = state.GpuSeconds, _isApp = state.IsApp, _lastActive = state.LastActive,
                Lifetimes = state.Lifetimes
            };
        }

        public UsageAppState ToState()
        {
            return new UsageAppState(key, _name, _imagePath, _appName, _publisher, _cpuTicks, _ioBytes, _gpuSeconds,
                _isApp, _lastActive, Lifetimes);
        }

        public void Observe(ProcessUsage process, long generation, SampleKey sample, double cpuTicks,
            double seconds)
        {
            if (_runningGeneration != generation)
            {
                _runningGeneration = generation;
                _running = 0;
            }

            _running++;
            // The Other row mixes executables, so it keeps no single name.
            if (!other)
            {
                _name = process.Name;
                if (process.ImagePath.Length > 0) _imagePath = process.ImagePath;
                if (process.AppName.Length > 0) _appName = process.AppName;
                if (process.Publisher.Length > 0) _publisher = process.Publisher;
            }

            _isApp |= process.HasAppWindow == true || process.HasHadAppWindow;
            _cpuTicks += cpuTicks;
            var io = process.IoBytesPerSecond is { } rate ? rate * seconds : 0;
            _ioBytes += io;
            var gpu = process.GpuPercent is { } percent ? percent / 100 * seconds : (double?)null;
            if (gpu is not null) _gpuSeconds = (_gpuSeconds ?? 0) + gpu.Value;
            if (cpuTicks > 0 || io > 0 || gpu > 0) _lastActive = sample;
        }

        public AppUsage ToUsage(long latest)
        {
            return new AppUsage(key, _name, _imagePath, _appName, _publisher, (long)Math.Round(_cpuTicks), _ioBytes,
                _gpuSeconds, Lifetimes, _runningGeneration == latest ? _running : 0, _isApp, _lastActive);
        }
    }
}