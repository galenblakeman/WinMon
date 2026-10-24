using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Preferences;

/// <summary>The Settings section for automatic captures: one row per rule, cooldown, folder and notifications.</summary>
internal sealed class AutoCaptureSection : StackPanel
{
    private readonly CheckBox _enabled;
    private readonly CheckBox _notify;
    private readonly TextBox _cooldown;
    private readonly TextBox _folder;
    private readonly string _defaultFolder;
    private readonly List<(CaptureMetric Metric, CheckBox Enabled, TextBox Threshold, TextBox Seconds)> _rules = [];

    internal AutoCaptureSection(AutoCapturePreferences preferences, string defaultFolder, string primaryDrive,
        string? secondaryDrive)
    {
        _defaultFolder = defaultFolder;
        _enabled = Check(Strings.AutoCaptureEnable, preferences.Enabled);
        _enabled.FontWeight = FontWeights.SemiBold;
        Children.Add(_enabled);
        Children.Add(Note(Strings.AutoCaptureHelp, new Thickness(0, 4, 0, 8)));

        var table = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        // The label takes the spare width; every other column fits its content.
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var column = 1; column < 7; column++)
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var row = 0;
        foreach (var rule in preferences.Rules)
        {
            if (rule.Metric == CaptureMetric.Process)
            {
                table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
                row++;
            }

            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = rule.Metric switch
            {
                CaptureMetric.Cpu => Strings.CPU, CaptureMetric.Memory => Strings.Memory,
                CaptureMetric.PrimaryDisk => Strings.Format(Strings.DiskLabel, primaryDrive),
                CaptureMetric.SecondaryDisk => secondaryDrive is null
                    ? Strings.SecondDrive
                    : Strings.Format(Strings.DiskLabel, secondaryDrive),
                CaptureMetric.Network => Strings.Network, CaptureMetric.Gpu => Strings.GPU,
                _ => Strings.AutoCaptureAnyProcess
            };
            var enabled = Check(label, rule.Enabled);
            var threshold = Number(rule.Threshold, Strings.Format(Strings.AutoCaptureThresholdFor, label));
            var seconds = Number(rule.Seconds, Strings.Format(Strings.AutoCaptureSecondsFor, label));
            var unit = rule.Metric switch
            {
                CaptureMetric.Network => "Mbps", CaptureMetric.Process => Strings.AutoCapturePercentOfCore,
                CaptureMetric.PrimaryDisk or CaptureMetric.SecondaryDisk => Strings.AutoCapturePercentActive,
                _ => "%"
            };
            Place(enabled, 0);
            Place(Muted(Strings.AutoCaptureAbove), 1);
            Place(threshold, 2);
            Place(Muted(unit), 3);
            Place(Muted(Strings.AutoCaptureFor), 4);
            Place(seconds, 5);
            Place(Muted(Strings.AutoCaptureSecondsUnit), 6);
            _rules.Add((rule.Metric, enabled, threshold, seconds));
            row++;
        }

        Children.Add(table);
        Children.Add(Note(Strings.AutoCaptureProcessHelp, new Thickness(0, 0, 0, 8)));

        _cooldown = Number(preferences.CooldownMinutes, Strings.AutoCaptureCooldown);
        var cooldown = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        cooldown.Children.Add(Text(Strings.AutoCaptureWaitBefore));
        _cooldown.Margin = new Thickness(0, 0, 6, 0);
        cooldown.Children.Add(_cooldown);
        cooldown.Children.Add(Text(Strings.AutoCaptureWaitAfter));
        Children.Add(cooldown);

        Children.Add(Text(Strings.AutoCaptureFolder));
        _folder = new TextBox
        {
            Text = preferences.Folder ?? defaultFolder, IsReadOnly = true, MinHeight = 26,
            VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 0)
        };
        _folder.SetResourceReference(Control.BackgroundProperty, "Raised");
        _folder.SetResourceReference(Control.ForegroundProperty, "Text");
        AutomationProperties.SetName(_folder, Strings.AutoCaptureFolder);
        var change = ActionButton(Strings.AutoCaptureChangeFolder, ChooseFolder);
        var open = ActionButton(Strings.AutoCaptureOpenFolder, OpenFolder);
        var folder = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(open, Dock.Right);
        DockPanel.SetDock(change, Dock.Right);
        folder.Children.Add(open);
        folder.Children.Add(change);
        folder.Children.Add(_folder);
        Children.Add(folder);
        Children.Add(Note(Strings.Format(Strings.AutoCaptureKeepNote, CaptureReports.Keep), new Thickness(0, 4, 0, 8)));
        _notify = Check(Strings.AutoCaptureNotify, preferences.Notify);
        Children.Add(_notify);
        return;

        void Place(UIElement element, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            if (element is FrameworkElement framework) framework.Margin = new Thickness(column == 0 ? 0 : 6, 3, 0, 3);
            table.Children.Add(element);
        }
    }

    /// <summary>The section's settings, or null with a message when a number is outside its range.</summary>
    internal AutoCapturePreferences? Read(out string? error)
    {
        error = null;
        var rules = new List<CaptureRuleSetting>();
        foreach (var (metric, enabled, thresholdBox, secondsBox) in _rules)
        {
            var minimum = metric == CaptureMetric.Network ? 0.1 : 1;
            var maximum = CaptureRuleSetting.MaximumThreshold(metric);
            if (!double.TryParse(thresholdBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var threshold) ||
                threshold < minimum || threshold > maximum)
            {
                error = Strings.Format(Strings.AutoCaptureInvalidThreshold, minimum, maximum);
                thresholdBox.Focus();
                return null;
            }

            if (!int.TryParse(secondsBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var seconds) ||
                seconds is < 1 or > 300)
            {
                error = Strings.AutoCaptureInvalidSeconds;
                secondsBox.Focus();
                return null;
            }

            rules.Add(new CaptureRuleSetting(metric, enabled.IsChecked == true, threshold, seconds));
        }

        if (!int.TryParse(_cooldown.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var cooldown) ||
            cooldown is < 1 or > 1440)
        {
            error = Strings.AutoCaptureInvalidCooldown;
            _cooldown.Focus();
            return null;
        }

        var folder = _folder.Text.Trim();
        return new AutoCapturePreferences
        {
            Enabled = _enabled.IsChecked == true, Rules = [.. rules], CooldownMinutes = cooldown,
            Folder = string.Equals(folder, _defaultFolder, StringComparison.OrdinalIgnoreCase) ? null : folder,
            Notify = _notify.IsChecked == true
        };
    }

    /// <summary>Whether the reports folder can be created and written; checked on save for a chosen folder.</summary>
    internal static bool CanWrite(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".winmon-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                                       NotSupportedException)
        {
            return false;
        }
    }

    internal string Folder => _folder.Text.Trim();

    private void ChooseFolder()
    {
        var dialog = new OpenFolderDialog { Title = Strings.AutoCaptureFolder, InitialDirectory = Folder };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) _folder.Text = dialog.FolderName;
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Process.Start(new ProcessStartInfo(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
                { UseShellExecute = false, Arguments = $"\"{Folder}\"" })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageDialog.ShowNotice(Window.GetWindow(this), ex.Message, Strings.AutoCaptureFolder);
        }
    }

    private static CheckBox Check(string content, bool isChecked)
    {
        var box = new CheckBox { Content = content, IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Center };
        box.SetResourceReference(Control.ForegroundProperty, "Text");
        return box;
    }

    private static TextBox Number(double value, string name)
    {
        var box = new TextBox
        {
            Text = value.ToString("0.##", CultureInfo.CurrentCulture), Width = 52, MinHeight = 24,
            TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center
        };
        box.SetResourceReference(Control.BackgroundProperty, "Raised");
        box.SetResourceReference(Control.ForegroundProperty, "Text");
        AutomationProperties.SetName(box, name);
        return box;
    }

    private static TextBlock Text(string text)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        return block;
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return block;
    }

    private static TextBlock Note(string text, Thickness margin)
    {
        var block = new TextBlock { Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = margin };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return block;
    }

    private static Button ActionButton(string label, Action click)
    {
        var button = new Button { Content = label, MinWidth = 70, Margin = new Thickness(0, 4, 6, 0) };
        button.SetResourceReference(Control.BackgroundProperty, "Raised");
        button.SetResourceReference(Control.ForegroundProperty, "Text");
        button.Click += (_, _) => click();
        return button;
    }
}
