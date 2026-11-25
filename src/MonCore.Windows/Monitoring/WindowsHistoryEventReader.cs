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

    private static IEnumerable<IncidentEvent> ReadLog(string log, DateTimeOffset startUtc, DateTimeOffset endUtc, Stopwatch started,
        TimeSpan budget, CancellationToken cancellationToken)
    {
        if (started.Elapsed >= budget) throw new TimeoutException();
        using var query = EvtQuery(IntPtr.Zero, null, BuildQuery(log, startUtc, endUtc), EvtQueryChannelPath | EvtQueryForwardDirection);
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
            if (!TryReadEvent(item, out var parsed)) throw new InvalidDataException("Unreadable Windows event.");
            cancellationToken.ThrowIfCancellationRequested();
            if (started.Elapsed >= budget) throw new TimeoutException();
            yield return parsed;
        }
    }

    private static bool TryReadEvent(SafeEvtHandle item, out IncidentEvent result)
    {
        result = default!;
        var xml = RenderXml(item);
        if (xml is null) return false;
        if (!TryParseXml(xml, "", out var parsed)) return false;
        result = parsed with { Message = FirstLine(FormatMessage(item, parsed.Provider)) };
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
        ReadProcessLifetime(document, provider, id, out var pid, out var creation);
        result = new IncidentEvent(timestamp, provider!, id, level, FirstLine(message), pid, creation);
        return true;
    }

    private static void ReadProcessLifetime(XDocument document, string? provider, int id, out int? pid, out long? creation)
    {
        pid = null; creation = null;
        if (provider == "Application Error" && id == 1000)
        {
            foreach (var data in document.Descendants().Where(element => element.Name.LocalName == "Data"))
            {
                var name = data.Attribute("Name")?.Value;
                if (name == "ProcessId" && TryParseProcessId(data.Value, out var parsedPid) && parsedPid > 0) pid = parsedPid;
                if (name == "ProcessCreationTime" && TryParseHex(data.Value, out var parsedCreation)) creation = parsedCreation;
            }
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
