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
    private sealed record ThemeChoice(string Name, string Label)
    {
        public override string ToString() => Label;
    }
    private readonly MonitorPreferences _initialPreferences;
    private readonly ComboBox _themeSelector;
    private readonly ComboBox _languageSelector;
    private readonly ComboBox _paletteSelector;
    private readonly ComboBox _retentionSelector;
    private readonly CheckBox _minimizeToTrayCheckBox;
    private readonly CheckBox _startupCheckBox;
    private readonly CheckBox _automaticUpdateCheckBox;
    private readonly TextBlock _updateCheckStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _primaryDriveSelector;
    private readonly ComboBox _secondaryDriveSelector;
    private readonly TextBox _shortcutBox;
    private readonly TextBlock _shortcutStatus;
    private readonly Func<HotkeyGesture, bool> _isShortcutAvailable;
    private HotkeyGesture? _shortcut;
    private readonly Func<DriveChoice[]> _loadDrives;
    private Task? _driveLoadTask;
    private bool _closed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
    /// </summary>
    /// <param name="preferences">The preferences shown when the dialog opens.</param>
    /// <param name="isShortcutAvailable">Reports whether no other application holds a recorded shortcut.</param>
    /// <param name="shortcutStatus">Describes whether the current shortcut works.</param>
    public SettingsWindow(MonitorPreferences preferences, Func<HotkeyGesture, bool>? isShortcutAvailable = null, string shortcutStatus = "", Func<Task<string>>? checkForUpdates = null)
        : this(preferences, DiscoverDrives, isShortcutAvailable, shortcutStatus, checkForUpdates)
    {
    }

    internal SettingsWindow(MonitorPreferences preferences, Func<DriveChoice[]> loadDrives, Func<HotkeyGesture, bool>? isShortcutAvailable = null, string shortcutStatus = "", Func<Task<string>>? checkForUpdates = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _loadDrives = loadDrives;
        _initialPreferences = preferences;
        _isShortcutAvailable = isShortcutAvailable ?? (_ => true);
        _shortcut = preferences.ShowGesture;
        Controls.WindowTheme.Attach(this);

        Title = Strings.WinMonSettings;
        Width = 460;
        Height = 838;
        MinWidth = 420;
        MinHeight = Math.Min(778, Math.Max(320, SystemParameters.WorkArea.Height - 32));
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 32);
        Height = Math.Min(Height, MaxHeight);
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Text");

        _themeSelector = CreateSelector([], "");
        _themeSelector.ItemsSource = new[]
        {
            new ThemeChoice("Dark", Strings.ThemeDark),
            new ThemeChoice("Light", Strings.ThemeLight),
            new ThemeChoice("System", Strings.ThemeSystem)
        };
        _themeSelector.DisplayMemberPath = nameof(ThemeChoice.Label);
        _themeSelector.SelectedValuePath = nameof(ThemeChoice.Name);
        _themeSelector.SelectedValue = preferences.Theme;
        _languageSelector = CreateSelector([], "");
        _languageSelector.ItemsSource = AppLanguages.Choices;
        _languageSelector.DisplayMemberPath = nameof(AppLanguages.Choice.Label);
        _languageSelector.SelectedValuePath = nameof(AppLanguages.Choice.Name);
        _languageSelector.SelectedValue = AppLanguages.Normalize(preferences.Language);
        _languageSelector.ToolTip = Strings.LanguageApplyNote;
        var paletteChoices = ChartPalettes.Names.Select(name => new PaletteChoice(name))
            .OrderBy(choice => choice.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        _paletteSelector = CreateSelector([], "");
        _paletteSelector.ItemsSource = paletteChoices;
        _paletteSelector.SelectedValuePath = nameof(PaletteChoice.Name);
        _paletteSelector.SelectedValue = ChartPalettes.Normalize(preferences.ChartPalette);
        TextSearch.SetTextPath(_paletteSelector, nameof(PaletteChoice.DisplayName));
        var paletteResources = new ResourceDictionary
        {
            Source = new Uri("/WinMon;component/Preferences/PaletteResources.xaml", UriKind.Relative)
        };
        _paletteSelector.ItemTemplate = paletteResources["PaletteChoiceTemplate"] as DataTemplate
            ?? throw new InvalidOperationException("Palette choice template was not found.");
        UpdatePalettePreviews();
        _themeSelector.SelectionChanged += (_, _) => UpdatePalettePreviews();
        _paletteSelector.ToolTip = Strings.ResourceColorsCPUMemoryPrimaryDiskNetworkGPUSecondDisk;
        _retentionSelector = CreateSelector([Strings.Value10Minutes, Strings.Value30Minutes, Strings.Value60Minutes], RetentionLabel(preferences.RetentionSeconds));
        _minimizeToTrayCheckBox = CreateCheckBox(Strings.MinimizeToTheNotificationArea, preferences.MinimizeToTray);
        _startupCheckBox = CreateCheckBox(Strings.StartWinMonWhenISignIn, preferences.StartWithWindows);
        _automaticUpdateCheckBox = CreateCheckBox(Strings.AutomaticallyCheckForUpdates, preferences.AutomaticallyCheckForUpdates);
        _shortcutBox = CreateShortcutBox();
        _shortcutStatus = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        _shortcutStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        AutomationProperties.SetLiveSetting(_shortcutStatus, AutomationLiveSetting.Polite);
        ShowShortcut(shortcutStatus);

        _primaryDriveSelector = CreateDriveSelector(preferences.PrimaryDrive, false);
        _secondaryDriveSelector = CreateDriveSelector(preferences.SecondaryDrive, true);
        ContentRendered += async (_, _) => await LoadDrivesAsync();
        Closed += (_, _) => _closed = true;

        Grid layout = new()
        {
            Margin = new Thickness(24)
        };
        for (var row = 0; row <= 40; row++)
            layout.RowDefinitions.Add(new RowDefinition { Height = row is 22 or 23 ? new GridLength(0) : row % 2 == 0 ? GridLength.Auto : new GridLength(row is 11 or 15 or 21 or 33 ? 16 : 10) });
        TextBlock heading = CreateHeading(Strings.Appearance);
        Grid.SetRow(heading, 0);
        layout.Children.Add(heading);
        AddField(layout, Strings.Language, _languageSelector, 2);
        var languageNote = new TextBlock { Text = Strings.LanguageApplyNote, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        languageNote.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Grid.SetRow(languageNote, 4);
        layout.Children.Add(languageNote);
        AddField(layout, Strings.Theme, _themeSelector, 6);
        AddField(layout, Strings.ChartPalette, _paletteSelector, 8);
        AddField(layout, Strings.HistoricalRetention, _retentionSelector, 10);
        Grid.SetRow(_minimizeToTrayCheckBox, 12);
        layout.Children.Add(_minimizeToTrayCheckBox);
        Grid.SetRow(_startupCheckBox, 14);
        layout.Children.Add(_startupCheckBox);
        var shortcutHeading = CreateHeading(Strings.KeyboardShortcut);
        Grid.SetRow(shortcutHeading, 16);
        layout.Children.Add(shortcutHeading);
        AddField(layout, Strings.ShowHideWinMon, _shortcutBox, 18);
        var resetButton = new Button { Content = Strings.Default, MinWidth = 70, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        resetButton.SetResourceReference(BackgroundProperty, "Raised");
        resetButton.SetResourceReference(ForegroundProperty, "Text");
        AutomationProperties.SetName(resetButton, Strings.RestoreTheDefaultShortcut);
        resetButton.Click += (_, _) => RecordShortcut(HotkeyGesture.Default);
        var statusRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(resetButton, Dock.Right);
        statusRow.Children.Add(resetButton);
        statusRow.Children.Add(_shortcutStatus);
        Grid.SetRow(statusRow, 20);
        layout.Children.Add(statusRow);
        var disksHeading = CreateHeading(Strings.DiskMonitoring);
        Grid.SetRow(disksHeading, 24);
        layout.Children.Add(disksHeading);
        var diskHelp = new TextBlock { Text = Strings.ChooseUpTo2Volumes, FontSize = 12 };
        diskHelp.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Grid.SetRow(diskHelp, 26);
        layout.Children.Add(diskHelp);
        AddField(layout, Strings.PrimaryDrive, _primaryDriveSelector, 28);
        AddField(layout, Strings.SecondDrive, _secondaryDriveSelector, 30);
        var diskNote = new TextBlock { Text = Strings.SelectNoneToMonitorOneDriveActiveTimeAndRead, FontSize = 12 };
        diskNote.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Grid.SetRow(diskNote, 32);
        layout.Children.Add(diskNote);
        var updateHeading = CreateHeading(Strings.Updates);
        Grid.SetRow(updateHeading, 34);
        layout.Children.Add(updateHeading);
        Grid.SetRow(_automaticUpdateCheckBox, 36);
        layout.Children.Add(_automaticUpdateCheckBox);
        _updateCheckStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        AutomationProperties.SetLiveSetting(_updateCheckStatus, AutomationLiveSetting.Polite);
        Grid.SetRow(_updateCheckStatus, 38);
        layout.Children.Add(_updateCheckStatus);
        StackPanel buttons = CreateButtons(checkForUpdates);
        Grid.SetRow(buttons, 40);
        layout.Children.Add(buttons);

        Border surface = new() { Child = layout };
        surface.SetResourceReference(Border.BackgroundProperty, "Surface");
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = surface };
        return;

        void UpdatePalettePreviews()
        {
            var theme = _themeSelector.SelectedValue as string;
            var light = theme == "Light" || (theme == "System" && MainWindow.IsSystemLight());
            foreach (var choice in paletteChoices) choice.UpdateTheme(light);
        }
    }

    /// <summary>
    /// Gets the edited preferences when the dialog is accepted, or <see langword="null"/> when it is cancelled.
    /// </summary>
    public MonitorPreferences? Result { get; private set; }

    internal sealed record DriveChoice(string? Name, string Label)
    {
        public override string ToString() => Label;
    }

    private static ComboBox CreateDriveSelector(string? selected, bool optional)
    {
        var selector = CreateSelector([], "");
        SetDriveChoices(selector, [], selected, optional, loading: true);
        selector.Width = 210;
        return selector;
    }

    // Start after the dialog's first render; neither construction nor painting waits for a drive.
    internal Task LoadDrivesAsync() => _driveLoadTask ??= LoadDrivesCoreAsync();

    private async Task LoadDrivesCoreAsync()
    {
        try
        {
            var drives = await Task.Run(_loadDrives);
            if (_closed) return;
            // Read the current selections, since the user may have changed them while loading.
            var primary = (_primaryDriveSelector.SelectedItem as DriveChoice)?.Name;
            var secondary = (_secondaryDriveSelector.SelectedItem as DriveChoice)?.Name;
            SetDriveChoices(_primaryDriveSelector, drives, primary, false);
            SetDriveChoices(_secondaryDriveSelector, drives, secondary, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("WinMon drive discovery: {0}", ex);
        }
    }

    private static DriveChoice[] DiscoverDrives()
    {
        var choices = new List<DriveChoice>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                var name = PreferenceStore.NormalizeDrive(drive.Name);
                if (name is null) continue;
                var label = name;
                try
                {
                    if (drive.IsReady && drive.VolumeLabel is { Length: > 0 } volumeLabel)
                        label += $" — {volumeLabel}";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                choices.Add(new DriveChoice(name, label));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return choices.ToArray();
    }

    private static void SetDriveChoices(ComboBox selector, IReadOnlyList<DriveChoice> drives, string? selected, bool optional, bool loading = false)
    {
        var choices = new List<DriveChoice>();
        if (optional) choices.Add(new DriveChoice(null, Strings.None));
        choices.AddRange(drives);
        if (selected is not null && choices.All(c => c.Name != selected))
            choices.Add(new DriveChoice(selected, loading ? selected : Strings.Format(Strings.UnavailableDrive, selected)));
        selector.ItemsSource = choices;
        selector.SelectedItem = choices.FirstOrDefault(c => c.Name == selected);
    }
    private static ComboBox CreateSelector(IReadOnlyList<string> choices, string selectedValue)
    {
        ComboBox selector = new()
        {
            ItemsSource = choices,
            SelectedItem = selectedValue,
            Width = 210,
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
            ToolTip = Strings.ClickHereThenPressTheCombinationAtLeastTwoOf
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
            ShowShortcut(Strings.OffClickTheBoxAndPressACombinationToSet);
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            // Echo the held modifiers until the key arrives.
            string[] held = [.. new[] { (ModifierKeys.Control, Strings.Ctrl), (ModifierKeys.Alt, Strings.Alt), (ModifierKeys.Shift, Strings.Shift), (ModifierKeys.Windows, Strings.Win) }
                .Where(m => modifiers.HasFlag(m.Item1)).Select(m => m.Item2)];
            _shortcutBox.Text = held.Length == 0 ? ShortcutText(_shortcut) : string.Join(" + ", held) + " + …";
            return;
        }
        RecordShortcut(new HotkeyGesture(ToHotkeyModifiers(modifiers), KeyInterop.VirtualKeyFromKey(key)));
    }

    private void RecordShortcut(HotkeyGesture gesture)
    {
        if (gesture.Problem is { } problem) ShowShortcut(Strings.Format(Strings.CanTBeUsed, gesture.DisplayText, problem));
        else if (gesture != _shortcut && !_isShortcutAvailable(gesture)) ShowShortcut(Strings.Format(Strings.AnotherAppIsUsingChooseAnother, gesture.DisplayText));
        else
        {
            _shortcut = gesture;
            ShowShortcut(Strings.AppliesWhenYouSaveIfACombinationDoesnTAppear);
        }
    }

    private void ShowShortcut(string status)
    {
        _shortcutBox.Text = ShortcutText(_shortcut);
        _shortcutStatus.Text = status;
    }

    private static string ShortcutText(HotkeyGesture? gesture) => gesture?.DisplayText ?? Strings.None;

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
        >= 3600 => Strings.Value60Minutes,
        >= 1800 => Strings.Value30Minutes,
        _ => Strings.Value10Minutes
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

    private StackPanel CreateButtons(Func<Task<string>>? checkForUpdates)
    {
        Button cancelButton = new()
        {
            Content = Strings.Cancel,
            IsCancel = true,
            MinWidth = 90,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Button saveButton = new()
        {
            Content = Strings.Save,
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

        Button? checkButton = null;
        if (checkForUpdates is not null)
        {
            var button = new Button { Content = Strings.CheckForUpdates, MinWidth = 130, Margin = new Thickness(0, 0, 8, 0) };
            checkButton = button;
            button.SetResourceReference(BackgroundProperty, "Raised");
            button.SetResourceReference(ForegroundProperty, "Text");
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                _updateCheckStatus.Text = Strings.UpdateChecking;
                try { _updateCheckStatus.Text = await checkForUpdates(); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("WinMon manual update check: {0}", ex.Message); _updateCheckStatus.Text = Strings.UpdateFailed; }
                finally { button.IsEnabled = true; }
            };
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        if (checkButton is not null) buttons.Children.Add(checkButton);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);
        return buttons;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var primary = (_primaryDriveSelector.SelectedItem as DriveChoice)?.Name;
        var secondary = (_secondaryDriveSelector.SelectedItem as DriveChoice)?.Name;
        if (primary is null || primary == secondary)
        {
            MessageBox.Show(this, Strings.ChooseAPrimaryVolumeAndADifferentSecondVolumeOr, Strings.DiskMonitoring, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Result = _initialPreferences with
        {
            PrimaryDrive = primary,
            SecondaryDrive = secondary,
            Theme = (_themeSelector.SelectedItem as ThemeChoice)?.Name ?? "Dark",
            Language = _languageSelector.SelectedValue as string ?? AppLanguages.SystemDefault,
            ChartPalette = _paletteSelector.SelectedValue as string ?? "Classic",
            RetentionSeconds = (_retentionSelector.SelectedItem as string) switch
            {
                var label when label == Strings.Value30Minutes => 1800,
                var label when label == Strings.Value60Minutes => 3600,
                _ => 600
            },
            MinimizeToTray = _minimizeToTrayCheckBox.IsChecked == true,
            StartWithWindows = _startupCheckBox.IsChecked == true,
            AutomaticallyCheckForUpdates = _automaticUpdateCheckBox.IsChecked == true,
            ShowShortcut = _shortcut?.ToString() ?? HotkeyGesture.NoneText
        };
        DialogResult = true;
    }
}
