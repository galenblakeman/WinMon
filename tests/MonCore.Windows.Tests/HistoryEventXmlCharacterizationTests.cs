using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace MonCore.Windows.Tests;

[TestFixture]
public sealed class HistoryEventXmlCharacterizationTests
{
    private const string Time = "2026-11-20T01:02:03.1234567+02:00";
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.Parse(Time, CultureInfo.InvariantCulture).ToUniversalTime();
    private static XElement SystemElement(string provider = "Application Error", string id = "1000", string time = Time) =>
        new("System", new XElement("Provider", new XAttribute("Name", provider)), new XElement("EventID", id),
            new XElement("TimeCreated", new XAttribute("SystemTime", time)), new XElement("Level", "2"));
    private static string Xml(XElement system, params object[] data) => new XElement("Event", system, data).ToString();
    private static XElement Data(string name, string value) => new("Data", new XAttribute("Name", name), value);
    private static IncidentEvent Parse(string xml, string message = "message")
    {
        Assert.That(WindowsHistoryEventReader.TryParseXml(xml, message, out var item), Is.True);
        Assert.That(item, Is.Not.Null);
        return item;
    }

    private static IEnumerable<TestCaseData> Headers()
    {
        yield return new TestCaseData("<Event/>").SetName("HeaderRejectsMissingSystem");
        yield return new TestCaseData(Xml(new XElement("System"))).SetName("HeaderRejectsEmptySystem");
        foreach (var field in new[] { "Provider", "EventID", "TimeCreated" })
        {
            var system = SystemElement(); system.Element(field)!.Remove();
            yield return new TestCaseData(Xml(system)).SetName("HeaderRejectsMissing" + field);
        }
        foreach (var (name, value) in new[] { ("Empty", ""), ("Whitespace", " \t"), ("TooLong", new string('x', 257)) })
            yield return new TestCaseData(Xml(SystemElement(value))).SetName("HeaderRejectsProvider" + name);
        foreach (var (name, value) in new[] { ("Negative", "-1"), ("TooLarge", "65536"), ("Overflow", "2147483648"), ("Hex", "0x3e8"), ("Invalid", "x"), ("Empty", "") })
            yield return new TestCaseData(Xml(SystemElement(id: value))).SetName("HeaderRejectsEventId" + name);
        foreach (var (name, value) in new[] { ("Empty", ""), ("Invalid", "never"), ("ImpossibleDate", "2026-02-30T00:00:00Z") })
            yield return new TestCaseData(Xml(SystemElement(time: value))).SetName("HeaderRejectsTime" + name);
        var missingName = SystemElement(); missingName.Element("Provider")!.RemoveAttributes();
        yield return new TestCaseData(Xml(missingName)).SetName("HeaderRejectsMissingProviderName");
        var missingTime = SystemElement(); missingTime.Element("TimeCreated")!.RemoveAttributes();
        yield return new TestCaseData(Xml(missingTime)).SetName("HeaderRejectsMissingTimeAttribute");
    }

    [TestCaseSource(nameof(Headers))]
    public void InvalidHeadersReturnFalseAndClearTheOutValue(string xml)
    {
        Assert.That(WindowsHistoryEventReader.TryParseXml(xml, null!, out var result), Is.False);
        Assert.That(result, Is.Null);
    }

    [TestCase("0", 0)]
    [TestCase("65535", 65535)]
    [TestCase(" +1000 ", 1000)]
    public void EventIdBoundariesAndIntegerWhitespaceRetainTheirValues(string text, int expected)
    {
        var item = Parse(Xml(SystemElement(" Provider ", text)));
        Assert.That(item, Is.EqualTo(new IncidentEvent(Timestamp, " Provider ", expected, IncidentEventLevel.Error, "message")));
    }

    [Test]
    public void MaximumProviderLengthIsAcceptedWithoutTrimming() =>
        Assert.That(Parse(Xml(SystemElement(new string('p', 256)))).Provider, Has.Length.EqualTo(256));

    [TestCase(null, IncidentEventLevel.Unknown)]
    [TestCase("", IncidentEventLevel.Unknown)]
    [TestCase("bad", IncidentEventLevel.Unknown)]
    [TestCase("2147483648", IncidentEventLevel.Unknown)]
    [TestCase("-1", IncidentEventLevel.Unknown)]
    [TestCase("0", IncidentEventLevel.Unknown)]
    [TestCase("1", IncidentEventLevel.Error)]
    [TestCase("2", IncidentEventLevel.Error)]
    [TestCase("3", IncidentEventLevel.Warning)]
    [TestCase("4", IncidentEventLevel.Information)]
    [TestCase("5", IncidentEventLevel.Unknown)]
    [TestCase(" +3 ", IncidentEventLevel.Warning)]
    public void SeverityRetainsItsExactMapping(string? text, IncidentEventLevel expected)
    {
        var system = SystemElement(); system.Element("Level")!.Remove();
        if (text is not null) system.Add(new XElement("Level", text));
        Assert.That(Parse(Xml(system)).Level, Is.EqualTo(expected));
    }

    [Test]
    public void LocalNamesIgnoreElementNamespacesAndRootNameButAttributesRemainUnqualified()
    {
        XNamespace ns = "urn:owned-event";
        var root = new XElement("DifferentRoot", SystemElement());
        foreach (var element in root.DescendantsAndSelf()) element.Name = ns + element.Name.LocalName;
        Assert.That(Parse(root.ToString()).Timestamp, Is.EqualTo(Timestamp));
        root.Element(ns + "System")!.Element(ns + "Provider")!.Attribute("Name")!.Remove();
        root.Element(ns + "System")!.Element(ns + "Provider")!.Add(new XAttribute(ns + "Name", "Application Error"));
        Assert.That(WindowsHistoryEventReader.TryParseXml(root.ToString(), "", out var result), Is.False);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void OnlyFirstDirectSystemAndFirstHeaderFieldsAreUsed()
    {
        var system = SystemElement();
        system.Add(new XElement("Provider", new XAttribute("Name", "Other")), new XElement("EventID", "99"),
            new XElement("TimeCreated", new XAttribute("SystemTime", "bad")), new XElement("Level", "3"));
        var xml = new XElement("Event", new XElement("Wrapper", SystemElement("Nested")), system, SystemElement("Later")).ToString();
        Assert.That(Parse(xml), Is.EqualTo(new IncidentEvent(Timestamp, "Application Error", 1000, IncidentEventLevel.Error, "message")));
        system.AddFirst(new XElement("Provider"));
        Assert.That(WindowsHistoryEventReader.TryParseXml(Xml(system), "", out var result), Is.False);
        Assert.That(result, Is.Null);
    }

    [TestCase("2026-11-19T23:02:03.1234567Z")]
    [TestCase("2026-11-19T23:02:03.1234567")]
    public void TimestampOffsetsAndMissingZoneNormalizeToUtc(string text) =>
        Assert.That(Parse(Xml(SystemElement(time: text))).Timestamp, Is.EqualTo(Timestamp).And.Property("Offset").EqualTo(TimeSpan.Zero));

    [TestCase("42", 42)]
    [TestCase(" +42 ", 42)]
    [TestCase(" 0X2a ", 42)]
    [TestCase("2147483647", int.MaxValue)]
    [TestCase("0x7fffffff", int.MaxValue)]
    public void ValidProcessIdsRequireAndRetainPositiveHexCreation(string text, int expected)
    {
        var item = Parse(Xml(SystemElement(), Data("ProcessId", text), Data("ProcessCreationTime", " 0X1a ")));
        Assert.Multiple(() => { Assert.That(item.ProcessId, Is.EqualTo(expected)); Assert.That(item.CreationTicks, Is.EqualTo(26)); });
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("2147483648")]
    [TestCase("0xffffffff")]
    [TestCase("0x")]
    [TestCase("bad")]
    [TestCase("")]
    public void InvalidProcessIdsClearBothLifetimeFields(string text)
    {
        var item = Parse(Xml(SystemElement(), Data("ProcessId", text), Data("ProcessCreationTime", "1")));
        AssertNoLifetime(item);
    }

    [TestCase("0")]
    [TestCase("ffffffffffffffff")]
    [TestCase("8000000000000000")]
    [TestCase("10000000000000000")]
    [TestCase("nothex")]
    [TestCase("0x")]
    [TestCase("")]
    public void InvalidCreationClearsBothLifetimeFields(string text) =>
        AssertNoLifetime(Parse(Xml(SystemElement(), Data("ProcessId", "42"), Data("ProcessCreationTime", text))));

    [TestCase("Application Error", "1001")]
    [TestCase("application error", "1000")]
    [TestCase("Application Error ", "1000")]
    [TestCase("Other", "1000")]
    public void OnlyExactApplicationError1000GetsProcessAttribution(string provider, string id) =>
        AssertNoLifetime(Parse(Xml(SystemElement(provider, id), Data("ProcessId", "42"), Data("ProcessCreationTime", "1"))));

    [TestCase("ProcessId")]
    [TestCase("ProcessCreationTime")]
    [TestCase("neither")]
    public void APartialLifetimeNeverLeaksOneField(string field)
    {
        var data = field == "neither" ? Array.Empty<object>() : new object[] { Data(field, "42") };
        AssertNoLifetime(Parse(Xml(SystemElement(), data)));
    }

    [Test]
    public void AllDescendantDataUseLastValidValuesAndInvalidPidDoesNotEraseEarlierValue()
    {
        var item = Parse(Xml(SystemElement(), new XElement("Wrapper", Data("ProcessId", "1"), Data("ProcessCreationTime", "a")),
            Data("ProcessId", "0X2A"), Data("ProcessId", "bad"), Data("ProcessId", "0"),
            Data("ProcessCreationTime", "7fffffffffffffff"), Data("ProcessCreationTime", "bad!"),
            Data("processid", "99"), new XElement("Data", "ignored")));
        Assert.Multiple(() => { Assert.That(item.ProcessId, Is.EqualTo(42)); Assert.That(item.CreationTicks, Is.EqualTo(long.MaxValue)); });
        AssertNoLifetime(Parse(Xml(SystemElement(), Data("ProcessId", "42"), Data("ProcessCreationTime", "1"), Data("ProcessCreationTime", "0"))));
    }

    [TestCase("empty")]
    [TestCase("carriage")]
    [TestCase("newline")]
    [TestCase("crlf")]
    [TestCase("limit")]
    [TestCase("over-limit")]
    public void MessageKeepsOnlyTheBoundedFirstLine(string kind)
    {
        var (input, expected) = kind switch
        {
            "empty" => ("", ""), "carriage" => ("a\rb", "a"), "newline" => ("\nb", ""), "crlf" => ("first\r\nsecond", "first"),
            "limit" => (new string('x', 300), new string('x', 300)), _ => (new string('x', 301), new string('x', 300))
        };
        Assert.That(Parse(Xml(SystemElement()), input).Message, Is.EqualTo(expected));
    }

    [TestCase("empty")]
    [TestCase("malformed")]
    [TestCase("dtd")]
    [TestCase("over-limit")]
    public void XmlSecurityAndParseFailuresKeepTheirExceptionTypeAndClearOutput(string kind)
    {
        var xml = kind switch
        {
            "empty" => "", "malformed" => "<Event>",
            "dtd" => "<!DOCTYPE Event [<!ENTITY secret SYSTEM 'file:///never-read-owned-test'>]><Event>&secret;</Event>",
            _ => Xml(SystemElement()) + new string(' ', 65536)
        };
        IncidentEvent result = new(DateTimeOffset.UnixEpoch, "sentinel", 1, IncidentEventLevel.Unknown, "");
        Assert.Throws<XmlException>(() => WindowsHistoryEventReader.TryParseXml(xml, "", out result));
        Assert.That(result, Is.Null);
    }

    [Test]
    public void NullInputsRetainTheirExceptionPrecedence()
    {
        IncidentEvent result = null!;
        Assert.Throws<ArgumentNullException>(() => WindowsHistoryEventReader.TryParseXml(null!, "", out result));
        Assert.That(result, Is.Null);
        Assert.Throws<NullReferenceException>(() => WindowsHistoryEventReader.TryParseXml(Xml(SystemElement()), null!, out result));
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExactXmlCharacterLimitIsAccepted()
    {
        var xml = Xml(SystemElement());
        xml += new string(' ', 65536 - xml.Length);
        Assert.That(Parse(xml).Provider, Is.EqualTo("Application Error"));
    }

    [TestCase("fr-FR")]
    [TestCase("tr-TR")]
    public void HeaderAndLifetimeNumbersUseInvariantCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var item = Parse(Xml(SystemElement(id: " +1000 "), Data("ProcessId", "0x2A"), Data("ProcessCreationTime", "10")));
            Assert.That(item, Is.EqualTo(new IncidentEvent(Timestamp, "Application Error", 1000, IncidentEventLevel.Error, "message", 42, 16)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static void AssertNoLifetime(IncidentEvent item)
    {
        Assert.Multiple(() => { Assert.That(item.ProcessId, Is.Null); Assert.That(item.CreationTicks, Is.Null); });
    }
}
