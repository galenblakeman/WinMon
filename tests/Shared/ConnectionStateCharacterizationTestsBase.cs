using System.Globalization;
using System.Net.NetworkInformation;
using NUnit.Framework;

namespace WinMon.Tests;

[NonParallelizable]
[SetCulture("en-US")]
[SetUICulture("en-US")]
public abstract class ConnectionStateCharacterizationTestsBase
{
    public static IEnumerable<TestCaseData> StateCases
    {
        get
        {
            (TcpState? State, string Key, string Name)[] cases =
            [
                (TcpState.Closed, "ConnectionsTcpStateClosed", "closed"),
                (TcpState.Listen, "ConnectionsTcpStateListen", "listen"),
                (TcpState.SynSent, "ConnectionsTcpStateSynSent", "syn-sent"),
                (TcpState.SynReceived, "ConnectionsTcpStateSynReceived", "syn-received"),
                (TcpState.Established, "ConnectionsTcpStateEstablished", "established"),
                (TcpState.FinWait1, "ConnectionsTcpStateFinWait1", "fin-wait-1"),
                (TcpState.FinWait2, "ConnectionsTcpStateFinWait2", "fin-wait-2"),
                (TcpState.CloseWait, "ConnectionsTcpStateCloseWait", "close-wait"),
                (TcpState.Closing, "ConnectionsTcpStateClosing", "closing"),
                (TcpState.LastAck, "ConnectionsTcpStateLastAck", "last-ack"),
                (TcpState.TimeWait, "ConnectionsTcpStateTimeWait", "time-wait"),
                (TcpState.DeleteTcb, "ConnectionsTcpStateDeleteTcb", "delete-tcb"),
                (null, "MissingReading", "missing"),
                (TcpState.Unknown, "MissingReading", "unknown"),
                ((TcpState)(-1), "MissingReading", "below-range"),
                ((TcpState)13, "MissingReading", "above-range"),
                ((TcpState)int.MaxValue, "MissingReading", "large-unknown")
            ];
            foreach (var culture in new[] { "en-US", "de-DE", "ja-JP" })
                foreach (var (state, key, name) in cases)
                    yield return new TestCaseData(culture, state, key)
                        .SetName("StateLabelsKeepKnownAndMissingValuesAcrossCultures_" + culture + "_" + name);
        }
    }

    [TestCaseSource(typeof(ConnectionStateCharacterizationTestsBase), nameof(StateCases))]
    public void StateLabelsKeepKnownAndMissingValuesAcrossCultures(string culture, TcpState? state, string resourceKey)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var expected = Resources.Strings.ResourceManager.GetString(resourceKey, CultureInfo.CurrentUICulture);
            Assert.That(expected, Is.Not.Null.And.Not.Empty, "The expected label must exist in the resources.");
            AssertState(state, expected!);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    protected abstract void AssertState(TcpState? state, string expected);
}
