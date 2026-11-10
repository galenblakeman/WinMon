using System.Globalization;

namespace WinMon.Controls;

internal static partial class UsageText
{
    /// <summary>Decimal units, matching the MB/s used for rates elsewhere.</summary>
    internal static string Bytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (bytes >= 1000 && unit < units.Length - 1)
        {
            bytes /= 1000;
            unit++;
        }

        return (unit == 0
            ? bytes.ToString("N0", CultureInfo.CurrentCulture)
            : bytes.ToString("N1", CultureInfo.CurrentCulture)) + " " + units[unit];
    }

}
