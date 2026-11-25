using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace MonCore.Windows.Tests;

[TestFixture]
public sealed class HardwareDiskCharacterizationTests
{
    private static readonly string[] Providers = ["Win32_DiskDrive", "MSFT_Disk", "MSFT_PhysicalDisk", "MSFT_Partition"];
    private static readonly string[][] Properties =
    [
        ["Index", "Model", "Manufacturer", "Size"],
        ["Number", "UniqueId", "UniqueIdFormat", "Model", "Manufacturer", "Size", "BusType", "HealthStatus"],
        ["UniqueId", "UniqueIdFormat", "MediaType"], ["DiskNumber", "DriveLetter"]
    ];
    private static HardwareRow Row(params (string Key, object? Value)[] fields)
    {
        var row = new HardwareRow(); foreach (var (key, value) in fields) row[key] = value; return row;
    }
    private static Query Complete() => new()
    {
        [Providers[0]] = [Row(("Index", 1), ("Model", " primary "), ("Manufacturer", " maker "), ("Size", 1000UL))],
        [Providers[1]] = [Row(("Number", 1), ("Model", "storage"), ("Manufacturer", "storage maker"), ("Size", 2000UL),
            ("UniqueId", "id"), ("UniqueIdFormat", 1), ("BusType", 17), ("HealthStatus", 2))],
        [Providers[2]] = [Row(("UniqueId", "ID"), ("UniqueIdFormat", 1), ("MediaType", 4))],
        [Providers[3]] = [Row(("DiskNumber", 1), ("DriveLetter", "c"))]
    };
    private static HardwareInventorySnapshot Read(Query query, CancellationToken token = default, Probe? probe = null) =>
        WindowsHardwareInventory.Read(query, probe ?? new Probe(), cancellationToken: token);

    [Test]
    public void AllFourQueriesRetainTheirExactOrderScopesAndPropertyProjection()
    {
        var query = Complete(); var disk = Read(query).Disks.Data!.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(query.Calls.Select(c => c.Provider), Is.EqualTo(Providers));
            Assert.That(query.Calls.Select(c => c.Scope), Is.EqualTo(new[] { @"root\cimv2", @"root\Microsoft\Windows\Storage", @"root\Microsoft\Windows\Storage", @"root\Microsoft\Windows\Storage" }));
            for (var i = 0; i < 4; i++) Assert.That(query.Calls[i].Properties, Is.EqualTo(Properties[i]));
            Assert.That(disk.Number, Is.EqualTo(1)); Assert.That(disk.Model, Is.EqualTo("primary"));
            Assert.That(disk.Manufacturer, Is.EqualTo("maker")); Assert.That(disk.CapacityBytes, Is.EqualTo(1000));
            Assert.That(disk.MediaType, Is.EqualTo(4)); Assert.That(disk.BusType, Is.EqualTo(17));
            Assert.That(disk.HealthStatus, Is.EqualTo(2)); Assert.That(disk.DriveLetters, Is.EqualTo(new[] { "C:" }));
        });
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
    [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
    [TestCase(8)] [TestCase(9)] [TestCase(10)] [TestCase(11)]
    [TestCase(12)] [TestCase(13)] [TestCase(14)] [TestCase(15)]
    public void ProviderAvailabilityCombinationsPreserveDataAndIssueSemantics(int mask)
    {
        var query = Complete(); for (var i = 0; i < 4; i++) if ((mask & (1 << i)) == 0) query[Providers[i]] = null;
        var section = Read(query).Disks;
        Assert.That(query.Calls, Has.Count.EqualTo(4));
        if ((mask & 3) == 0)
        {
            Assert.Multiple(() => { Assert.That(section.Status, Is.EqualTo("unavailable")); Assert.That(section.Data, Is.Null); Assert.That(section.Truncated, Is.False); Assert.That(section.Issues, Is.EqualTo(new[] { "providerUnavailable" })); });
            return;
        }
        var disk = section.Data!.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(section.Status, Is.EqualTo(mask == 15 ? "ok" : "partial"));
            Assert.That(section.Issues, Is.EqualTo(mask == 15 ? Array.Empty<string>() : ["providerUnavailable"]));
            Assert.That(section.Truncated, Is.False); Assert.That(disk.Number, Is.EqualTo(1));
            Assert.That(disk.Model, Is.EqualTo((mask & 1) != 0 ? "primary" : "storage"));
            Assert.That(disk.CapacityBytes, Is.EqualTo((mask & 1) != 0 ? 1000 : 2000));
            Assert.That(disk.BusType, Is.EqualTo((mask & 2) != 0 ? 17 : (int?)null));
            Assert.That(disk.HealthStatus, Is.EqualTo((mask & 2) != 0 ? 2 : (int?)null));
            Assert.That(disk.MediaType, Is.EqualTo((mask & 6) == 6 ? 4 : (int?)null));
            Assert.That(disk.DriveLetters, Is.EqualTo((mask & 8) != 0 ? new[] { "C:" } : null));
        });
    }

    [TestCase("empty")]
    [TestCase("null")]
    public void EmptyOrMissingPrimaryListFallsBackToStorageNumberAndFields(string primary)
    {
        var query = Complete(); query[Providers[0]] = primary == "empty" ? [] : null;
        var section = Read(query).Disks; var disk = section.Data!.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(section.Status, Is.EqualTo(primary == "empty" ? "ok" : "partial"));
            Assert.That(disk.Number, Is.EqualTo(1)); Assert.That(disk.Model, Is.EqualTo("storage"));
            Assert.That(disk.Manufacturer, Is.EqualTo("storage maker")); Assert.That(disk.CapacityBytes, Is.EqualTo(2000));
            Assert.That(disk.MediaType, Is.EqualTo(4)); Assert.That(disk.DriveLetters, Is.EqualTo(new[] { "C:" }));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public void EmptyAvailableSourcesRemainDistinctFromUnavailableSources(bool supplementalMissing)
    {
        var query = Complete(); query[Providers[0]] = []; query[Providers[1]] = [];
        if (supplementalMissing) query[Providers[2]] = null;
        var section = Read(query).Disks;
        Assert.Multiple(() =>
        {
            Assert.That(section.Data!.Items, Is.Empty); Assert.That(section.Truncated, Is.False);
            Assert.That(section.Status, Is.EqualTo(supplementalMissing ? "partial" : "ok"));
            Assert.That(section.Issues, Is.EqualTo(supplementalMissing ? new[] { "providerUnavailable" } : []));
        });
    }

    [TestCase("Win32_DiskDrive", 256)] [TestCase("Win32_DiskDrive", 257)]
    [TestCase("MSFT_Disk", 256)] [TestCase("MSFT_Disk", 257)]
    [TestCase("MSFT_PhysicalDisk", 256)] [TestCase("MSFT_PhysicalDisk", 257)]
    [TestCase("MSFT_Partition", 256)] [TestCase("MSFT_Partition", 257)]
    public void EveryProviderRetainsTheExact256RowTruncationBoundary(string provider, int count)
    {
        var query = Complete(); var first = query[provider]![0];
        query[provider] = new[] { first }.Concat(Enumerable.Range(1, count - 1).Select(i =>
            Row(("Index", i + 10), ("Number", i + 10), ("UniqueId", "other" + i), ("DiskNumber", i + 10)))).ToArray();
        var section = Read(query).Disks;
        Assert.Multiple(() =>
        {
            Assert.That(section.Truncated, Is.EqualTo(count > 256));
            Assert.That(section.Status, Is.EqualTo(count > 256 ? "partial" : "ok"));
            Assert.That(section.Issues, Is.EqualTo(count > 256 ? new[] { "truncated" } : []));
            Assert.That(section.Data!.Items, Has.Count.EqualTo(provider == Providers[0] ? 256 : 1));
            Assert.That(section.Data.Items[0].Number, Is.EqualTo(1));
            if (provider == Providers[0]) Assert.That(section.Data.Items[^1].Number, Is.EqualTo(265));
        });
    }

    [Test]
    public void MissingProviderAndTruncationKeepBothIssuesInTheirOriginalOrder()
    {
        var query = Complete(); query[Providers[0]] = null;
        query[Providers[1]] = Enumerable.Range(0, 257).Select(i => Row(("Number", i))).ToArray();
        var section = Read(query).Disks;
        Assert.Multiple(() => { Assert.That(section.Status, Is.EqualTo("partial")); Assert.That(section.Truncated, Is.True); Assert.That(section.Data!.Items, Has.Count.EqualTo(256)); Assert.That(section.Issues, Is.EqualTo(new[] { "providerUnavailable", "truncated" })); });
    }

    [Test]
    public void PrimarySourceOrderAndDuplicateNumbersArePreservedWithoutSupplementalRows()
    {
        var query = Complete(); query[Providers[0]] = [Row(("Index", 9)), Row(("Index", 1)), Row(("Index", 9))]; query[Providers[1]] = [];
        var section = Read(query).Disks;
        Assert.That(section.Data!.Items.Select(d => d.Number), Is.EqualTo(new ulong?[] { 9, 1, 9 }));
        Assert.That(section.Data.Items.Select(d => d.MediaType), Is.All.Null);
        Assert.That(section.Status, Is.EqualTo("ok"));
    }

    [TestCase("missing")]
    [TestCase("negative")]
    [TestCase("overflow")]
    public void InvalidPrimaryNumberNeverJoinsStorageOrPartitions(string kind)
    {
        var query = Complete(); var row = query[Providers[0]]![0]; row.Remove("Index");
        if (kind != "missing") row["Index"] = kind == "negative" ? "-1" : "18446744073709551616";
        var disk = Read(query).Disks.Data!.Items.Single();
        Assert.Multiple(() => { Assert.That(disk.Number, Is.Null); Assert.That(disk.MediaType, Is.Null); Assert.That(disk.BusType, Is.Null); Assert.That(disk.HealthStatus, Is.Null); Assert.That(disk.DriveLetters, Is.Null); Assert.That(disk.Model, Is.EqualTo("primary")); });
    }

    [Test]
    public void DuplicateStorageNumbersSuppressAllSupplementalDiskAttribution()
    {
        var query = Complete(); query[Providers[1]] = [query[Providers[1]]![0], Row(("Number", 1), ("UniqueId", "other"))];
        var disk = Read(query).Disks.Data!.Items.Single();
        Assert.Multiple(() => { Assert.That(disk.MediaType, Is.Null); Assert.That(disk.BusType, Is.Null); Assert.That(disk.HealthStatus, Is.Null); Assert.That(disk.DriveLetters, Is.EqualTo(new[] { "C:" })); });
    }

    [TestCase("correct", 4)] [TestCase("case-and-space", 4)]
    [TestCase("different-id", null)] [TestCase("different-format", null)]
    [TestCase("missing-disk-format", null)] [TestCase("missing-media-format", null)]
    [TestCase("blank-id", null)] [TestCase("missing-id", null)]
    [TestCase("duplicate-media", null)] [TestCase("one-matching-format", 4)]
    [TestCase("duplicate-id-other-number", null)] [TestCase("duplicate-id-other-format", null)]
    [TestCase("missing-media-type", null)] [TestCase("unknown-media-type", 999)]
    public void PhysicalMediaRequiresExactUnambiguousIdAndFormatRelationships(string kind, int? expected)
    {
        var query = Complete(); var disk = query[Providers[1]]![0]; var media = query[Providers[2]]![0];
        switch (kind)
        {
            case "case-and-space": disk["UniqueId"] = "  iD  "; media["UniqueId"] = " Id "; media["UniqueIdFormat"] = " +1 "; break;
            case "different-id": media["UniqueId"] = "other"; break;
            case "different-format": media["UniqueIdFormat"] = 2; break;
            case "missing-disk-format": disk.Remove("UniqueIdFormat"); break;
            case "missing-media-format": media.Remove("UniqueIdFormat"); break;
            case "blank-id": disk["UniqueId"] = " \t "; break;
            case "missing-id": disk.Remove("UniqueId"); break;
            case "duplicate-media": query[Providers[2]] = [media, media]; break;
            case "one-matching-format": query[Providers[2]] = [media, Row(("UniqueId", "id"), ("UniqueIdFormat", 2), ("MediaType", 3))]; break;
            case "duplicate-id-other-number": query[Providers[1]] = [disk, Row(("Number", 99), ("UniqueId", "ID"), ("UniqueIdFormat", 1))]; break;
            case "duplicate-id-other-format": query[Providers[1]] = [disk, Row(("Number", 99), ("UniqueId", "ID"), ("UniqueIdFormat", 2))]; break;
            case "missing-media-type": media.Remove("MediaType"); break;
            case "unknown-media-type": media["MediaType"] = 999; break;
        }
        var value = Read(query).Disks.Data!.Items.Single();
        Assert.Multiple(() => { Assert.That(value.MediaType, Is.EqualTo(expected)); Assert.That(value.BusType, Is.EqualTo(17)); Assert.That(value.HealthStatus, Is.EqualTo(2)); });
    }

    [Test]
    public void DuplicateIdentityBeyondTheOutputCapStillSuppressesMediaAttribution()
    {
        var query = Complete(); var disk = query[Providers[1]]![0];
        query[Providers[1]] = new[] { disk }.Concat(Enumerable.Range(2, 255).Select(i => Row(("Number", i), ("UniqueId", "other" + i))))
            .Append(Row(("Number", 999), ("UniqueId", "ID"), ("UniqueIdFormat", 1))).ToArray();
        var section = Read(query).Disks;
        Assert.Multiple(() => { Assert.That(section.Truncated, Is.True); Assert.That(section.Data!.Items.Single().MediaType, Is.Null); Assert.That(section.Data.Items.Single().BusType, Is.EqualTo(17)); });
    }

    [TestCase("C", "C:")] [TestCase("c", "C:")] [TestCase("67", "C:")]
    [TestCase(" +67 ", "C:")] [TestCase("65", "A:")] [TestCase("90", "Z:")]
    [TestCase("64", null)] [TestCase("91", null)] [TestCase("65536", null)]
    [TestCase("", null)] [TestCase("C:", null)] [TestCase("0x43", null)]
    public void DriveLettersRetainNumericCharacterAndAsciiRangeRules(string text, string? expected)
    {
        var query = Complete(); query[Providers[3]] = [Row(("DiskNumber", 1), ("DriveLetter", text))];
        Assert.That(Read(query).Disks.Data!.Items.Single().DriveLetters, Is.EqualTo(expected is null ? Array.Empty<string>() : [expected]));
    }

    [Test]
    public void PartitionLettersAreMatchedDeduplicatedAndSortedWithoutFillingMissingProviders()
    {
        var query = Complete(); query[Providers[3]] = [Row(("DiskNumber", 1), ("DriveLetter", "z")), Row(("DiskNumber", 1), ("DriveLetter", "c")), Row(("DiskNumber", 1), ("DriveLetter", "67")), Row(("DiskNumber", 2), ("DriveLetter", "A")), Row(("DiskNumber", 1))];
        Assert.That(Read(query).Disks.Data!.Items.Single().DriveLetters, Is.EqualTo(new[] { "C:", "Z:" }));
        query[Providers[3]] = []; Assert.That(Read(query).Disks.Data!.Items.Single().DriveLetters, Is.Empty);
        query[Providers[3]] = null; Assert.That(Read(query).Disks.Data!.Items.Single().DriveLetters, Is.Null);
    }

    [TestCase(null, null)] [TestCase("  ", null)] [TestCase(" None ", null)]
    [TestCase(" unknown ", null)] [TestCase("To be filled by O.E.M.", null)] [TestCase(" real ", "real")]
    public void PrimaryDisplayFieldsKeepExistingCleaningRules(string? text, string? expected)
    {
        var query = Complete(); query[Providers[0]]![0]["Model"] = text; query[Providers[0]]![0]["Manufacturer"] = text;
        var disk = Read(query).Disks.Data!.Items.Single();
        Assert.Multiple(() => { Assert.That(disk.Model, Is.EqualTo(expected)); Assert.That(disk.Manufacturer, Is.EqualTo(expected)); });
    }

    [Test]
    public void UnknownCodesAndUnsignedExtremesAreRetainedWithoutInferenceOrOverflow()
    {
        var query = Complete(); query[Providers[0]]![0]["Index"] = ulong.MaxValue; query[Providers[0]]![0]["Size"] = ulong.MaxValue;
        var disk = query[Providers[1]]![0]; disk["Number"] = ulong.MaxValue; disk["BusType"] = 999; disk["HealthStatus"] = 999;
        query[Providers[2]]![0]["MediaType"] = 999; query[Providers[3]]![0]["DiskNumber"] = ulong.MaxValue;
        var value = Read(query).Disks.Data!.Items.Single();
        Assert.Multiple(() => { Assert.That(value.Number, Is.EqualTo(ulong.MaxValue)); Assert.That(value.CapacityBytes, Is.EqualTo(ulong.MaxValue)); Assert.That(value.MediaType, Is.EqualTo(999)); Assert.That(value.BusTypeName, Is.Null); Assert.That(value.HealthStatusName, Is.Null); Assert.That(value.MediaTypeName, Is.Null); Assert.That(value.DriveLetters, Is.EqualTo(new[] { "C:" })); });
    }

    [TestCase("Win32_DiskDrive")] [TestCase("MSFT_Disk")]
    [TestCase("MSFT_PhysicalDisk")] [TestCase("MSFT_Partition")]
    public void CancellationAfterEachQueryStopsBeforeTheNextReadOrDiskProjection(string provider)
    {
        using var cancellation = new CancellationTokenSource(); var query = Complete();
        query.OnRead = name => { if (name == provider) cancellation.Cancel(); };
        var error = Assert.Catch<OperationCanceledException>(() => Read(query, cancellation.Token));
        Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(query.Calls.Select(c => c.Provider), Is.EqualTo(Providers.Take(Array.IndexOf(Providers, provider) + 1)));
    }

    [TestCase("Win32_DiskDrive")] [TestCase("MSFT_Disk")]
    [TestCase("MSFT_PhysicalDisk")] [TestCase("MSFT_Partition")]
    public void ProviderExceptionsRemainUnwrappedAndPreventFollowingQueries(string provider)
    {
        var expected = new IOException("owned inventory failure"); var query = Complete();
        query.OnRead = name => { if (name == provider) throw expected; };
        Assert.That(Assert.Catch(() => Read(query)), Is.SameAs(expected));
        Assert.That(query.Calls.Select(c => c.Provider), Is.EqualTo(Providers.Take(Array.IndexOf(Providers, provider) + 1)));
    }

    [Test]
    public void IdentityPartitionAndDisplayFieldsRetainTheirEvaluationOrder()
    {
        var calls = new List<string>(); object Value(string name, string text) => new TextValue(() => calls.Add(name), text);
        var query = Complete();
        query[Providers[0]] = [Row(("Index", Value("drive-number", "1")), ("Model", Value("model", "name")), ("Manufacturer", Value("maker", "maker")), ("Size", Value("size", "1000")))];
        query[Providers[1]] = [Row(("Number", Value("storage-number", "1")), ("UniqueId", Value("storage-id", "id")), ("UniqueIdFormat", Value("storage-format", "1")), ("BusType", Value("bus", "17")), ("HealthStatus", Value("health", "2")))];
        query[Providers[2]] = [Row(("UniqueId", Value("media-id", "id")), ("UniqueIdFormat", Value("media-format", "1")), ("MediaType", Value("media-type", "4")))];
        query[Providers[3]] = [Row(("DiskNumber", Value("partition-number", "1")), ("DriveLetter", Value("letter", "C")))];
        Assert.That(Read(query).Disks.Data!.Items.Single().MediaType, Is.EqualTo(4));
        Assert.That(calls, Is.EqualTo(new[] { "drive-number", "storage-number", "storage-id", "storage-format", "media-id", "media-format", "storage-format", "storage-id", "partition-number", "letter", "model", "maker", "size", "media-type", "bus", "health" }));
    }

    [TestCase("primary-empty")]
    [TestCase("storage-empty")]
    public void OneEmptySuccessfulSourceAndOneFailedSourceKeepAnEmptyPartialSection(string kind)
    {
        var query = Complete();
        query[Providers[0]] = kind == "primary-empty" ? [] : null;
        query[Providers[1]] = kind == "storage-empty" ? [] : null;
        var section = Read(query).Disks;
        Assert.Multiple(() =>
        {
            Assert.That(section.Status, Is.EqualTo("partial"));
            Assert.That(section.Data!.Items, Is.Empty);
            Assert.That(section.Truncated, Is.False);
            Assert.That(section.Issues, Is.EqualTo(new[] { "providerUnavailable" }));
            Assert.That(query.Calls.Select(c => c.Provider), Is.EqualTo(Providers));
        });
    }


    private sealed class TextValue(Action read, string text) : IFormattable
    {
        public string ToString(string? format, IFormatProvider? provider) { read(); return text; }
    }
    private sealed record Call(string Scope, string Provider, string[] Properties);
    private sealed class Query : Dictionary<string, IReadOnlyList<HardwareRow>?>, IHardwareQuery
    {
        public List<Call> Calls { get; } = [];
        public Action<string>? OnRead { get; set; }
        public IReadOnlyList<HardwareRow>? Read(string scope, string className, params string[] properties)
        {
            if (Providers.Contains(className)) Calls.Add(new(scope, className, properties.ToArray()));
            OnRead?.Invoke(className);
            return TryGetValue(className, out var rows) ? rows : [];
        }
    }
    private sealed class Probe : IHardwareProbe
    {
        public WindowsHardwareProbe.CpuReading Cpu() => new([], 4, 8);
        public (ulong? InstalledBytes, ulong? UsableBytes) Memory() => (16UL, 15UL);
        public IReadOnlyList<WindowsHardwareProbe.GpuReading> Graphics() => [];
        public IReadOnlyList<WindowsHardwareProbe.VolumeReading> Volumes() => [];
    }
}
