namespace WinMon.Monitoring;

/// <summary>A bounded record of uncompacted usage intervals for Capture-mode period totals.</summary>
public sealed class UsageHistory
{
    /// <summary>Per-process observations retained for Capture-mode usage totals.</summary>
    private sealed record Owner(ProcessLifetimeIdentity Identity, string Key, string Name, string ImagePath,
        string AppName, string Publisher, bool IsApp);

    private readonly record struct Observation(Owner Owner, double CpuTicks, double IoBytes, double? GpuSeconds);

    private sealed record Frame(SampleKey Key, double IntervalSeconds, bool Valid, double MachineBusyTicks,
        double AttributedTicks, Observation[] Observations)
    {
        public double StartMoment => Key.Moment - IntervalSeconds;
    }

    private readonly Queue<Frame> _frames = [];
    private readonly int _entryBudget;
    private readonly ProcessIdentityPolicy _identityPolicy;
    private Dictionary<ProcessLifetimeIdentity, Owner> _owners = [];
    private Dictionary<ProcessLifetimeIdentity, Owner> _nextOwners = [];
    private int _entries;
    private long _trimGeneration;
    private (SampleKey End, int RangeSeconds, long TrimGeneration, UsagePeriod? Period)? _rangeCache;

    /// <summary>Create history bounded by elapsed seconds and complete process observations.</summary>
    public UsageHistory(int capacity = 600, int entryBudget = 500_000, ProcessIdentityPolicy? identityPolicy = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(entryBudget, 1);
        Capacity = capacity;
        _entryBudget = entryBudget;
        _identityPolicy = identityPolicy ?? ProcessIdentityPolicy.Windows;
    }

    /// <summary>Maximum retained elapsed seconds and frame count.</summary>
    public int Capacity { get; private set; }

    /// <summary>Whether the entry budget shortened the available recording.</summary>
    public bool WasMemoryLimited { get; private set; }

    /// <summary>Record one uncompacted live sample, including gaps so period coverage remains honest.</summary>
    public void Add(MonitorSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(sample.Processes);
        if (sample.Processes.Count > _entryBudget)
        {
            // The live ledger still receives the full sample; this bounded Capture history records the coverage gap.
            WasMemoryLimited = true;
            _frames.Enqueue(new Frame(SampleKey.Of(sample), 0, false, 0, 0, []));
            Trim(SampleKey.Of(sample));
            return;
        }

        var valid = UsageLedger.IsValid(sample);
        var observations = valid ? new Observation[sample.Processes.Count] : [];
        var attributed = 0d;
        _nextOwners.Clear();
        for (var i = 0; i < observations.Length; i++)
        {
            var process = sample.Processes[i];
            var cpu = process.CpuAvailable ? process.CpuPercent / 100 * sample.IntervalCpuTicks : 0;
            var io = process.IoBytesPerSecond is { } rate ? rate * sample.IntervalSeconds : 0;
            var gpu = process.GpuPercent is { } percent ? percent / 100 * sample.IntervalSeconds : (double?)null;
            attributed += cpu;
            var isApp = process.HasAppWindow == true || process.HasHadAppWindow;
            if (!_owners.TryGetValue(process.Identity, out var owner) || owner.Name != process.Name ||
                owner.ImagePath != process.ImagePath || owner.AppName != process.AppName ||
                owner.Publisher != process.Publisher || owner.IsApp != isApp)
                owner = new Owner(process.Identity, _identityPolicy.ExecutableKey(process), process.Name,
                    process.ImagePath, process.AppName, process.Publisher, isApp);
            _nextOwners[process.Identity] = owner;
            observations[i] = new Observation(owner, cpu, io, gpu);
        }
        (_owners, _nextOwners) = (_nextOwners, _owners);
        _nextOwners.Clear();

        var busy = valid ? (sample.TotalCpuPercent ?? 0) / 100 * sample.IntervalCpuTicks : 0;
        _frames.Enqueue(new Frame(SampleKey.Of(sample), Math.Max(0, sample.IntervalSeconds), valid, busy, attributed,
            observations));
        _entries += observations.Length;
        Trim(SampleKey.Of(sample));
    }

    /// <summary>Change retention and immediately discard older intervals.</summary>
    public void Resize(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        if (Capacity == capacity) return;
        Capacity = capacity;
        if (_frames.Count > 0) Trim(_frames.Last().Key);
    }

    /// <summary>Return the retained portion of the visible chart range ending at <paramref name="end"/>.</summary>
    /// <remarks>
    /// A missing selected endpoint returns null. A selected endpoint remains queryable when the requested range begins
    /// before retained history; <see cref="UsagePeriod.Complete"/> then reports the partial coverage.
    /// </remarks>
    public UsagePeriod? Query(MonitorSample end, int rangeSeconds)
    {
        ArgumentNullException.ThrowIfNull(end);
        ArgumentOutOfRangeException.ThrowIfNegative(rangeSeconds);
        var key = SampleKey.Of(end);
        if (_rangeCache is { } cache && cache.End.Equals(key) && cache.RangeSeconds == rangeSeconds &&
            cache.TrimGeneration == _trimGeneration)
            return cache.Period;

        var startMoment = key.Moment - rangeSeconds;
        // A sample reports the interval ending at its timestamp. Exclude the interval ending exactly at the range start.
        var frames = _frames.Where(frame => frame.Key.Moment > startMoment && frame.Key.Moment <= key.Moment).ToArray();
        var period = frames.Length == 0 || !frames[^1].Key.Equals(key)
            ? null
            : BuildPeriod(frames, frames[0].StartMoment <= startMoment);
        _rangeCache = (key, rangeSeconds, _trimGeneration, period);
        return period;
    }

    private UsagePeriod BuildPeriod(IReadOnlyList<Frame> frames, bool complete)
    {
        var entries = new Dictionary<string, Entry>(_identityPolicy.Comparer);
        var busy = 0d;
        var attributed = 0d;
        var end = frames[^1].Key;
        Frame? previous = null;
        foreach (var frame in frames)
        {
            complete &= frame.Valid;
            if (previous is not null && frame.StartMoment > previous.Key.Moment + 0.000001) complete = false;
            busy += frame.MachineBusyTicks;
            attributed += frame.AttributedTicks;
            foreach (var observation in frame.Observations)
            {
                if (!entries.TryGetValue(observation.Owner.Key, out var entry))
                {
                    var key = entries.Count >= UsageLedger.MaxApps ? UsageLedger.OtherAppsKey : observation.Owner.Key;
                    if (!entries.TryGetValue(key, out entry)) entries.Add(key, entry = new Entry(key));
                }
                entry.Add(observation, frame.Key, frame.Key.Equals(end));
            }
            previous = frame;
        }

        return new UsagePeriod([.. entries.Values.Select(entry => entry.ToUsage())], (long)Math.Round(busy),
            (long)Math.Round(Math.Max(0, busy - attributed)),
            frames[0].Key.Timestamp - TimeSpan.FromSeconds(frames[0].IntervalSeconds), frames[^1].Key.Timestamp,
            complete);
    }

    private void Trim(SampleKey latest)
    {
        while (_frames.Count > 0)
        {
            var oldest = _frames.Peek();
            var age = latest.Moment - oldest.Key.Moment;
            var overBudget = _entries > _entryBudget;
            if (_frames.Count <= Capacity && age < Capacity && !overBudget) break;
            WasMemoryLimited |= overBudget;
            _entries -= _frames.Dequeue().Observations.Length;
            _trimGeneration++;
        }
    }

    private sealed class Entry(string key)
    {
        private string _name = "";
        private string _imagePath = "";
        private string _appName = "";
        private string _publisher = "";
        private double _cpu;
        private double _io;
        private double? _gpu;
        private int _lifetimes;
        private int _running;
        private bool _isApp;
        private SampleKey? _lastActive;
        private readonly HashSet<ProcessLifetimeIdentity> _lifetimesSeen = [];

        public void Add(Observation observation, SampleKey sample, bool atEnd)
        {
            var owner = observation.Owner;
            if (_lifetimesSeen.Add(owner.Identity)) _lifetimes++;
            if (atEnd) _running++;
            _name = owner.Name;
            if (owner.ImagePath.Length > 0) _imagePath = owner.ImagePath;
            if (owner.AppName.Length > 0) _appName = owner.AppName;
            if (owner.Publisher.Length > 0) _publisher = owner.Publisher;
            _isApp |= owner.IsApp;
            _cpu += observation.CpuTicks;
            _io += observation.IoBytes;
            if (observation.GpuSeconds is { } gpu) _gpu = (_gpu ?? 0) + gpu;
            if (observation.CpuTicks > 0 || observation.IoBytes > 0 || observation.GpuSeconds > 0)
                _lastActive = sample;
        }

        public AppUsage ToUsage() => new(key, key == UsageLedger.OtherAppsKey ? "" : _name,
            key == UsageLedger.OtherAppsKey ? "" : _imagePath,
            key == UsageLedger.OtherAppsKey ? "" : _appName,
            key == UsageLedger.OtherAppsKey ? "" : _publisher, (long)Math.Round(_cpu), _io, _gpu, _lifetimes,
            _running, _isApp, _lastActive);
    }
}

/// <summary>Usage totals over a retained Capture-mode period.</summary>
public sealed record UsagePeriod(IReadOnlyList<AppUsage> Apps, long MachineBusyTicks, long NotAttributedTicks,
    DateTimeOffset Since, DateTimeOffset Latest, bool Complete);
