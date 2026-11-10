using System.Globalization;

namespace WinMon.Resources;

public static partial class Strings
{
    public static CultureInfo? Culture => CultureInfo.CurrentUICulture;
    public static string Format(string format, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, arguments);
}
