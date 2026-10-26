namespace WinMon.Monitoring;

/// <summary>WinMon's localized policy for writing a dump of a verified process lifetime.</summary>
internal static class ProcessDumpWriter
{
    internal static bool IsEligible(ProcessUsage process, bool live, bool grouped) =>
        WindowsProcessDumpWriter.IsEligible(process, live, grouped);

    internal static void Write(ProcessUsage process, string destination, bool fullMemory) =>
        WindowsProcessDumpWriter.Write(process, destination, fullMemory,
            Strings.ProcessDumpUnavailable,
            Strings.TheOriginalProcessHasExitedAndItsPIDHasBeen,
            Strings.ThisProcessHasAlreadyExited);
}
