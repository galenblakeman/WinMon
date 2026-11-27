using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace WinMon.Resources;

public static partial class Strings
{
    private static CultureInfo? _culture;

    /// <summary>The selected UI culture, shared by existing asynchronous operations too.</summary>
    public static CultureInfo? Culture => Volatile.Read(ref _culture);

    public static LocalizedTextSource Text { get; } = new();

    // Views read the same text every second; ResourceManager takes a lock and walks the culture fallbacks each time.
    private sealed record TextCache(CultureInfo Culture, ConcurrentDictionary<string, string> Values);
    private static TextCache? _text;

    /// <summary>Resource text for the selected UI culture, read once per culture.</summary>
    internal static string Get(string name)
    {
        var culture = Culture ?? CultureInfo.CurrentUICulture;
        var cache = Volatile.Read(ref _text);
        if (cache is null || !ReferenceEquals(cache.Culture, culture))
            Volatile.Write(ref _text, cache = new TextCache(culture, new ConcurrentDictionary<string, string>()));
        return cache.Values.GetOrAdd(name, static (key, culture) => ResourceManager.GetString(key, culture)!, culture);
    }

    internal static void SetCulture(CultureInfo? culture)
    {
        if (Equals(Culture, culture)) return;
        Volatile.Write(ref _culture, culture);
        Text.NotifyLanguageChanged();
    }

    /// <summary>Format localized text using the user's number and date conventions.</summary>
    public static string Format(string format, params object?[] arguments)
    {
        return string.Format(CultureInfo.CurrentCulture, format, arguments);
    }
}

/// <summary>A binding source that refreshes resource text without rebuilding windows.</summary>
public sealed class LocalizedTextSource : INotifyPropertyChanged
{
    public string this[string key] => Strings.Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void NotifyLanguageChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }
}
