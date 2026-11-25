using NUnit.Framework;
using Win32Exception = System.ComponentModel.Win32Exception;

namespace WinMon.NetworkSetup.Tests;

[TestFixture, Category("Integration")]
public sealed class SetupEntryCancellationCharacterizationTests
{
    private const string User = "S-1-5-21-1-2-3-1001";

    [TestCase(null), TestCase(""), TestCase("INSTALL"), TestCase("revoke")]
    public void BeginRejectsUnsupportedOperationsBeforePreparationOrCancellation(string? operation)
    {
        using var entry = new Entry(() => throw new AssertionException("Invalid operation cannot prepare."));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var error = Assert.ThrowsAsync<InvalidDataException>(async () =>
            await SetupService.BeginAsync(operation!, User, null!, cancelled.Token, entry.Host));

        Assert.That(error!.Message, Is.EqualTo("Unsupported setup operation."));
        Assert.That(entry.Preparations, Is.Zero);
        entry.AssertUnchanged();
    }

    [TestCase("null"), TestCase("empty"), TestCase("whitespace"), TestCase("malformed")]
    [TestCase("noncanonical"), TestCase("everyone"), TestCase("anonymous"), TestCase("authenticated")]
    public void RevokeRejectsInvalidIdentitiesBeforePreparationOrCancellation(string kind)
    {
        using var entry = new Entry(() => throw new AssertionException("Invalid identity cannot prepare."));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var sid = kind switch
        {
            "null" => null!, "empty" => "", "whitespace" => " \t", "malformed" => "owned invalid SID",
            "noncanonical" => "s-1-5-21-1-2-3-1001", "everyone" => "S-1-1-0", "anonymous" => "S-1-5-7",
            "authenticated" => "S-1-5-11", _ => throw new AssertionException("Unknown identity partition.")
        };

        var error = Assert.ThrowsAsync<InvalidDataException>(async () =>
            await SetupService.RevokeAsync(sid, cancelled.Token, entry.Host));

        Assert.That(error!.Message, Is.EqualTo("Invalid authorization identity."));
        Assert.That(entry.Preparations, Is.Zero);
        entry.AssertUnchanged();
    }

    [TestCase("begin-install", "io"), TestCase("begin-install", "win32"), TestCase("begin-install", "cancel")]
    [TestCase("begin-remove", "io"), TestCase("begin-remove", "win32"), TestCase("begin-remove", "cancel")]
    [TestCase("remove", "io"), TestCase("remove", "win32"), TestCase("remove", "cancel")]
    [TestCase("revoke", "io"), TestCase("revoke", "win32"), TestCase("revoke", "cancel")]
    public void PreparationFailuresRemainUnwrappedAndOutrankTheAlreadyCancelledRequest(string operation, string kind)
    {
        using var cancelled = new CancellationTokenSource();
        using var other = new CancellationTokenSource();
        cancelled.Cancel();
        other.Cancel();
        Exception original = kind switch
        {
            "io" => new IOException("Owned preparation failure"),
            "win32" => new Win32Exception(5, "Owned preparation failure"),
            "cancel" => new OperationCanceledException("Owned preparation cancellation", other.Token),
            _ => throw new AssertionException("Unknown failure partition.")
        };
        using var entry = new Entry(() => throw original);

        var error = Assert.CatchAsync<Exception>(async () => await entry.Invoke(operation, cancelled.Token));

        Assert.That(error, Is.SameAs(original));
        Assert.That(entry.Preparations, Is.EqualTo(1));
        if (error is OperationCanceledException cancellation)
            Assert.That(cancellation.CancellationToken, Is.EqualTo(other.Token));
        entry.AssertUnchanged();
    }

    [TestCase("begin-install"), TestCase("begin-remove"), TestCase("remove"), TestCase("revoke")]
    public void CancelledRequestsPrepareOnceThenExitBeforeLockStateAndPackageAccess(string operation)
    {
        using var entry = new Entry(() => { });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var error = Assert.ThrowsAsync<OperationCanceledException>(async () => await entry.Invoke(operation, cancelled.Token));

        Assert.That(error!.CancellationToken, Is.EqualTo(cancelled.Token));
        Assert.That(entry.Preparations, Is.EqualTo(1));
        entry.AssertUnchanged();
    }

    private sealed class Entry : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "WinMon.SetupEntry." + Guid.NewGuid().ToString("N"));
        private readonly bool _restart = SetupService.RestartRequired;
        internal SetupHost Host { get; }
        internal int Preparations { get; private set; }

        internal Entry(Action prepare)
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, "marker"), "owned entry baseline");
            Host = new SetupHost
            {
                InstallDirectory = Path.Combine(_root, "never-created-install"),
                PrepareParent = () => { Preparations++; prepare(); },
                EnsureOwnedOrAbsent = () => throw Unexpected(), CaptureState = () => throw Unexpected(),
                StopOwnedIfRunning = _ => throw Unexpected(), CreateOrUpdateOwned = _ => throw Unexpected(),
                StartAndVerify = _ => throw Unexpected(), RestoreService = (_, _) => throw Unexpected(),
                StopAndDeleteOwned = _ => throw Unexpected(), CaptureRegistration = () => throw Unexpected(),
                WriteRegistration = () => throw Unexpected(), RestoreRegistration = _ => throw Unexpected(),
                VerifyPublisher = _ => throw Unexpected(), ProvisionLogs = _ => throw Unexpected()
            };
        }

        internal async Task Invoke(string operation, CancellationToken token)
        {
            switch (operation)
            {
                case "begin-install":
                case "begin-remove":
                    // A cancelled or failed preparation must not read or take ownership of a package.
                    await using (await SetupService.BeginAsync(operation[6..], User, null!, token, Host)) { }
                    break;
                case "remove": await SetupService.RemoveAsync(token, Host); break;
                case "revoke": await SetupService.RevokeAsync(User, token, Host); break;
                default: throw new AssertionException("Unknown entry point.");
            }
        }

        internal void AssertUnchanged()
        {
            Assert.That(Directory.GetDirectories(_root), Is.Empty);
            Assert.That(Directory.GetFiles(_root).Select(Path.GetFileName), Is.EqualTo(new[] { "marker" }));
            Assert.That(File.ReadAllText(Path.Combine(_root, "marker")), Is.EqualTo("owned entry baseline"));
            Assert.That(SetupService.RestartRequired, Is.EqualTo(_restart));
        }
        private static AssertionException Unexpected() => new("Setup entry crossed an unexpected OS boundary.");
        public void Dispose()
        {
            var prefix = Path.Combine(Path.GetTempPath(), "WinMon.SetupEntry.");
            Assert.That(Path.GetFullPath(_root), Does.StartWith(Path.GetFullPath(prefix)));
            Directory.Delete(_root, true);
        }
    }
}
