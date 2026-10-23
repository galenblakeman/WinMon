using System.Text;
using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>Plain-text export of the displayed snapshot, including all hardware details.</summary>
internal static class HardwareInfoText
{
    internal static string Format(IReadOnlyList<WindowsHardwareInfo.Section> sections, DateTimeOffset capturedAt)
    {
        var text = new StringBuilder();
        text.AppendLine(Strings.Format(Strings.ProductVersion, AppInfo.Version));
        text.AppendLine(AppInfo.Description);
        text.AppendLine(Strings.Format(Strings.License, AppInfo.Copyright, AppInfo.License));
        text.AppendLine(AppInfo.RepositoryUrl);
        text.AppendLine(Strings.Format(Strings.ExportSnapshotTime, capturedAt));
        foreach (var section in sections)
        {
            text.AppendLine();
            text.AppendLine(section.Name);
            text.AppendLine(section.Details);
            if (section.Items is not { Count: > 0 }) continue;
            text.AppendLine();
            text.AppendLine(section.DetailHeading);
            foreach (var item in section.Items)
            {
                text.AppendLine(item.Name);
                text.AppendLine(item.Text);
                text.AppendLine();
            }
        }

        return text.ToString().TrimEnd();
    }
}