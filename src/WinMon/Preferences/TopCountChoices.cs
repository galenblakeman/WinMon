namespace WinMon.Preferences;

/// <summary>Zero means all available entries; finite choices remain integers for existing dropdown callers.</summary>
internal static class TopCountChoices
{
    private sealed class AllChoice
    {
        public override string ToString()
        {
            return Strings.AllEntries;
        }
    }

    internal static object All { get; } = new AllChoice();
    internal static object[] Items => [5, 10, 15, 20, 50, All];

    internal static bool IsSupported(int count)
    {
        return count is 0 or 5 or 10 or 15 or 20 or 50;
    }

    internal static object Selection(int count)
    {
        return count == 0 ? All : count;
    }

    internal static bool TryGetCount(object? selection, out int count)
    {
        count = selection is int number ? number : 0;
        return ReferenceEquals(selection, All) || (selection is int && IsSupported(count));
    }

    internal static IEnumerable<T> Limit<T>(IEnumerable<T> entries, int count)
    {
        return count == 0 ? entries : entries.Take(count);
    }
}