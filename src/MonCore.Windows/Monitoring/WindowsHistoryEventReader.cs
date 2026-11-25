using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

/// <summary>Reads a small, curated set of Windows events for a frozen history range.</summary>
public sealed class WindowsHistoryEventReader
{
    internal const int MaximumEvents = 500;
    internal const int MaximumCrashEvents = 2_000;
    private const int XmlBytes = 128 * 1024, MessageCharacters = 64 * 1024;
    private const int ErrorNoMoreItems = 259, ErrorInsufficientBuffer = 122, ErrorTimeout = 1460;
    private const uint EvtQueryChannelPath = 1, EvtQueryForwardDirection = 0x100, EvtRenderEventXml = 1, EvtFormatMessageEvent = 1;
    private static readonly (string Provider, int[] Ids)[] ApplicationSelectors =
    [("Application Error", [1000]), ("Application Hang", [1002]), (".NET Runtime", [1026])];
    private static readonly (string Provider, int[] Ids)[] SystemSelectors =
    [("Service Control Manager", [7031, 7034]), ("Microsoft-Windows-Resource-Exhaustion-Detector", [2004]),
        ("disk", [7, 51, 153]), ("Ntfs", [55, 98]), ("Display", [4101]),
        ("Microsoft-Windows-WHEA-Logger", [17, 18, 19, 47]), ("Microsoft-Windows-Kernel-Processor-Power", [37]),
        ("Microsoft-Windows-WindowsUpdateClient", [19, 20, 43])];

    /// <summary>Reads Application and System synchronously on the calling worker thread.</summary>
    public IncidentEventResult Read(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken) =>
        Read(startUtc, endUtc, TimeSpan.FromSeconds(5), cancellationToken);

    /// <summary>Reads only structured application crash and hang events for diagnostics clients.</summary>
    public CrashEventResult ReadCrashes(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken) =>
        ReadCrashes(startUtc, endUtc, TimeSpan.FromSeconds(5), cancellationToken);

    internal IncidentEventResult Read(DateTimeOffset startUtc, DateTimeOffset endUtc, TimeSpan budget, CancellationToken cancellationToken)
    {
        if (endUtc < startUtc) throw new ArgumentOutOfRangeException(nameof(endUtc));
        if (budget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(budget));
        cancellationToken.ThrowIfCancellationRequested();
        var started = Stopwatch.StartNew();
        return ReadCore(ReadNativeLog, cancellationToken);

        IEnumerable<IncidentEvent> ReadNativeLog(string log, CancellationToken token) =>
            ReadLog(log, startUtc, endUtc, started, budget, token);
    }

    internal CrashEventResult ReadCrashes(DateTimeOffset startUtc, DateTimeOffset endUtc, TimeSpan budget,
        CancellationToken cancellationToken)
    {
        if (endUtc < startUtc) throw new ArgumentOutOfRangeException(nameof(endUtc));
        if (budget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(budget));
        cancellationToken.ThrowIfCancellationRequested();
        var started = Stopwatch.StartNew();
        return ReadCrashesCore(ReadNativeLog, ReadOldest, cancellationToken);

        IEnumerable<IncidentEvent> ReadNativeLog(CancellationToken token) =>
            ReadQuery(BuildCrashQuery(startUtc, endUtc), started, budget, token, includeMessage: false);
        DateTimeOffset? ReadOldest(CancellationToken token) => ReadOldestApplicationEvent(started, budget, token);
    }

    internal static CrashEventResult ReadCrashesCore(CrashEventReader reader,
        OldestApplicationEventReader oldestReader, CancellationToken cancellationToken)
    {
        var events = new List<IncidentEvent>(MaximumCrashEvents);
        var status = IncidentEventCoverageStatus.Complete;
        try
        {
            foreach (var item in reader(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                events.Add(item);
                if (events.Count < MaximumCrashEvents) continue;
                status = IncidentEventCoverageStatus.Unknown;
                break;
            }
        }
        catch (TimeoutException) { status = IncidentEventCoverageStatus.TimedOut; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or XmlException or InvalidDataException)
        { status = IncidentEventCoverageStatus.Unreadable; }

        DateTimeOffset? oldest = null;
        if (status is IncidentEventCoverageStatus.Complete or IncidentEventCoverageStatus.Unknown)
        {
            try { oldest = oldestReader(cancellationToken); }
            catch (TimeoutException) { status = IncidentEventCoverageStatus.TimedOut; }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or XmlException or InvalidDataException)
            { status = IncidentEventCoverageStatus.Unreadable; }
        }
        return new CrashEventResult(events, new IncidentEventLogCoverage("Application", status),
            events.Count == MaximumCrashEvents, oldest);
    }

    internal static IncidentEventResult ReadCore(IncidentEventLogReader reader, CancellationToken cancellationToken)
    {
        var events = new List<IncidentEvent>(MaximumEvents);
        var coverage = new List<IncidentEventLogCoverage>(2);
        var limit = false;
        foreach (var log in new[] { "Application", "System" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (events.Count == MaximumEvents)
            {
                coverage.Add(new IncidentEventLogCoverage(log, IncidentEventCoverageStatus.Unknown));
                limit = true;
                continue;
            }
            var status = IncidentEventCoverageStatus.Complete;
            try
            {
                foreach (var item in reader(log, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    events.Add(item);
                    if (events.Count < MaximumEvents) continue;
                    limit = true;
                    status = IncidentEventCoverageStatus.Unknown;
                    break;
                }
            }
            catch (TimeoutException) { status = IncidentEventCoverageStatus.TimedOut; }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or XmlException or InvalidDataException)
            { status = IncidentEventCoverageStatus.Unreadable; }
            coverage.Add(new IncidentEventLogCoverage(log, status));
        }
        return new IncidentEventResult(events, coverage, limit);
    }

    internal delegate IEnumerable<IncidentEvent> IncidentEventLogReader(string log, CancellationToken cancellationToken);
    internal delegate IEnumerable<IncidentEvent> CrashEventReader(CancellationToken cancellationToken);
    internal delegate DateTimeOffset? OldestApplicationEventReader(CancellationToken cancellationToken);

    internal static string BuildQuery(string log, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var selectors = log switch { "Application" => ApplicationSelectors, "System" => SystemSelectors, _ => throw new ArgumentOutOfRangeException(nameof(log)) };
        var start = startUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        var end = endUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        var time = $"TimeCreated[@SystemTime &gt;= '{start}' and @SystemTime &lt;= '{end}']";
        var builder = new StringBuilder("<QueryList><Query Id=\"0\" Path=\"").Append(log).Append("\">");
        foreach (var (provider, ids) in selectors)
        {
            var eventIds = string.Join(" or ", ids.Select(id => "EventID=" + id.ToString(CultureInfo.InvariantCulture)));
            builder.Append("<Select Path=\"").Append(log).Append("\">*[System[Provider[@Name='")
                .Append(SecurityElement.Escape(provider)).Append("'] and (").Append(eventIds).Append(") and ").Append(time).Append("]]</Select>");
        }
        if (log == "System") builder.Append("<Select Path=\"System\">*[System[EventID=129 and ").Append(time).Append("]]</Select>");
        return builder.Append("</Query></QueryList>").ToString();
    }

    internal static string BuildCrashQuery(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var start = startUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        var end = endUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        var time = $"TimeCreated[@SystemTime &gt;= '{start}' and @SystemTime &lt;= '{end}']";
        return "<QueryList><Query Id=\"0\" Path=\"Application\">" +
            $"<Select Path=\"Application\">*[System[((Provider[@Name='Application Error'] and EventID=1000) or (Provider[@Name='Application Hang'] and EventID=1002)) and {time}]]</Select>" +
            "</Query></QueryList>";
    }

    private static IEnumerable<IncidentEvent> ReadLog(string log, DateTimeOffset startUtc, DateTimeOffset endUtc, Stopwatch started,
        TimeSpan budget, CancellationToken cancellationToken)
    {
        return ReadQuery(BuildQuery(log, startUtc, endUtc), started, budget, cancellationToken, includeMessage: true);
    }

    private static IEnumerable<IncidentEvent> ReadQuery(string queryText, Stopwatch started, TimeSpan budget,
        CancellationToken cancellationToken, bool includeMessage)
    {
        if (started.Elapsed >= budget) throw new TimeoutException();
        using var query = EvtQuery(IntPtr.Zero, null, queryText, EvtQueryChannelPath | EvtQueryForwardDirection);
        if (query.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var raw = new nint[1];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (started.Elapsed >= budget) throw new TimeoutException();
            if (!EvtNext(query, 1, raw, 50, 0, out var returned))
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == ErrorNoMoreItems) yield break;
                if (error == ErrorTimeout) continue;
                throw new Win32Exception(error);
            }
            if (returned != 1 || raw[0] == 0)
            {
                if (raw[0] != 0) EvtClose(raw[0]);
                throw new InvalidDataException("Invalid Windows event handle.");
            }
            using var item = new SafeEvtHandle(raw[0]);
            raw[0] = 0;
            if (!TryReadEvent(item, includeMessage, out var parsed)) throw new InvalidDataException("Unreadable Windows event.");
            cancellationToken.ThrowIfCancellationRequested();
            if (started.Elapsed >= budget) throw new TimeoutException();
            yield return parsed;
        }
    }

    private static DateTimeOffset? ReadOldestApplicationEvent(Stopwatch started, TimeSpan budget,
        CancellationToken cancellationToken)
    {
        const string queryText = "<QueryList><Query Id=\"0\" Path=\"Application\"><Select Path=\"Application\">*[System[TimeCreated]]</Select></Query></QueryList>";
        using var enumerator = ReadQuery(queryText, started, budget, cancellationToken, includeMessage: false).GetEnumerator();
        return enumerator.MoveNext() ? enumerator.Current.Timestamp : null;
    }

    private static bool TryReadEvent(SafeEvtHandle item, bool includeMessage, out IncidentEvent result)
    {
        result = default!;
        var xml = RenderXml(item);
        if (xml is null) return false;
        if (!TryParseXml(xml, "", out var parsed)) return false;
        result = includeMessage ? parsed with { Message = FirstLine(FormatMessage(item, parsed.Provider)) } : parsed;
        return true;
    }

    internal static bool TryParseXml(string xml, string message, out IncidentEvent result)
    {
        result = default!;
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = XmlBytes / sizeof(char)
        });
        var document = XDocument.Load(reader, LoadOptions.None);
        var system = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "System");
        if (system is null || !TryReadSystem(system, out var provider, out var id, out var timestamp)) return false;
        var level = ReadLevel(system);
        var hasStructuredFailureData = (provider == "Application Error" && id == 1000) ||
            (provider == "Application Hang" && id == 1002);
        var eventData = hasStructuredFailureData ? ReadEventData(document) : EventDataFields.Empty;
        ReadProcessLifetime(eventData, provider, id, out var pid, out var creation);
        var signature = ReadCrashSignature(eventData, provider, id);
        result = new IncidentEvent(timestamp, provider!, id, level, FirstLine(message), pid, creation, signature);
        return true;
    }

    private static CrashSignature? ReadCrashSignature(EventDataFields fields, string? provider, int id)
    {
        var kind = provider == "Application Error" && id == 1000 ? CrashKind.Crash :
            provider == "Application Hang" && id == 1002 ? CrashKind.Hang : (CrashKind?)null;
        if (kind is null) return null;
        if (fields.DuplicateNames) return null;
        var named = fields.Named.Count > 0;
        var applicationText = named ? fields.Named.GetValueOrDefault("AppName") :
            kind == CrashKind.Hang && fields.Values.Count == 10 ? fields.Values[0] : null;
        var versionText = named ? fields.Named.GetValueOrDefault("AppVersion") :
            kind == CrashKind.Hang && fields.Values.Count == 10 ? fields.Values[1] : null;
        if (!TryReadName(applicationText, out var application) || !TryReadVersion(versionText, out var applicationVersion)) return null;
        if (kind == CrashKind.Hang)
        {
            var signature = new CrashSignature(kind.Value, application, applicationVersion);
            return CrashSignatures.IsValid(signature) ? signature : null;
        }
        if (!TryReadName(fields.Named.GetValueOrDefault("ModuleName"), out var module) ||
            !TryReadVersion(fields.Named.GetValueOrDefault("ModuleVersion"), out var moduleVersion) ||
            !TryParseUnsignedHex(fields.Named.GetValueOrDefault("ExceptionCode"), uint.MaxValue, out var exceptionCode) ||
            !TryParseUnsignedHex(fields.Named.GetValueOrDefault("FaultingOffset"), ulong.MaxValue, out var offset)) return null;
        var crash = new CrashSignature(kind.Value, application, applicationVersion, module, moduleVersion, (uint)exceptionCode, offset);
        return CrashSignatures.IsValid(crash) ? crash : null;
    }

    private static EventDataFields ReadEventData(XDocument document)
    {
        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        var values = new List<string>();
        var duplicateNames = false;
        int? processId = null;
        long? processCreation = null;
        foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "Data"))
        {
            values.Add(element.Value);
            var name = element.Attribute("Name")?.Value;
            if (name == "ProcessId" && TryParseProcessId(element.Value, out var parsedPid) && parsedPid > 0) processId = parsedPid;
            if (name == "ProcessCreationTime" && TryParseHex(element.Value, out var parsedCreation)) processCreation = parsedCreation;
            if (name is not null)
            {
                if (!named.TryAdd(name, element.Value))
                {
                    duplicateNames = true;
                    named[name] = element.Value;
                }
            }
        }
        return new EventDataFields(named, values, duplicateNames, processId, processCreation);
    }

    private static void ReadProcessLifetime(EventDataFields data, string? provider, int id, out int? pid, out long? creation)
    {
        pid = null; creation = null;
        if (provider == "Application Error" && id == 1000)
        {
            pid = data.ProcessId;
            creation = data.ProcessCreation;
            if (pid is null || creation is null || creation <= 0) { pid = null; creation = null; }
        }
    }

    private static bool TryReadSystem(XElement system, out string? provider, out int id, out DateTimeOffset timestamp)
    {
        id = default; timestamp = default;
        provider = system.Elements().FirstOrDefault(element => element.Name.LocalName == "Provider")?.Attribute("Name")?.Value;
        var idText = system.Elements().FirstOrDefault(element => element.Name.LocalName == "EventID")?.Value;
        var timeText = system.Elements().FirstOrDefault(element => element.Name.LocalName == "TimeCreated")?.Attribute("SystemTime")?.Value;
        return !string.IsNullOrWhiteSpace(provider) && provider.Length <= 256 && int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id is >= 0 and <= 65535 &&
            DateTimeOffset.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp);
    }

    private static IncidentEventLevel ReadLevel(XElement system) =>
        int.TryParse(system.Elements().FirstOrDefault(element => element.Name.LocalName == "Level")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawLevel)
            ? rawLevel switch { 1 or 2 => IncidentEventLevel.Error, 3 => IncidentEventLevel.Warning, 4 => IncidentEventLevel.Information, _ => IncidentEventLevel.Unknown } : IncidentEventLevel.Unknown;

    private static string? RenderXml(SafeEvtHandle item)
    {
        var buffer = new byte[4096];
        while (true)
        {
            if (EvtRender(IntPtr.Zero, item, EvtRenderEventXml, buffer.Length, buffer, out var used, out _)) return Encoding.Unicode.GetString(buffer, 0, Math.Min(used, buffer.Length)).TrimEnd('\0');
            if (Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer || used > XmlBytes) return null;
            buffer = new byte[used];
        }
    }

    private static string FormatMessage(SafeEvtHandle item, string provider)
        => FormatMessage(item, EvtOpenPublisherMetadata(IntPtr.Zero, provider, null, 0, 0));

    internal static string FormatMessage(SafeHandle item, IEventMessageMetadata metadata)
    {
        using var lease = metadata;
        if (metadata.IsInvalid) return "";
        var buffer = new char[1024];
        while (true)
        {
            if (metadata.TryFormat(item, buffer, out var used, out var error)) return new string(buffer, 0, Math.Min((int)used, buffer.Length)).TrimEnd('\0');
            if (error != ErrorInsufficientBuffer || used > MessageCharacters) return "";
            buffer = new char[used];
        }
    }

    private static string FirstLine(string value)
    {
        var lineEnd = value.AsSpan().IndexOfAny('\r', '\n');
        var line = lineEnd >= 0 ? value[..lineEnd] : value;
        return line.Length <= 300 ? line : line[..300];
    }
    private static bool TryParseHex(string value, out long parsed)
    {
        value = value.Trim(); if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        return long.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsed);
    }

    private sealed record EventDataFields(IReadOnlyDictionary<string, string> Named, IReadOnlyList<string> Values,
        bool DuplicateNames, int? ProcessId, long? ProcessCreation)
    {
        public static EventDataFields Empty { get; } = new(new Dictionary<string, string>(), [], false, null, null);
    }

    private static bool TryParseUnsignedHex(string? value, ulong maximum, out ulong parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        return ulong.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsed) && parsed <= maximum;
    }
    private static bool TryReadName(string? value, out string name)
    {
        var raw = value ?? "";
        name = raw.Trim();
        return name.Length is > 0 and <= 260 && !raw.Any(char.IsControl) &&
            name.IndexOfAny(['\\', '/', ':']) < 0;
    }
    private static bool TryReadVersion(string? value, out string version)
    {
        var raw = value ?? "";
        version = raw.Trim();
        return version.Length is > 0 and <= 260 && !raw.Any(char.IsControl) &&
            Version.TryParse(version, out _);
    }
    private static bool TryParseProcessId(string value, out int parsed)
    {
        value = value.Trim();
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(value[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsed)
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }
    private sealed class SafeEvtHandle : SafeHandleZeroOrMinusOneIsInvalid, IEventMessageMetadata
    {
        public SafeEvtHandle() : base(true) { }
        public SafeEvtHandle(nint value) : base(true) => SetHandle(value);
        bool IEventMessageMetadata.TryFormat(SafeHandle item, char[] buffer, out uint used, out int error)
        {
            var success = EvtFormatMessage(this, (SafeEvtHandle)item, 0, 0, IntPtr.Zero, EvtFormatMessageEvent,
                (uint)buffer.Length, buffer, out used);
            error = success ? 0 : Marshal.GetLastPInvokeError();
            return success;
        }
        protected override bool ReleaseHandle() => EvtClose(handle);
    }
    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeEvtHandle EvtQuery(nint session, string? path, string query, uint flags);
    [DllImport("wevtapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EvtNext(SafeEvtHandle resultSet, uint eventsSize, [Out] nint[] events, uint timeout, uint flags, out uint returned);
    [DllImport("wevtapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EvtRender(nint context, SafeEvtHandle fragment, uint flags, int bufferSize, [Out] byte[] buffer, out int used, out int propertyCount);
    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeEvtHandle EvtOpenPublisherMetadata(nint session, string publisherId, string? logFilePath, uint locale, uint flags);
    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EvtFormatMessage(SafeEvtHandle publisherMetadata, SafeEvtHandle @event, uint messageId, uint valueCount, nint values, uint flags, uint bufferSize, [Out] char[] buffer, out uint bufferUsed);
    [DllImport("wevtapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EvtClose(nint handle);
}
