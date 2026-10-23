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
    public string this[string key] =>
        Strings.ResourceManager.GetString(key, Strings.Culture ?? CultureInfo.CurrentUICulture)!;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void NotifyLanguageChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }
}