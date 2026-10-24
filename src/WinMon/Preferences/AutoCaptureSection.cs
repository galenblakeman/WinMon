using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shapes;
using Microsoft.Win32;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Preferences;

/// <summary>
/// The Settings page for automatic captures: a rule table, cooldown, folder and notifications. Its on/off switch,
/// <see cref="EnabledSwitch"/>, sits in the page header, outside this panel.
/// </summary>
internal sealed class AutoCaptureSection : StackPanel
{
    private readonly CheckBox _enabled;
    private readonly CheckBox _notify;
    private readonly TextBox _cooldown;
    private readonly TextBox _folder;
    private readonly string _defaultFolder;
    private readonly List<Rule> _rules = [];

    private sealed record Rule(CaptureMetric Metric, CheckBox Enabled, TextBlock Label, TextBox Threshold, TextBox Seconds);

    internal AutoCaptureSection(AutoCapturePreferences preferences, string defaultFolder, string primaryDrive,
        string? secondaryDrive)
    {
        _defaultFolder = defaultFolder;
        _enabled = new CheckBox { IsChecked = preferences.Enabled, VerticalAlignment = VerticalAlignment.Center };
        _enabled.SetResourceReference(StyleProperty, "SettingsSwitch");
        AutomationProperties.SetName(_enabled, Strings.AutoCaptureEnable);
        _enabled.ToolTip = Strings.AutoCaptureEnable;
        _enabled.Checked += (_, _) => OnEnabledChanged();
        _enabled.Unchecked += (_, _) => OnEnabledChanged();
        OnEnabledChanged();
        Children.Add(Note(Strings.AutoCaptureHelp, new Thickness(0, 0, 0, 10)));

        var table = new Grid();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var column = 1; column < 5; column++)
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(Header(Strings.AutoCaptureRuleHeader), 0, 0);
        Place(Header(Strings.AutoCaptureAboveHeader), 0, 1, 2);
        Place(Header(Strings.AutoCaptureForHeader), 0, 3, 2);
        var row = 1;
        foreach (var setting in preferences.Rules)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var swatch = new Rectangle
                { Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Shape.FillProperty,
                setting.Metric == CaptureMetric.Process ? "Muted" : CaptureReportText.BrushKey(setting.Metric));
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(swatch);
            content.Children.Add(label);
            var enabled = new CheckBox
                { Content = content, IsChecked = setting.Enabled, VerticalAlignment = VerticalAlignment.Center };
            enabled.SetResourceReference(Control.ForegroundProperty, "Text");
            if (setting.Metric == CaptureMetric.Process) enabled.ToolTip = Strings.AutoCaptureProcessHelp;
            var threshold = Number(setting.Threshold);
            var seconds = Number(setting.Seconds);
            var unit = setting.Metric switch
            {
                CaptureMetric.Network => "Mbps", CaptureMetric.Process => Strings.AutoCapturePercentOfCore,
                CaptureMetric.PrimaryDisk or CaptureMetric.SecondaryDisk => Strings.AutoCapturePercentActive,
                _ => "%"
            };
            Place(enabled, row, 0);
            Place(threshold, row, 1);
            Place(Muted(unit), row, 2);
            Place(seconds, row, 3);
            Place(Muted(Strings.AutoCaptureSecondsUnit), row, 4);
            _rules.Add(new Rule(setting.Metric, enabled, label, threshold, seconds));
            row++;
        }

        UpdateDrives(primaryDrive, secondaryDrive);
        var frame = new Border
        {
            Child = table, Padding = new Thickness(12, 6, 12, 6), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 10)
        };
        frame.SetResourceReference(Border.BackgroundProperty, "SectionHeaderSurface");
        frame.SetResourceReference(Border.BorderBrushProperty, "Line");
        AutomationProperties.SetName(table, Strings.AutoCaptureHeading);
        Children.Add(frame);

        _cooldown = Number(preferences.CooldownMinutes);
        AutomationProperties.SetName(_cooldown, Strings.AutoCaptureCooldown);
        _cooldown.Margin = new Thickness(0, 0, 6, 0);
        var cooldown = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        cooldown.Children.Add(Text(Strings.AutoCaptureWaitBefore));
        cooldown.Children.Add(_cooldown);
        cooldown.Children.Add(Text(Strings.AutoCaptureWaitAfter));
        Children.Add(cooldown);

        var folderLabel = Muted(Strings.AutoCaptureFolder);
        Children.Add(folderLabel);
        _folder = new TextBox
        {
            Text = preferences.Folder ?? defaultFolder, IsReadOnly = true, MinHeight = 28,
            VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 0)
        };
        _folder.SetResourceReference(Control.BackgroundProperty, "Raised");
        _folder.SetResourceReference(Control.ForegroundProperty, "Text");
        AutomationProperties.SetLabeledBy(_folder, folderLabel);
        var change = ActionButton(Strings.AutoCaptureChangeFolder, ChooseFolder);
        var open = ActionButton(Strings.AutoCaptureOpenFolder, OpenFolder);
        var folder = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(open, Dock.Right);
        DockPanel.SetDock(change, Dock.Right);
        folder.Children.Add(open);
        folder.Children.Add(change);
        folder.Children.Add(_folder);
        Children.Add(folder);
        Children.Add(Note(Strings.Format(Strings.AutoCaptureKeepNote, CaptureReports.Keep), new Thickness(0, 4, 0, 10)));
        _notify = new CheckBox { Content = Strings.AutoCaptureNotify, IsChecked = preferences.Notify };
        _notify.SetResourceReference(Control.ForegroundProperty, "Text");
        Children.Add(_notify);
        return;

        void Place(UIElement element, int at, int column, int span = 1)
        {
            Grid.SetRow(element, at);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, span);
            if (element is FrameworkElement framework)
                framework.Margin = new Thickness(column == 0 ? 0 : 6, 3, 0, 3);
            table.Children.Add(element);
        }
    }

    /// <summary>The page's on/off switch, placed in its header by the Settings window.</summary>
    internal CheckBox EnabledSwitch => _enabled;

    /// <summary>Raised when the switch changes, so the sidebar can show On or Off.</summary>
    internal event Action<bool>? EnabledChanged;

    internal bool IsOn => _enabled.IsChecked == true;

    internal string Folder => _folder.Text.Trim();

    private void OnEnabledChanged()
    {
        _enabled.Content = IsOn ? Strings.SettingsOn : Strings.SettingsOff;
        EnabledChanged?.Invoke(IsOn);
    }

    /// <summary>Follow the drive selectors: label the disk rules and disable the second-disk rule without one.</summary>
    internal void UpdateDrives(string? primaryDrive, string? secondaryDrive)
    {
        foreach (var rule in _rules)
        {
            rule.Label.Text = rule.Metric switch
            {
                CaptureMetric.Cpu => Strings.CPU, CaptureMetric.Memory => Strings.Memory,
                CaptureMetric.PrimaryDisk => primaryDrive is null
                    ? Strings.Disk
                    : Strings.Format(Strings.DiskLabel, primaryDrive),
                CaptureMetric.SecondaryDisk => secondaryDrive is null
                    ? Strings.SecondDrive
                    : Strings.Format(Strings.DiskLabel, secondaryDrive),
                CaptureMetric.Network => Strings.Network, CaptureMetric.Gpu => Strings.GPU,
                _ => Strings.AutoCaptureAnyProcess
            };
            AutomationProperties.SetName(rule.Threshold,
                Strings.Format(Strings.AutoCaptureThresholdFor, rule.Label.Text));
            AutomationProperties.SetName(rule.Seconds, Strings.Format(Strings.AutoCaptureSecondsFor, rule.Label.Text));
            if (rule.Metric != CaptureMetric.SecondaryDisk) continue;
            // The values are kept, so choosing a second drive again restores the rule as it was.
            var available = secondaryDrive is not null;
            rule.Enabled.IsEnabled = rule.Threshold.IsEnabled = rule.Seconds.IsEnabled = available;
        }
    }

    /// <summary>The page's settings, or null with a message and the field to fix when a number is out of range.</summary>
    internal AutoCapturePreferences? Read(out string? error, out Control? invalid)
    {
        error = null;
        invalid = null;
        var rules = new List<CaptureRuleSetting>();
        foreach (var rule in _rules)
        {
            var minimum = rule.Metric == CaptureMetric.Network ? 0.1 : 1;
            var maximum = CaptureRuleSetting.MaximumThreshold(rule.Metric);
            if (!double.TryParse(rule.Threshold.Text, NumberStyles.Float, CultureInfo.CurrentCulture,
                    out var threshold) || threshold < minimum || threshold > maximum)
            {
                error = Strings.Format(Strings.AutoCaptureInvalidThreshold, minimum, maximum);
                invalid = rule.Threshold;
                return null;
            }

            if (!int.TryParse(rule.Seconds.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var seconds) ||
                seconds is < 1 or > 300)
            {
                error = Strings.AutoCaptureInvalidSeconds;
                invalid = rule.Seconds;
                return null;
            }

            rules.Add(new CaptureRuleSetting(rule.Metric, rule.Enabled.IsChecked == true, threshold, seconds));
        }

        if (!int.TryParse(_cooldown.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var cooldown) ||
            cooldown is < 1 or > 1440)
        {
            error = Strings.AutoCaptureInvalidCooldown;
            invalid = _cooldown;
            return null;
        }

        var folder = Folder;
        return new AutoCapturePreferences
        {
            Enabled = IsOn, Rules = [.. rules], CooldownMinutes = cooldown,
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
            var probe = System.IO.Path.Combine(folder, $".winmon-{Guid.NewGuid():N}.tmp");
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
            Process.Start(new ProcessStartInfo(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
                { UseShellExecute = false, Arguments = $"\"{Folder}\"" })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageDialog.ShowNotice(Window.GetWindow(this), ex.Message, Strings.AutoCaptureFolder);
        }
    }

    private static TextBox Number(double value)
    {
        var box = new TextBox
        {
            Text = value.ToString("0.##", CultureInfo.CurrentCulture), Width = 52, MinHeight = 24,
            TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center
        };
        box.SetResourceReference(Control.BackgroundProperty, "Raised");
        box.SetResourceReference(Control.ForegroundProperty, "Text");
        return box;
    }

    private static TextBlock Header(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return block;
    }

    private static TextBlock Text(string text)
    {
        var block = new TextBlock
            { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
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
        var block = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = margin };
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
