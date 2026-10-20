using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using WinMon.Shortcuts;

namespace WinMon.Preferences;

/// <summary>
/// Provides controls for editing monitor preferences before they are persisted.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly MonitorPreferences _initialPreferences;
    private readonly ComboBox _themeSelector;
    private readonly ComboBox _paletteSelector;
    private readonly ComboBox _retentionSelector;
    private readonly CheckBox _minimizeToTrayCheckBox;
    private readonly CheckBox _startupCheckBox;
    private readonly ComboBox _primaryDriveSelector;
    private readonly ComboBox _secondaryDriveSelector;
    private readonly TextBox _shortcutBox;
    private readonly TextBlock _shortcutStatus;
    private readonly CheckBox _launchCheckBox;
    private readonly Func<HotkeyGesture, bool> _isShortcutAvailable;
    private HotkeyGesture? _shortcut;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
    /// </summary>
    /// <param name="preferences">The preferences shown when the dialog opens.</param>
    /// <param name="isShortcutAvailable">Reports whether no other application holds a recorded shortcut.</param>
    /// <param name="shortcutStatus">Describes whether the current shortcut works.</param>
    public SettingsWindow(MonitorPreferences preferences, Func<HotkeyGesture, bool>? isShortcutAvailable = null, string shortcutStatus = "")
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _initialPreferences = preferences;
        _isShortcutAvailable = isShortcutAvailable ?? (_ => true);
        _shortcut = preferences.ShowGesture;
        Controls.WindowTheme.Attach(this);

        Title = "WinMon settings";
        Width = 460;
        Height = 810;
        MinWidth = 420;
        MinHeight = 810;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Text");

        _themeSelector = CreateSelector(["Dark", "Light", "System"], preferences.Theme);
        _paletteSelector = CreateSelector(ChartPalettes.Names, ChartPalettes.Normalize(preferences.ChartPalette));
        _paletteSelector.ToolTip = "Resource colors for charts, labels and legends. High contrast uses Windows colors.";
        _retentionSelector = CreateSelector(["10 minutes", "30 minutes", "60 minutes"], RetentionLabel(preferences.RetentionSeconds));
        _minimizeToTrayCheckBox = CreateCheckBox("Minimize to the notification area", preferences.MinimizeToTray);
        _startupCheckBox = CreateCheckBox("Start WinMon when I sign in", preferences.StartWithWindows);
        _shortcutBox = CreateShortcutBox();
        _shortcutStatus = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        _shortcutStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        AutomationProperties.SetLiveSetting(_shortcutStatus, AutomationLiveSetting.Polite);
        _launchCheckBox = CreateCheckBox("Also start WinMon with this shortcut", preferences.LaunchWithShortcut);
        _launchCheckBox.ToolTip = "Adds WinMon to your Start menu. Windows then handles the shortcut, so it works even when WinMon isn't running.";
        ShowShortcut(shortcutStatus);

        _primaryDriveSelector = CreateDriveSelector(preferences.PrimaryDrive, false);
        _secondaryDriveSelector = CreateDriveSelector(preferences.SecondaryDrive, true);

        Grid layout = new()
        {
            Margin = new Thickness(24)
        };
        for (var row = 0; row <= 30; row++)
            layout.RowDefinitions.Add(new RowDefinition { Height = row % 2 == 0 ? GridLength.Auto : new GridLength(row is 7 or 11 or 19 or 29 ? 24 : 12) });
        TextBlock heading = CreateHeading("Appearance");
        Grid.SetRow(heading, 0);
        layout.Children.Add(heading);
        AddField(layout, "Theme", _themeSelector, 2);
        AddField(layout, "Chart palette", _paletteSelector, 4);
        AddField(layout, "Historical retention", _retentionSelector, 6);
        Grid.SetRow(_minimizeToTrayCheckBox, 8);
        layout.Children.Add(_minimizeToTrayCheckBox);
        Grid.SetRow(_startupCheckBox, 10);
        layout.Children.Add(_startupCheckBox);
        var shortcutHeading = CreateHeading("Keyboard shortcut");
        Grid.SetRow(shortcutHeading, 12);
        layout.Children.Add(shortcutHeading);
        AddField(layout, "Show WinMon", _shortcutBox, 14);
        var resetButton = new Button { Content = "Default", MinWidth = 70, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        resetButton.SetResourceReference(BackgroundProperty, "Raised");
        resetButton.SetResourceReference(ForegroundProperty, "Text");
        AutomationProperties.SetName(resetButton, "Restore the default shortcut");
        resetButton.Click += (_, _) => RecordShortcut(HotkeyGesture.Default);
        var statusRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(resetButton, Dock.Right);
        statusRow.Children.Add(resetButton);
        statusRow.Children.Add(_shortcutStatus);
        Grid.SetRow(statusRow, 16);
        layout.Children.Add(statusRow);
        Grid.SetRow(_launchCheckBox, 18);
        layout.Children.Add(_launchCheckBox);
        var disksHeading = CreateHeading("Disk monitoring");
        Grid.SetRow(disksHeading, 20);
        layout.Children.Add(disksHeading);
        var diskHelp = new TextBlock { Text = "Choose up to 2 volumes.", FontSize = 12 };
        diskHelp.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Grid.SetRow(diskHelp, 22);
        layout.Children.Add(diskHelp);
        AddField(layout, "Primary drive", _primaryDriveSelector, 24);
        AddField(layout, "Second drive", _secondaryDriveSelector, 26);
        var diskNote = new TextBlock { Text = "Select None to monitor one drive.\nActive time and read/write throughput.", FontSize = 12 };
        diskNote.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Grid.SetRow(diskNote, 28);
        layout.Children.Add(diskNote);
        StackPanel buttons = CreateButtons();
        Grid.SetRow(buttons, 30);
        layout.Children.Add(buttons);

        Border surface = new() { Child = layout };
        surface.SetResourceReference(Border.BackgroundProperty, "Surface");
        Content = surface;
    }

    /// <summary>
    /// Gets the edited preferences when the dialog is accepted, or <see langword="null"/> when it is cancelled.
    /// </summary>
    public MonitorPreferences? Result { get; private set; }

    private sealed record DriveChoice(string? Name, string Label)
    {
        public override string ToString() => Label;
    }

    private static ComboBox CreateDriveSelector(string? selected, bool optional)
    {
        var choices = new List<DriveChoice>();
        if (optional) choices.Add(new DriveChoice(null, "None"));
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                var name = PreferenceStore.NormalizeDrive(drive.Name);
                if (name is null) continue;
                var label = name;
                try { if (drive.IsReady && drive.VolumeLabel.Length > 0) label += $" — {drive.VolumeLabel}"; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                choices.Add(new DriveChoice(name, label));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        if (selected is not null && choices.All(c => c.Name != selected))
            choices.Add(new DriveChoice(selected, $"{selected} — Unavailable"));
        var selector = CreateSelector([], "");
        selector.ItemsSource = choices;
        selector.SelectedItem = choices.FirstOrDefault(c => c.Name == selected);
        selector.Width = 210;
        return selector;
    }
    private static ComboBox CreateSelector(IReadOnlyList<string> choices, string selectedValue)
    {
        ComboBox selector = new()
        {
            ItemsSource = choices,
            SelectedItem = selectedValue,
            MinWidth = 170,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        selector.SetResourceReference(BackgroundProperty, "Raised");
        selector.SetResourceReference(ForegroundProperty, "Text");
        return selector;
    }

    private static CheckBox CreateCheckBox(string content, bool isChecked)
    {
        CheckBox checkBox = new()
        {
            Content = content,
            IsChecked = isChecked,
            Margin = new Thickness(0, 2, 0, 2)
        };
        checkBox.SetResourceReference(ForegroundProperty, "Text");
        return checkBox;
    }

    private TextBox CreateShortcutBox()
    {
        TextBox box = new()
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            Width = 210,
            ToolTip = "Click here, then press the combination: at least two of Ctrl, Alt and Shift, plus a key. Backspace turns the shortcut off."
        };
        box.SetResourceReference(BackgroundProperty, "Raised");
        box.SetResourceReference(ForegroundProperty, "Text");
        box.PreviewKeyDown += OnShortcutKeyDown;
        box.PreviewKeyUp += (_, _) => { if (Keyboard.Modifiers == ModifierKeys.None) box.Text = ShortcutText(_shortcut); };
        box.LostKeyboardFocus += (_, _) => box.Text = ShortcutText(_shortcut);
        return box;
    }

    private void OnShortcutKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        // Tab, Shift+Tab and Esc keep their dialog meaning.
        if (key is Key.Tab or Key.Escape && (modifiers & ~ModifierKeys.Shift) == ModifierKeys.None) return;
        e.Handled = true;
        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
        {
            _shortcut = null;
            ShowShortcut("Off. Click the box and press a combination to set one.");
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            // Echo the held modifiers until the key arrives.
            string[] held = [.. new[] { (ModifierKeys.Control, "Ctrl"), (ModifierKeys.Alt, "Alt"), (ModifierKeys.Shift, "Shift"), (ModifierKeys.Windows, "Win") }
                .Where(m => modifiers.HasFlag(m.Item1)).Select(m => m.Item2)];
            _shortcutBox.Text = held.Length == 0 ? ShortcutText(_shortcut) : string.Join(" + ", held) + " + …";
            return;
        }
        RecordShortcut(new HotkeyGesture(ToHotkeyModifiers(modifiers), KeyInterop.VirtualKeyFromKey(key)));
    }

    private void RecordShortcut(HotkeyGesture gesture)
    {
        if (gesture.Problem is { } problem) ShowShortcut($"{gesture.DisplayText} can't be used. {problem}");
        else if (gesture != _shortcut && !_isShortcutAvailable(gesture)) ShowShortcut($"Another app is using {gesture.DisplayText}. Choose another.");
        else
        {
            _shortcut = gesture;
            ShowShortcut("Applies when you save. If a combination doesn't appear, another app is using it.");
        }
    }

    private void ShowShortcut(string status)
    {
        _shortcutBox.Text = ShortcutText(_shortcut);
        _shortcutStatus.Text = status;
        _launchCheckBox.IsEnabled = _shortcut is not null;
    }

    private static string ShortcutText(HotkeyGesture? gesture) => gesture?.DisplayText ?? HotkeyGesture.NoneText;

    private static HotkeyModifiers ToHotkeyModifiers(ModifierKeys modifiers) =>
        (modifiers.HasFlag(ModifierKeys.Control) ? HotkeyModifiers.Control : 0) |
        (modifiers.HasFlag(ModifierKeys.Alt) ? HotkeyModifiers.Alt : 0) |
        (modifiers.HasFlag(ModifierKeys.Shift) ? HotkeyModifiers.Shift : 0) |
        (modifiers.HasFlag(ModifierKeys.Windows) ? HotkeyModifiers.Windows : 0);

    private static TextBlock CreateHeading(string text)
    {
        TextBlock heading = new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 16
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        return heading;
    }

    private static string RetentionLabel(int seconds) => seconds switch
    {
        >= 3600 => "60 minutes",
        >= 1800 => "30 minutes",
        _ => "10 minutes"
    };

    private static void AddField(Grid layout, string label, Control control, int row)
    {
        Grid field = new();
        field.ColumnDefinitions.Add(new ColumnDefinition());
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock text = new()
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        AutomationProperties.SetLabeledBy(control, text);
        AutomationProperties.SetName(control, label);
        field.Children.Add(text);
        Grid.SetColumn(control, 1);
        field.Children.Add(control);
        Grid.SetRow(field, row);
        layout.Children.Add(field);
    }

    private StackPanel CreateButtons()
    {
        Button cancelButton = new()
        {
            Content = "Cancel",
            IsCancel = true,
            MinWidth = 90,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Button saveButton = new()
        {
            Content = "Save",
            IsDefault = true,
            MinWidth = 90
        };
        cancelButton.SetResourceReference(BackgroundProperty, "Raised");
        Controls.ButtonIcon.SetData(cancelButton, Controls.ButtonIcon.Cancel);
        Controls.ButtonIcon.SetData(saveButton, Controls.ButtonIcon.Save);
        cancelButton.SetResourceReference(ForegroundProperty, "Text");
        saveButton.SetResourceReference(BackgroundProperty, "Raised");
        saveButton.SetResourceReference(ForegroundProperty, "Text");
        saveButton.Click += SaveButton_Click;

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancelButton, saveButton }
        };
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var primary = (_primaryDriveSelector.SelectedItem as DriveChoice)?.Name;
        var secondary = (_secondaryDriveSelector.SelectedItem as DriveChoice)?.Name;
        if (primary is null || primary == secondary)
        {
            MessageBox.Show(this, "Choose a primary volume and a different second volume, or select None.", "Disk monitoring", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Result = _initialPreferences with
        {
            PrimaryDrive = primary,
            SecondaryDrive = secondary,
            Theme = _themeSelector.SelectedItem as string ?? "Dark",
            ChartPalette = _paletteSelector.SelectedItem as string ?? "Classic",
            RetentionSeconds = (_retentionSelector.SelectedItem as string) switch
            {
                "30 minutes" => 1800,
                "60 minutes" => 3600,
                _ => 600
            },
            MinimizeToTray = _minimizeToTrayCheckBox.IsChecked == true,
            StartWithWindows = _startupCheckBox.IsChecked == true,
            ShowShortcut = _shortcut?.ToString() ?? HotkeyGesture.NoneText,
            LaunchWithShortcut = _shortcut is not null && _launchCheckBox.IsChecked == true
        };
        DialogResult = true;
    }
}
