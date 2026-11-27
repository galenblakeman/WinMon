using System.Globalization;

namespace WinMon.Resources;

public static partial class Strings
{
    public static CultureInfo? Culture => CultureInfo.CurrentUICulture;
    internal static string Get(string name) => ResourceManager.GetString(name, Culture)!;
    public static string Format(string format, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, arguments);
}
