namespace WinMon.Monitoring;

/// <summary>A PID plus an opaque start token in an explicitly named clock domain. Valid only on the originating machine/boot.</summary>
public readonly record struct ProcessLifetimeIdentity
{
    public ProcessLifetimeIdentity(int processId, long startToken, ProcessLifetimeDomain domain)
    {
        ProcessId = processId;
        StartToken = startToken;
        Domain = domain;
    }

    // Field order keeps the identity at 16 bytes, matching the old PID/start tuple's size.
    public long StartToken { get; }
    public int ProcessId { get; }
    public ProcessLifetimeDomain Domain { get; }
    public const ProcessLifetimeDomain WindowsFileTime = ProcessLifetimeDomain.WindowsFileTime;
    public const ProcessLifetimeDomain UnixMicroseconds = ProcessLifetimeDomain.UnixMicroseconds;

    public bool Equals(ProcessLifetimeIdentity other) => ProcessId == other.ProcessId &&
        StartToken == other.StartToken && Domain == other.Domain;

    public override int GetHashCode() => Domain == WindowsFileTime
        ? HashCode.Combine(ProcessId, StartToken) : HashCode.Combine(ProcessId, StartToken, Domain);
}

/// <summary>Explicit interpretation of an adapter's opaque lifetime token; never compare tokens across domains.</summary>
public enum ProcessLifetimeDomain
{
    WindowsFileTime,
    UnixMicroseconds
}

/// <summary>Adapter-selected executable keys and equality, shared by ranking, pins, selection and usage.</summary>
public sealed class ProcessIdentityPolicy
{
    public ProcessIdentityPolicy(string id, StringComparer comparer, Func<ProcessUsage, string> executableKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(comparer);
        ArgumentNullException.ThrowIfNull(executableKey);
        Id = id;
        Comparer = comparer;
        ExecutableKey = executableKey;
    }

    public string Id { get; }
    public StringComparer Comparer { get; }
    public Func<ProcessUsage, string> ExecutableKey { get; }

    /// <summary>Preserves the original Windows path/name grouping and comparison behavior.</summary>
    public static ProcessIdentityPolicy Windows { get; } = new("windows", StringComparer.OrdinalIgnoreCase,
        process => process.ImagePath.Length > 0 ? process.ImagePath : process.Name);

    /// <summary>
    /// Conservative exact-path policy: distinct case spellings never merge. Unreadable paths group only by lifetime.
    /// Adapters may instead supply canonical file identities for volumes where aliases should merge.
    /// </summary>
    public static ProcessIdentityPolicy ExactPaths { get; } = new("exact-paths", StringComparer.Ordinal,
        process => process.ImagePath.Length > 0 ? "path:" + process.ImagePath :
            $"lifetime:{process.LifetimeDomain}:{process.ProcessId}:{process.CreationTicks}");
}
