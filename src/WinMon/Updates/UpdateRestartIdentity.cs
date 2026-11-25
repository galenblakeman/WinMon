namespace WinMon.Updates;

internal static class UpdateRestartIdentity
{
    internal static bool IsExpectedRestart(string? resultToken) => resultToken is not null;
    internal static (string Path, string Token)? RestartHistory(string? resultToken, string? resultDirectory) =>
        resultToken is { } token && resultDirectory is { } directory
            ? (Path.Combine(directory, MonCore.Monitoring.RestartHistory.FileName), token)
            : null;
}
