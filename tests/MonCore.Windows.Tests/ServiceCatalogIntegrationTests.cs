using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace MonCore.Windows.Tests;

[TestFixture, Category("Integration")]
public sealed class ServiceCatalogIntegrationTests
{
    [Test]
    public void DefaultCaptureReturnsNativeServiceNamesAndRawStatusValues()
    {
        var catalog = WindowsServices.Capture();

        Assert.Multiple(() =>
        {
            Assert.That(catalog.ObservedFileTime, Is.GreaterThan(0));
            Assert.That(catalog.Services, Is.Not.Empty);
            Assert.That(catalog.Services.Select(service => service.Name), Is.Unique);
            Assert.That(catalog.Services.All(service => service.Name.Length > 0 && service.DisplayName is not null && service.ProcessId >= 0), Is.True);
        });
    }

    [Test]
    public void DefaultReaderReusesUnchangedObservationsAndKeepsEarlierCatalogsFrozen()
    {
        var reader = new WindowsServices.Reader();
        var first = reader.Read();
        var snapshot = first.Services.ToArray();
        var byName = snapshot.ToDictionary(service => service.Name, StringComparer.Ordinal);
        var second = reader.Read();
        var unchanged = second.Services.Where(service => byName.TryGetValue(service.Name, out var previous) && previous == service).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(first.Services, Is.EqualTo(snapshot));
            Assert.That(unchanged, Is.Not.Empty);
            foreach (var service in unchanged) Assert.That(service, Is.SameAs(byName[service.Name]));
        });
    }
}
