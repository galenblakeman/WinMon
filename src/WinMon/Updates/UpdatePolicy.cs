namespace WinMon.Updates;

/// <summary>Store packages are serviced by Microsoft Store, never by the GitHub updater.</summary>
internal static class UpdatePolicy
{
    internal static bool GitHubUpdatesEnabled =>
#if WINMON_STORE
        false;
#else
        true;
#endif

    internal static void RequireGitHubUpdates()
    {
        if (!GitHubUpdatesEnabled)
            throw new InvalidOperationException("Updates for this build are managed by Microsoft Store.");
    }
}
