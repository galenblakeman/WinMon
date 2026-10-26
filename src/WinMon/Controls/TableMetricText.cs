namespace WinMon.Controls;

/// <summary>Display table measurements in a unit chosen for each value.</summary>
internal static class TableMetricText
{
    internal static string Memory(ulong bytes)
    {
        double value = bytes;
        var format = Strings.TableBytes;
        if (bytes >= 1_099_511_627_776)
        {
            value /= 1_099_511_627_776d;
            format = Strings.TableTiB;
        }
        else if (bytes >= 1_073_741_824)
        {
            value /= 1_073_741_824d;
            format = Strings.TableGiB;
        }
        else if (bytes >= 1_048_576)
        {
            value /= 1_048_576d;
            format = Strings.TableMiB;
        }
        else if (bytes >= 1024)
        {
            value /= 1024d;
            format = Strings.TableKiB;
        }

        return Strings.Format(format, value);
    }

    internal static string IoRate(double bytesPerSecond) =>
        Strings.Format(Strings.TablePerSecond, UsageText.Bytes(bytesPerSecond));
}
