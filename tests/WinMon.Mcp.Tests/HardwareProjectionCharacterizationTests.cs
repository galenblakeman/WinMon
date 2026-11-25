using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace WinMon.Mcp.Tests;

[TestFixture]
public sealed class HardwareProjectionCharacterizationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Sections = ["cpu", "motherboard", "memory", "graphics", "disks", "volumes", "network"];
    private static readonly string[] TextPaths =
    [
        "cpu.data.names.0", "cpu.data.processors.0.name", "cpu.data.processors.0.manufacturer",
        "cpu.data.processors.0.socketDesignation", "cpu.data.processors.0.deviceId",
        "motherboard.data.items.0.manufacturer", "motherboard.data.items.0.product", "motherboard.data.items.0.version",
        "motherboard.data.items.0.serialNumber", "memory.data.modules.0.deviceLocator", "memory.data.modules.0.bankLabel",
        "memory.data.modules.0.manufacturer", "memory.data.modules.0.partNumber", "graphics.data.adapters.0.name",
        "disks.data.items.0.model", "disks.data.items.0.manufacturer", "disks.data.items.0.driveLetters.0",
        "volumes.data.items.0.name", "volumes.data.items.0.label", "volumes.data.items.0.fileSystem",
        "network.data.adapters.0.name", "network.data.adapters.0.description"
    ];
    private static readonly string[] PrivatePaths =
        ["motherboard.data.items.0.serialNumber", "volumes.data.items.0.label", "network.data.adapters.0.name"];
    // This order is the caller-visible tie order used when shrinking a response.
    private static readonly (string Path, string? Marker)[] Collections =
    [
        ("cpu.data.processors", "name"), ("motherboard.data.items", "product"),
        ("memory.data.modules", "deviceLocator"), ("graphics.data.adapters", "name"),
        ("disks.data.items", "model"), ("volumes.data.items", "name"),
        ("network.data.adapters", "description"), ("cpu.data.names", null),
        ("disks.data.items.0.driveLetters", null)
    ];

    private static HardwareInventorySnapshot Fixture() => new(7,
        new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 22, 0, 0, 1, TimeSpan.Zero), "ok", true, false,
        new("ok", false, [], new(["CPU"], 4, 8,
            [new("Processor", "Vendor", "Socket", "CPU0", 12, 4, 8, 3700, 1024, 2048, true, false)])),
        new("ok", false, [], new([new("Vendor", "Board", "Version", "serial-secret")])),
        new("ok", false, [], new(4096, 4000, 1, 2, [new("Slot", "Bank", 4096, 34, 3200, 3000, "Vendor", "Part")])),
        new("ok", false, [], new([new("Graphics", 1024, 2048)])),
        new("ok", false, [], new([new(3, "Disk", "Vendor", 8000, 4, 17, 0, ["C:"])])),
        new("ok", false, [], new([new("C:\\", "label-secret", "NTFS", 8000, 6000, true, false)])),
        new("ok", false, [], new([new("adapter-secret", "Network", 1, 2, 3, 4, false, 1000000)])));

    private static JsonNode Node(HardwareInventorySnapshot snapshot) => JsonSerializer.SerializeToNode(snapshot, Json)!;
    private static HardwareInventorySnapshot Model(JsonNode node) => node.Deserialize<HardwareInventorySnapshot>(Json)!;
    private static JsonNode? At(JsonNode node, string path)
    {
        JsonNode? current = node;
        foreach (var part in path.Split('.'))
            current = current is JsonArray array ? array[int.Parse(part, System.Globalization.CultureInfo.InvariantCulture)] : current![part];
        return current;
    }
    private static void Put(JsonNode node, string path, JsonNode? value)
    {
        var separator = path.LastIndexOf('.');
        var parent = separator < 0 ? node : At(node, path[..separator])!;
        var leaf = path[(separator + 1)..];
        if (parent is JsonArray array) array[int.Parse(leaf, System.Globalization.CultureInfo.InvariantCulture)] = value;
        else parent[leaf] = value;
    }
    private static void SetCollection(JsonNode node, int index, int count)
    {
        var (path, marker) = Collections[index];
        var prototype = At(Node(Fixture()), path)!.AsArray()[0]!;
        var rows = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            var row = prototype.DeepClone();
            if (marker is null) row = JsonValue.Create("row-" + i)!;
            else row[marker] = "row-" + i;
            rows.Add(row);
        }
        Put(node, path, rows);
    }
    private static HardwareInventorySnapshot Counts(params int[] counts)
    {
        var node = Node(Fixture());
        for (var i = 0; i < 8; i++) SetCollection(node, i, counts[i]);
        return Model(node);
    }

    private static IEnumerable<TestCaseData> TextCases()
    {
        (string? Input, string? Expected, bool Clipped)[] values =
        [
            (null, null, false), ("", "", false), ("ordinary", "ordinary", false),
            (new('a', 511), new('a', 511), false), (new('a', 512), new('a', 512), false),
            (new('a', 513), new('a', 512), true), ("line\tbreak\n\0", "linebreak", true),
            (new('界', 513), new('界', 512), true),
            (new string('a', 511) + "😀", new('a', 511), true),
            (new string('a', 510) + "😀b", new string('a', 510) + "😀", true)
        ];
        for (var field = 0; field < TextPaths.Length; field++)
        for (var value = 0; value < values.Length; value++)
            yield return new TestCaseData(TextPaths[field], values[value].Input, values[value].Expected, values[value].Clipped)
                .SetName($"ProjectionText_{field}_{value}");
    }

    [TestCaseSource(nameof(TextCases))]
    public void EveryProjectedTextFieldRetainsItsBoundaryAndSanitizationContract(string path, string? input, string? expected, bool clipped)
    {
        var node = Node(Fixture());
        Put(node, path, JsonValue.Create(input));
        var source = Model(node);
        var result = HardwareProjection.Safe(source, out var changed);
        if (input is null && path is "cpu.data.names.0" or "volumes.data.items.0.name") expected = "";
        var output = Node(result);
        var missingDriveLetter = path == "disks.data.items.0.driveLetters.0" && input is null;
        Assert.That(missingDriveLetter ? null : At(output, path)?.GetValue<string>(), Is.EqualTo(expected));
        Assert.That(changed, Is.EqualTo(clipped));
        Assert.That(result.Status, Is.EqualTo(clipped ? "partial" : "ok"));
        Assert.That(result.Truncated, Is.EqualTo(clipped));
        // Missing drive-letter values are removed rather than retained as null entries.
        if (missingDriveLetter)
            Assert.That(At(output, "disks.data.items.0.driveLetters")!.AsArray(), Is.Empty);
    }

    private static IEnumerable<TestCaseData> PrivacyCases()
    {
        for (var i = 0; i < PrivatePaths.Length; i++)
        foreach (var include in new[] { false, true })
            yield return new TestCaseData(PrivatePaths[i], include).SetName($"ProjectionPrivacy_{i}_{include}");
    }
    [TestCaseSource(nameof(PrivacyCases))]
    public void HiddenIdentifierTextDoesNotTriggerClipping(string path, bool include)
    {
        var node = Node(Fixture() with { IdentifiersIncluded = include });
        Put(node, path, JsonValue.Create(new string('s', 513)));
        var result = HardwareProjection.Safe(Model(node), out var changed);
        Assert.That(At(Node(result), path)?.GetValue<string>(), Is.EqualTo(include ? new string('s', 512) : null));
        Assert.That(changed, Is.EqualTo(include));
        Assert.That(result.IdentifiersIncluded, Is.EqualTo(include));
    }

    [TestCase(false)] [TestCase(true)]
    public void ResponseUsesTheRequestedIdentifierPolicyWithoutMutatingTheInput(bool include)
    {
        var source = Fixture() with { IdentifiersIncluded = !include };
        var before = JsonSerializer.Serialize(source, Json);
        var result = HardwareResponse.From(source, include);
        var output = Node(result.Data!);
        foreach (var path in PrivatePaths)
            Assert.That(At(output, path) is not null, Is.EqualTo(include));
        Assert.That(result.Data!.Cpu.Data!.Processors[0].DeviceId, Is.EqualTo("CPU0"));
        Assert.That(result.Data.IdentifiersIncluded, Is.EqualTo(include));
        Assert.That(JsonSerializer.Serialize(source, Json), Is.EqualTo(before));
    }

    private static IEnumerable<TestCaseData> CapacityCases()
    {
        for (var i = 0; i < Collections.Length; i++)
        foreach (var count in new[] { 255, 256, 257 })
            yield return new TestCaseData(i, count).SetName($"ProjectionCapacity_{i}_{count}");
    }
    [TestCaseSource(nameof(CapacityCases))]
    public void EveryProjectedCollectionKeepsItsInclusiveCapacityAndOrder(int index, int count)
    {
        var node = Node(Fixture());
        SetCollection(node, index, count);
        var source = Model(node);
        var before = JsonSerializer.Serialize(source, Json);
        var result = HardwareProjection.Safe(source, out var changed);
        var path = Collections[index].Path;
        var expected = At(Node(source), path)!.AsArray().Take(256).Select(x => x!.ToJsonString()).ToArray();
        Assert.That(At(Node(result), path)!.AsArray().Select(x => x!.ToJsonString()), Is.EqualTo(expected));
        Assert.That(changed, Is.EqualTo(count > 256));
        Assert.That(At(Node(result), path.Split('.')[0] + ".truncated")!.GetValue<bool>(), Is.EqualTo(count > 256));
        Assert.That(JsonSerializer.Serialize(source, Json), Is.EqualTo(before));
    }

    private static IEnumerable<TestCaseData> MetadataCases()
    {
        string?[] statuses = ["ok", "partial", "unavailable", "unknown", "", null];
        for (var section = 0; section < Sections.Length; section++)
        for (var status = 0; status < statuses.Length; status++)
            yield return new TestCaseData(Sections[section], statuses[status]).SetName($"ProjectionMetadata_{section}_{status}");
    }
    [TestCaseSource(nameof(MetadataCases))]
    public void EverySectionNormalizesStatusAndWhitelistsIssuesWithoutClaimingTruncation(string section, string? status)
    {
        var node = Node(Fixture());
        Put(node, section + ".status", JsonValue.Create(status));
        Put(node, section + ".issues", new JsonArray("readFailed", "private\nissue", null, "providerUnavailable", "readFailed", "truncated"));
        var result = HardwareProjection.Safe(Model(node), out var changed);
        var output = Node(result)[section]!;
        Assert.That(output["status"]!.GetValue<string>(), Is.EqualTo(status is "ok" or "partial" or "unavailable" ? status : "unavailable"));
        Assert.That(output["issues"]!.AsArray().Select(x => x!.GetValue<string>()), Is.EqualTo(new[] { "readFailed", "providerUnavailable", "truncated" }));
        Assert.That(changed, Is.False);
        Assert.That(result.Truncated, Is.False);
        Assert.That(result.Status, Is.EqualTo("ok"));
    }

    [TestCaseSource(nameof(Sections))]
    public void IssueCapacityIsAppliedBeforeFilteringAndDeduplication(string section)
    {
        var node = Node(Fixture());
        Put(node, section + ".issues", new JsonArray(Enumerable.Repeat("private", 16).Append("readFailed").Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()));
        var result = HardwareProjection.Safe(Model(node), out var changed);
        Assert.That(Node(result)[section]!["issues"]!.AsArray(), Is.Empty);
        Assert.That(changed, Is.False);
    }

    [TestCaseSource(nameof(Sections))]
    public void MissingSectionDataIsRetainedWithoutManufacturingTruncation(string section)
    {
        var node = Node(Fixture());
        Put(node, section + ".data", null);
        var result = HardwareProjection.Safe(Model(node), out var changed);
        Assert.That(Node(result)[section]!["data"], Is.Null);
        Assert.That(changed, Is.False);
    }

    [TestCase(false, false)] [TestCase(false, true)] [TestCase(true, false)] [TestCase(true, true)]
    public void ChangedReflectsSectionFlagsWhilePreservingAggregateTruncation(bool aggregate, bool section)
    {
        var source = Fixture() with { Truncated = aggregate };
        source = source with { Cpu = source.Cpu with { Truncated = section } };
        var result = HardwareProjection.Safe(source, out var changed);
        Assert.That(changed, Is.EqualTo(section));
        Assert.That(result.Truncated, Is.EqualTo(aggregate || section));
        Assert.That(result.Status, Is.EqualTo(section ? "partial" : "ok"));
        Assert.That(result.Cpu.Status, Is.EqualTo("ok"));
        Assert.That(result.Cpu.Issues, Is.Empty);
    }

    [Test]
    public void OrdinaryProjectionPreservesTheEntirePayloadAndInput()
    {
        var source = Fixture();
        var before = JsonSerializer.Serialize(source, Json);
        var result = HardwareProjection.Safe(source, out var changed);
        Assert.That(JsonSerializer.Serialize(result, Json), Is.EqualTo(before));
        Assert.That(JsonSerializer.Serialize(source, Json), Is.EqualTo(before));
        Assert.That(changed, Is.False);
    }

    [Test]
    public void EmptySectionsRemainEmptyAndComplete()
    {
        var source = Counts(new int[8]);
        var result = HardwareProjection.Safe(source, out var changed);
        Assert.That(JsonSerializer.Serialize(result, Json), Is.EqualTo(JsonSerializer.Serialize(source, Json)));
        Assert.That(changed, Is.False);
    }

    private static IEnumerable<TestCaseData> NullCollections()
    {
        for (var i = 0; i < 8; i++)
            yield return new TestCaseData(Collections[i].Path).SetName($"ProjectionNullCollection_{i}");
    }
    [TestCaseSource(nameof(NullCollections))]
    public void NullRequiredCollectionsRetainTheirOriginalArgumentException(string path)
    {
        var node = Node(Fixture());
        Put(node, path, null);
        var exception = Assert.Throws<ArgumentNullException>(() => HardwareProjection.Safe(Model(node), out _));
        Assert.That(exception!.ParamName, Is.EqualTo("source"));
    }

    private static IEnumerable<TestCaseData> NullRows()
    {
        for (var i = 0; i < 7; i++)
            yield return new TestCaseData(Collections[i].Path + ".0").SetName($"ProjectionNullRow_{i}");
    }
    [TestCaseSource(nameof(NullRows))]
    public void NullRequiredRowsRetainTheirOriginalNullReferenceException(string path)
    {
        var node = Node(Fixture());
        Put(node, path, null);
        Assert.Throws<NullReferenceException>(() => HardwareProjection.Safe(Model(node), out _));
    }

    [TestCaseSource(nameof(Sections))]
    public void NullRequiredSectionsRetainTheirOriginalNullReferenceException(string section)
    {
        var node = Node(Fixture());
        Put(node, section, null);
        Assert.Throws<NullReferenceException>(() => HardwareProjection.Safe(Model(node), out _));
    }

    [Test]
    public void MissingOptionalDriveLettersRemainMissing()
    {
        var node = Node(Fixture());
        Put(node, "disks.data.items.0.driveLetters", null);
        var result = HardwareProjection.Safe(Model(node), out var changed);
        Assert.That(result.Disks.Data!.Items[0].DriveLetters, Is.Null);
        Assert.That(changed, Is.False);
    }

    [TestCase("ok", "partial")] [TestCase("partial", "partial")]
    [TestCase("unavailable", "unavailable")] [TestCase("custom", "custom")]
    [TestCase(null, null)]
    public void ClippingOnlyChangesAnOrdinaryAggregateStatus(string? status, string? expected)
    {
        var node = Node(Fixture() with { Status = status! });
        Put(node, "cpu.data.names.0", JsonValue.Create(new string('a', 513)));
        var result = HardwareProjection.Safe(Model(node), out var changed);
        Assert.That(changed, Is.True);
        Assert.That(result.Status, Is.EqualTo(expected));
        Assert.That(result.Truncated, Is.True);
    }

    private static IEnumerable<TestCaseData> TrimCases()
    {
        for (var i = 0; i < 8; i++)
        foreach (var count in new[] { 1, 2, 3, 4 })
            yield return new TestCaseData(i, count).SetName($"TrimCapacity_{i}_{count}");
    }
    [TestCaseSource(nameof(TrimCases))]
    public void TrimHalvesTheLargestCollectionUsingItsOrderedPrefix(int index, int count)
    {
        var counts = new int[8]; counts[index] = count;
        var source = Counts(counts);
        var before = JsonSerializer.Serialize(source, Json);
        var result = HardwareProjection.Trim(source)!;
        var output = Node(result);
        for (var i = 0; i < 8; i++)
        {
            var path = Collections[i].Path;
            var expected = At(Node(source), path)!.AsArray().Take(i == index ? count / 2 : counts[i]).Select(x => x!.ToJsonString()).ToArray();
            Assert.That(At(output, path)!.AsArray().Select(x => x!.ToJsonString()), Is.EqualTo(expected));
        }
        var section = Collections[index].Path.Split('.')[0];
        Assert.That(output[section]!["truncated"]!.GetValue<bool>(), Is.True);
        Assert.That(output[section]!["status"]!.GetValue<string>(), Is.EqualTo("partial"));
        Assert.That(output[section]!["issues"]!.AsArray().Select(x => x!.GetValue<string>()), Is.EqualTo(new[] { "truncated" }));
        Assert.That(result.Truncated, Is.EqualTo(source.Truncated));
        Assert.That(result.Status, Is.EqualTo(source.Status));
        Assert.That(result.SchemaVersion, Is.EqualTo(source.SchemaVersion));
        Assert.That(result.CollectionStartedUtc, Is.EqualTo(source.CollectionStartedUtc));
        Assert.That(result.CollectionCompletedUtc, Is.EqualTo(source.CollectionCompletedUtc));
        Assert.That(JsonSerializer.Serialize(source, Json), Is.EqualTo(before));
    }

    private static IEnumerable<TestCaseData> Ties()
    {
        for (var first = 0; first < 7; first++)
        for (var second = first + 1; second < 8; second++)
            yield return new TestCaseData(first, second).SetName($"TrimTie_{first}_{second}");
    }
    [TestCaseSource(nameof(Ties))]
    public void TrimRetainsTheFirstMaximumTieOrder(int first, int second)
    {
        var counts = new int[8]; counts[first] = counts[second] = 3;
        var result = HardwareProjection.Trim(Counts(counts))!;
        Assert.That(At(Node(result), Collections[first].Path)!.AsArray().Count, Is.EqualTo(1));
        Assert.That(At(Node(result), Collections[second].Path)!.AsArray().Count, Is.EqualTo(3));
    }

    [Test]
    public void EmptyOrMissingDataHasNothingToTrim()
    {
        Assert.That(HardwareProjection.Trim(Counts(new int[8])), Is.Null);
        var node = Node(Fixture());
        foreach (var section in Sections) Put(node, section + ".data", null);
        Assert.That(HardwareProjection.Trim(Model(node)), Is.Null);
    }

    [TestCase("ok", "partial")] [TestCase("partial", "partial")]
    [TestCase("unavailable", "unavailable")] [TestCase("custom", "custom")]
    public void TrimPreservesItsExistingSelectedSectionMetadataRules(string status, string expected)
    {
        var source = Counts(3, 0, 0, 0, 0, 0, 0, 0);
        source = source with { Status = "outer", Truncated = true,
            Cpu = source.Cpu with { Status = status, Issues = ["readFailed", "truncated", "readFailed", "custom"] } };
        var result = HardwareProjection.Trim(source)!;
        Assert.That(result.Cpu.Status, Is.EqualTo(expected));
        Assert.That(result.Cpu.Issues, Is.EqualTo(new[] { "readFailed", "truncated", "custom" }));
        Assert.That(result.Status, Is.EqualTo("outer"));
        Assert.That(result.Truncated, Is.True);
        Assert.That(result.Motherboard, Is.SameAs(source.Motherboard));
        Assert.That(result.Network, Is.SameAs(source.Network));
    }

    [Test]
    public void RepeatedTrimmingStrictlyDecreasesThePayloadUntilNothingRemains()
    {
        var source = Counts(5, 5, 5, 5, 5, 5, 5, 5);
        var remaining = 40;
        while (HardwareProjection.Trim(source) is { } next)
        {
            var node = Node(next);
            var count = Collections.Take(8).Sum(x => At(node, x.Path)!.AsArray().Count);
            Assert.That(count, Is.LessThan(remaining));
            remaining = count; source = next;
        }
        Assert.That(remaining, Is.Zero);
    }

    [Test]
    public void NullSnapshotsRetainTheirOriginalExceptions()
    {
        Assert.Throws<NullReferenceException>(() => HardwareProjection.Safe(null!, out _));
        Assert.Throws<NullReferenceException>(() => HardwareProjection.Trim(null!));
    }

    [Test]
    public void AllSectionProjectionPrecedesOriginalCollectionClipping()
    {
        var failure = new InvalidOperationException("Original collection clipping reached.");
        var source = Fixture() with { Disks = new("ok", false, [], new(new CountFailureDisks(failure))), Network = null! };
        Assert.Throws<NullReferenceException>(() => HardwareProjection.Safe(source, out _));
        source = source with { Network = Fixture().Network };
        Assert.That(Assert.Throws<InvalidOperationException>(() => HardwareProjection.Safe(source, out _)), Is.SameAs(failure));
    }

    private sealed class CountFailureDisks(Exception failure) : IReadOnlyList<HardwareDisk>
    {
        public int Count => throw failure;
        public HardwareDisk this[int index] => Fixture().Disks.Data!.Items[index];
        public IEnumerator<HardwareDisk> GetEnumerator() => Fixture().Disks.Data!.Items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
