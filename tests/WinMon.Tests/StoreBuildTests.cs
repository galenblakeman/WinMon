using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Preferences;
using WinMon.Resources;
using WinMon.Updates;

namespace WinMon.Tests;

[TestFixture]
public sealed class StoreBuildTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void Settings_OnlyOffersGitHubControlsInOrdinaryBuilds()
    {
        var window = new SettingsWindow(new MonitorPreferences(), () => [], checkForUpdates: () => Task.FromResult("checked"));
        try
        {
            // Store builds have no Updates section, so this leaves the General section showing.
            window.ShowSection(SettingsSection.Updates);
            Assert.That(window.Section == SettingsSection.Updates, Is.EqualTo(UpdatePolicy.GitHubUpdatesEnabled));
            var elements = Descendants(window).ToArray();
            Assert.That(elements.OfType<CheckBox>().Any(c => Equals(c.Content, Strings.AutomaticallyCheckForUpdates)),
                Is.EqualTo(UpdatePolicy.GitHubUpdatesEnabled));
            Assert.That(elements.OfType<Button>().Any(c => Equals(c.Content, Strings.CheckForUpdates)),
                Is.EqualTo(UpdatePolicy.GitHubUpdatesEnabled));
        }
        finally { window.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    [Test]
    public void UpdatePolicy_MatchesBuildChannel()
    {
#if WINMON_STORE
        Assert.That(UpdatePolicy.GitHubUpdatesEnabled, Is.False);
#else
        Assert.That(UpdatePolicy.GitHubUpdatesEnabled, Is.True);
        Assert.DoesNotThrow(UpdatePolicy.RequireGitHubUpdates);
#endif
    }

#if WINMON_STORE
    [Test]
    public void StoreBuild_RejectsChecksDownloadsAndHandoffBeforeAnySideEffects()
    {
        using var handler = new RejectNetworkHandler();
        using var client = new UpdateClient(new HttpClient(handler), "", "invalid-directory");
        Assert.ThatAsync(() => client.CheckAsync("1.0.0", CancellationToken.None), Throws.InvalidOperationException);
        Assert.ThatAsync(() => client.DownloadAsync(null!, null, CancellationToken.None), Throws.InvalidOperationException);
        Assert.ThatAsync(() => UpdateLauncher.StartAsync(null!, CancellationToken.None), Throws.InvalidOperationException);
        Assert.That(handler.Requests, Is.Zero);
        Assert.That(UpdateRuntime.RunHelper("invalid-directory", "invalid-token"), Is.EqualTo(1));
        UpdateRuntime.AcceptResultArguments(["--update-result", "invalid-directory", "invalid-token"]);
        UpdateRuntime.SignalReady();
        Assert.That(UpdateRuntime.IsExpectedRestart, Is.False);
        Assert.That(UpdateRuntime.IsUpdating(), Is.False);
        Assert.That(UpdateRuntime.ConsumeNotice(), Is.Null);
    }

    private sealed class RejectNetworkHandler : HttpMessageHandler
    {
        internal int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            throw new AssertionException("Store builds must not contact the GitHub updater.");
        }
    }
#endif
}
