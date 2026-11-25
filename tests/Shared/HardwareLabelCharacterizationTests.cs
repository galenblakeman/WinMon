using System.Globalization;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
public sealed class HardwareLabelCharacterizationTests
{
    private static IEnumerable<TestCaseData> MemoryCases => Cases(
        [(18, "DDR"), (19, "DDR2"), (20, "DDR2FBDIMM"), (24, "DDR3"), (26, "DDR4"),
            (27, "LPDDR"), (28, "LPDDR2"), (29, "LPDDR3"), (30, "LPDDR4"), (34, "DDR5"), (35, "LPDDR5")],
        [null, 0, 17, 21, 23, 25, 31, 33, 36, 65536, uint.MaxValue, ulong.MaxValue]);

    private static IEnumerable<TestCaseData> BusCases => Cases(
        [(1, "SCSI"), (2, "ATAPI"), (3, "ATA"), (4, "IEEE1394"), (5, "SSA"), (6, "FibreChannel"),
            (7, "USB"), (8, "RAID"), (9, "ISCSI"), (10, "SAS"), (11, "SATA"), (12, "SD"), (13, "MMC"),
            (14, "VirtualBus"), (15, "FileBackedVirtual"), (16, "StorageSpaces"), (17, "NVMe")],
        [null, 0, 18, 255, 65536, ulong.MaxValue]);

    private static IEnumerable<TestCaseData> MediaCases => Cases(
        [(3, "HDD"), (4, "SSD"), (5, "StorageClassMemory")], [null, 0, 2, 6, ulong.MaxValue]);

    private static IEnumerable<TestCaseData> HealthCases => Cases(
        [(0, "Healthy"), (1, "Warning"), (2, "Unhealthy")], [null, 3, ulong.MaxValue]);

    private static IEnumerable<TestCaseData> Cases((ulong Code, string Key)[] known, ulong?[] unknown)
    {
        foreach (var culture in new[] { "en-US", "de-DE", "ja-JP" })
        {
            foreach (var (code, key) in known) yield return new TestCaseData(culture, (ulong?)code, key);
            foreach (var code in unknown) yield return new TestCaseData(culture, code, "Unavailable");
        }
    }

    [TestCaseSource(nameof(MemoryCases))]
    public void MemoryCodesKeepLocalizedTypeAndUnknownFallback(string culture, ulong? code, string key) =>
        Check(culture, key, () => HardwareInventory.MemoryType(code));

    [TestCaseSource(nameof(BusCases))]
    public void BusCodesKeepLocalizedConnectionAndUnknownFallback(string culture, ulong? code, string key) =>
        Check(culture, key, () => HardwareInventory.BusType(code));

    [TestCaseSource(nameof(MediaCases))]
    public void MediaCodesKeepLocalizedTechnologyAndUnknownFallback(string culture, ulong? code, string key) =>
        Check(culture, key, () => HardwareInventory.MediaType(code));

    [TestCaseSource(nameof(HealthCases))]
    public void HealthCodesKeepProviderStatusAndUnknownFallback(string culture, ulong? code, string key) =>
        Check(culture, key, () => HardwareInventory.Health(code));

    private static void Check(string culture, string key, Func<string> label)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            var selected = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = selected;
            var expected = Strings.ResourceManager.GetString(key, selected);
            Assert.That(expected, Is.Not.Null.And.Not.Empty, "The expected resource must exist.");
            Assert.That(label(), Is.EqualTo(expected));
            Assert.That(label(), Is.EqualTo(expected), "Repeated reads must retain the same label.");
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
