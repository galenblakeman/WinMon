using System.Diagnostics;
using System.Security;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WinMon.Controls;
using WinMon.Shortcuts;
using WinMon.Updates;
using Path = System.Windows.Shapes.Path;

namespace WinMon.Preferences;

/// <summary>A page of the Settings window, in sidebar order.</summary>
internal enum SettingsSection
{
    General,
    Appearance,
    Monitoring,
    AutomaticCaptures,
    Updates
}

/// <summary>
/// Provides controls for editing monitor preferences before they are persisted, one sidebar section at a time.
/// </summary>
public sealed class SettingsWindow : Window
{
    private sealed record ThemeChoice(string Name, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    private sealed record GaugeChoice(GaugeStyle Style, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>Hosts the appearance illustration; assistive technology skips its sample controls.</summary>
    private sealed class PreviewHost : Border
    {
        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new Peer(this);
        }

        private sealed class Peer(PreviewHost owner) : FrameworkElementAutomationPeer(owner)
        {
            protected override List<AutomationPeer>? GetChildrenCore()
            {
                return null;
            }

            protected override bool IsControlElementCore()
            {
                return false;
            }

            protected override bool IsContentElementCore()
            {
                return false;
            }
        }
    }

    private readonly MonitorPreferences _initialPreferences;
    private readonly ComboBox _themeSelector;
    private readonly ComboBox _paletteSelector;
    private readonly ComboBox _gaugeSelector;
    private readonly AppearancePreview _appearancePreview;
    private readonly ComboBox _retentionSelector;
    private readonly CheckBox _minimizeToTrayCheckBox;
    private readonly CheckBox _startupCheckBox;
    private readonly CheckBox _automaticUpdateCheckBox;
    private readonly TextBlock _updateCheckStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _shortcutBox;
    private readonly TextBlock _shortcutStatus;
    private readonly Func<HotkeyGesture, bool> _isShortcutAvailable;
    private HotkeyGesture? _shortcut;
    private readonly Func<DriveChoice[]> _loadDrives;
    private readonly string _defaultCaptureFolder;
    private Task? _driveLoadTask;
    private bool _closed;

    private readonly ListBox _navigation = new()
        { BorderThickness = new Thickness(0), Background = Brushes.Transparent };

    private readonly ScrollViewer _page = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false
    };

    private readonly List<(SettingsSection Section, string Label, Geometry Icon, FrameworkElement Page)> _pages = [];

    private readonly TextBlock _captureState =
        new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

    private readonly TextBlock _lastChecked = new();
    private Button? _saveButton;

    private static readonly Geometry Monitoring = Geometry.Parse("M 1,9 L 4,9 L 6,3 L 9,13 L 11,7 L 15,7");
    private static readonly Geometry AppearanceIcon =
        Geometry.Parse("M 2,2 L 14,2 L 14,12 L 2,12 Z M 8,12 L 8,15 M 5,15 L 11,15 M 4,8 L 7,5 L 9,8 L 12,5");
    private static readonly Geometry Capture = Geometry.Parse("M 2,3 L 14,3 L 8,13 Z");

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
    /// </summary>
    /// <param name="preferences">The preferences shown when the dialog opens.</param>
    /// <param name="isShortcutAvailable">Reports whether no other application holds a recorded shortcut.</param>
    /// <param name="shortcutStatus">Describes whether the current shortcut works.</param>
    /// <param name="checkForUpdates">Checks for an update and describes the result.</param>
    /// <param name="captureFolder">The default automatic capture folder; the installed default when omitted.</param>
    /// <param name="lastUpdateCheck">When this session last finished an update check, if it has.</param>
    public SettingsWindow(MonitorPreferences preferences, Func<HotkeyGesture, bool>? isShortcutAvailable = null,
        string shortcutStatus = "", Func<Task<string>>? checkForUpdates = null, string? captureFolder = null,
        DateTimeOffset? lastUpdateCheck = null)
        : this(preferences, DiscoverDrives, isShortcutAvailable, shortcutStatus, checkForUpdates, captureFolder,
            lastUpdateCheck)
    {
    }

    internal SettingsWindow(MonitorPreferences preferences, Func<DriveChoice[]> loadDrives,
        Func<HotkeyGesture, bool>? isShortcutAvailable = null, string shortcutStatus = "",
        Func<Task<string>>? checkForUpdates = null, string? captureFolder = null,
        DateTimeOffset? lastUpdateCheck = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _loadDrives = loadDrives;
        _initialPreferences = preferences;
        _isShortcutAvailable = isShortcutAvailable ?? (_ => true);
        _shortcut = preferences.ShowGesture;
        WindowTheme.Attach(this);

        Title = Strings.WinMonSettings;
        // Every section fits this size; a section that outgrows it scrolls on its own.
        Width = 780;
        Height = Math.Min(600, Math.Max(320, SystemParameters.WorkArea.Height - 32));
        MinWidth = 640;
        MinHeight = 320;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Text");

        _themeSelector = CreateSelector([], "");
        ThemeChoice[] standardThemes =
        {
            new ThemeChoice("System", Strings.ThemeSystem),
            new ThemeChoice("Light", Strings.ThemeLight),
            new ThemeChoice("Dark", Strings.ThemeDark)
        };
        ThemeChoice[] customThemes =
        {
            new ThemeChoice("Candy", Strings.ThemeCandy),
            new ThemeChoice("Copper", Strings.ThemeCopper),
            new ThemeChoice("Crystal", Strings.ThemeCrystal),
            new ThemeChoice("EvergreenInstrument", Strings.ThemeEvergreenInstrument),
            new ThemeChoice("FrostedPorcelain", Strings.ThemeFrostedPorcelain),
            new ThemeChoice("Gunmetal", Strings.ThemeGunmetal),
            new ThemeChoice("MidnightInk", Strings.ThemeMidnightInk),
            new ThemeChoice("Porcelain", Strings.ThemePorcelain),
            new ThemeChoice("Prism", Strings.ThemePrism),
            new ThemeChoice("SmokedSapphire", Strings.ThemeSmokedSapphire),
            new ThemeChoice("Slate", Strings.ThemeSlate),
            new ThemeChoice("Wildberry", Strings.ThemeWildberry)
        };
        _themeSelector.ItemsSource = standardThemes.Concat(customThemes
            .OrderBy(choice => choice.Label, StringComparer.CurrentCultureIgnoreCase)).ToArray();
        _themeSelector.DisplayMemberPath = nameof(ThemeChoice.Label);
        _themeSelector.SelectedValuePath = nameof(ThemeChoice.Name);
        _themeSelector.SelectedValue = preferences.Theme;
        LanguageSelector = CreateSelector([], "");
        LanguageSelector.ItemsSource = AppLanguages.Choices;
        LanguageSelector.DisplayMemberPath = nameof(AppLanguages.Choice.Label);
        LanguageSelector.SelectedValuePath = nameof(AppLanguages.Choice.Name);
        LanguageSelector.SelectedValue = AppLanguages.Normalize(preferences.Language);
        LanguageSelector.ToolTip = Strings.LanguageApplyNote;
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
                                        ?? throw new InvalidOperationException(
                                            "Palette choice template was not found.");
        UpdatePalettePreviews();
        _themeSelector.SelectionChanged += (_, _) =>
        {
            UpdatePalettePreviews();
            UpdateAppearancePreview();
        };
        _paletteSelector.ToolTip = Strings.ResourceColorsCPUMemoryPrimaryDiskNetworkGPUSecondDisk;
        _gaugeSelector = CreateSelector([], "");
        _gaugeSelector.ItemsSource = new[]
        {
            new GaugeChoice(GaugeStyle.Bowl, Strings.GaugeBowl),
            new GaugeChoice(GaugeStyle.Needle, Strings.GaugeNeedle),
            new GaugeChoice(GaugeStyle.SegmentedArc, Strings.GaugeSegmentedArc),
            new GaugeChoice(GaugeStyle.LeftBar, Strings.GaugeLeftBar),
            new GaugeChoice(GaugeStyle.BottomBar, Strings.GaugeBottomBar),
            new GaugeChoice(GaugeStyle.SegmentedBar, Strings.GaugeSegmentedBar),
            new GaugeChoice(GaugeStyle.None, Strings.None)
        };
        _gaugeSelector.DisplayMemberPath = nameof(GaugeChoice.Label);
        _gaugeSelector.SelectedValuePath = nameof(GaugeChoice.Style);
        _gaugeSelector.SelectedValue = preferences.GaugeStyle;
        _gaugeSelector.ToolTip = Strings.PeakLastTwoMinutesExplanation;
        _appearancePreview = new AppearancePreview();
        UpdateAppearancePreview();
        _paletteSelector.SelectionChanged += (_, _) => UpdateAppearancePreview();
        _gaugeSelector.SelectionChanged += (_, _) => UpdateAppearancePreview();
        _retentionSelector = CreateSelector([Strings.Value10Minutes, Strings.Value30Minutes, Strings.Value60Minutes],
            RetentionLabel(preferences.RetentionSeconds));
        _minimizeToTrayCheckBox = CreateCheckBox(Strings.MinimizeToTheNotificationArea, preferences.MinimizeToTray);
        _startupCheckBox = CreateCheckBox(Strings.StartWinMonWhenISignIn, preferences.StartWithWindows);
        _automaticUpdateCheckBox =
            CreateCheckBox(Strings.AutomaticallyCheckForUpdates, preferences.AutomaticallyCheckForUpdates);
        _shortcutBox = CreateShortcutBox();
        _shortcutStatus = new TextBlock
            { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        _shortcutStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        AutomationProperties.SetLiveSetting(_shortcutStatus, AutomationLiveSetting.Polite);
        ShowShortcut(shortcutStatus);

        PrimaryDriveSelector = CreateDriveSelector(preferences.PrimaryDrive, false);
        SecondaryDriveSelector = CreateDriveSelector(preferences.SecondaryDrive, true);
        _defaultCaptureFolder = captureFolder ?? new PreferenceStore().DefaultCaptureFolder;
        AutoCapture = new AutoCaptureSection(preferences.AutoCapture, _defaultCaptureFolder, preferences.PrimaryDrive,
            preferences.SecondaryDrive);
        ContentRendered += async (_, _) => await LoadDrivesAsync();
        Closed += (_, _) => _closed = true;

        PrimaryDriveSelector.SelectionChanged += (_, _) => FollowDrives();
        SecondaryDriveSelector.SelectionChanged += (_, _) => FollowDrives();

        _pages.Add((SettingsSection.General, Strings.SettingsGeneral, ButtonIcon.Settings, CreateGeneralPage()));
        _pages.Add((SettingsSection.Appearance, Strings.Appearance, AppearanceIcon, CreateAppearancePage()));
        _pages.Add((SettingsSection.Monitoring, Strings.SettingsMonitoring, Monitoring, CreateMonitoringPage()));
        _pages.Add((SettingsSection.AutomaticCaptures, Strings.AutoCaptureHeading, Capture, CreateCapturePage()));
        // Store builds update through the Store, so they have no Updates section.
        if (UpdatePolicy.GitHubUpdatesEnabled)
            _pages.Add((SettingsSection.Updates, Strings.Updates, ButtonIcon.Live,
                CreateUpdatesPage(checkForUpdates, lastUpdateCheck)));
        foreach (var page in _pages) _navigation.Items.Add(CreateNavItem(page.Section, page.Label, page.Icon));
        _navigation.SelectionChanged += (_, _) =>
        {
            if (_navigation.SelectedIndex < 0) return;
            _page.Content = _pages[_navigation.SelectedIndex].Page;
            _page.ScrollToTop();
        };
        _navigation.SelectedIndex = 0;
        AutomationProperties.SetName(_navigation, Strings.SettingsSections);
        // Labels trim instead of scrolling sideways.
        ScrollViewer.SetHorizontalScrollBarVisibility(_navigation, ScrollBarVisibility.Disabled);
        PreviewKeyDown += OnSectionKeyDown;

        var navigation = new Border
        {
            Child = _navigation, Width = 216, Padding = new Thickness(8, 12, 8, 12),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        navigation.SetResourceReference(Border.BackgroundProperty, "SectionHeaderSurface");
        navigation.SetResourceReference(Border.BorderBrushProperty, "Line");
        var footer = new Border
        {
            Child = CreateButtons(), Padding = new Thickness(20, 12, 20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };
        footer.SetResourceReference(Border.BorderBrushProperty, "Line");
        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(navigation, Dock.Left);
        layout.Children.Add(footer);
        layout.Children.Add(navigation);
        layout.Children.Add(_page);
        Border surface = new() { Child = layout, BorderThickness = new Thickness(0, 1, 0, 0) };
        surface.SetResourceReference(Border.BackgroundProperty, "Surface");
        surface.SetResourceReference(Border.BorderBrushProperty, "Line");
        Content = surface;
        return;

        void UpdateAppearancePreview()
        {
            if (_appearancePreview is not { } preview) return;
            preview.Update(_themeSelector.SelectedValue as string ?? "Dark",
                _paletteSelector.SelectedValue as string ?? "Classic", SelectedGaugeStyle);
        }

        void UpdatePalettePreviews()
        {
            var theme = _themeSelector.SelectedValue as string;
            var light = theme == "Light" || (theme == "System" && MainWindow.IsSystemLight()) ||
                        (ThemeFinishes.IsFinish(theme) && ThemeFinishes.For(theme!).Light);
            foreach (var choice in paletteChoices) choice.UpdateTheme(light);
        }
    }

    /// <summary>
    /// Gets the edited preferences when the dialog is accepted, or <see langword="null"/> when it is cancelled.
    /// </summary>
    public MonitorPreferences? Result { get; private set; }

    internal ComboBox GaugeSelector => _gaugeSelector;

    internal ComboBox ThemeSelector => _themeSelector;

    internal ComboBox PaletteSelector => _paletteSelector;

    internal MetricReadout GaugePreview => _appearancePreview.GaugePreview;

    internal AppearancePreview CombinedPreview => _appearancePreview;

    private GaugeStyle SelectedGaugeStyle => _gaugeSelector.SelectedValue as GaugeStyle? ?? GaugeStyle.Bowl;

    /// <summary>Copy the preferences this dialog edits from <paramref name="settings"/> onto <paramref name="current"/>.</summary>
    /// <remarks>
    /// Everything else, such as grouping, pins and compact mode, belongs to the main window and may change while
    /// a confirmed result is being saved, so applying a result must not restore the values the dialog opened with.
    /// </remarks>
    internal static MonitorPreferences ApplySettings(MonitorPreferences current, MonitorPreferences settings)
    {
        return current with
        {
            PrimaryDrive = settings.PrimaryDrive,
            SecondaryDrive = settings.SecondaryDrive,
            Theme = settings.Theme,
            Language = settings.Language,
            ChartPalette = settings.ChartPalette,
            GaugeStyle = settings.GaugeStyle,
            RetentionSeconds = settings.RetentionSeconds,
            MinimizeToTray = settings.MinimizeToTray,
            StartWithWindows = settings.StartWithWindows,
            AutomaticallyCheckForUpdates = settings.AutomaticallyCheckForUpdates,
            ShowShortcut = settings.ShowShortcut,
            AutoCapture = settings.AutoCapture
        };
    }

    internal sealed record DriveChoice(string? Name, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    private static ComboBox CreateDriveSelector(string? selected, bool optional)
    {
        var selector = CreateSelector([], "");
        SetDriveChoices(selector, [], selected, optional, true);
        selector.Width = 210;
        return selector;
    }

    // Start after the dialog's first render; neither construction nor painting waits for a drive.
    internal Task LoadDrivesAsync()
    {
        return _driveLoadTask ??= LoadDrivesCoreAsync();
    }

    private async Task LoadDrivesCoreAsync()
    {
        try
        {
            var drives = await Task.Run(_loadDrives);
            if (_closed) return;
            // Read the current selections, since the user may have changed them while loading.
            var primary = (PrimaryDriveSelector.SelectedItem as DriveChoice)?.Name;
            var secondary = (SecondaryDriveSelector.SelectedItem as DriveChoice)?.Name;
            SetDriveChoices(PrimaryDriveSelector, drives, primary, false);
            SetDriveChoices(SecondaryDriveSelector, drives, secondary, true);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("WinMon drive discovery: {0}", ex);
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
                    if (drive is { IsReady: true, VolumeLabel: { Length: > 0 } volumeLabel })
                        label += $" — {volumeLabel}";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                {
                }

                choices.Add(new DriveChoice(name, label));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
        }

        return [.. choices];
    }

    private static void SetDriveChoices(ComboBox selector, IReadOnlyList<DriveChoice> drives, string? selected,
        bool optional, bool loading = false)
    {
        var choices = new List<DriveChoice>();
        if (optional) choices.Add(new DriveChoice(null, Strings.None));
        choices.AddRange(drives);
        if (selected is not null && choices.All(c => c.Name != selected))
            choices.Add(new DriveChoice(selected,
                loading ? selected : Strings.Format(Strings.UnavailableDrive, selected)));
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
        box.PreviewKeyUp += (_, _) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.None) box.Text = ShortcutText(_shortcut);
        };
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

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            // Echo the held modifiers until the key arrives.
            string[] held =
            [
                .. new[]
                    {
                        (ModifierKeys.Control, Strings.Ctrl), (ModifierKeys.Alt, Strings.Alt),
                        (ModifierKeys.Shift, Strings.Shift), (ModifierKeys.Windows, Strings.Win)
                    }
                    .Where(m => modifiers.HasFlag(m.Item1)).Select(m => m.Item2)
            ];
            _shortcutBox.Text = held.Length == 0 ? ShortcutText(_shortcut) : string.Join(" + ", held) + " + …";
            return;
        }

        RecordShortcut(new HotkeyGesture(ToHotkeyModifiers(modifiers), KeyInterop.VirtualKeyFromKey(key)));
    }

    private void RecordShortcut(HotkeyGesture gesture)
    {
        if (gesture.Problem is { } problem)
        {
            ShowShortcut(Strings.Format(Strings.CanTBeUsed, gesture.DisplayText, problem));
        }
        else if (gesture != _shortcut && !_isShortcutAvailable(gesture))
        {
            ShowShortcut(Strings.Format(Strings.AnotherAppIsUsingChooseAnother, gesture.DisplayText));
        }
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

    private static string ShortcutText(HotkeyGesture? gesture)
    {
        return gesture?.DisplayText ?? Strings.None;
    }

    private static HotkeyModifiers ToHotkeyModifiers(ModifierKeys modifiers)
    {
        return (modifiers.HasFlag(ModifierKeys.Control) ? HotkeyModifiers.Control : 0) |
               (modifiers.HasFlag(ModifierKeys.Alt) ? HotkeyModifiers.Alt : 0) |
               (modifiers.HasFlag(ModifierKeys.Shift) ? HotkeyModifiers.Shift : 0) |
               (modifiers.HasFlag(ModifierKeys.Windows) ? HotkeyModifiers.Windows : 0);
    }

    /// <summary>The section on screen.</summary>
    internal SettingsSection Section => _pages[Math.Max(0, _navigation.SelectedIndex)].Section;

    /// <summary>Show a section, as choosing it in the sidebar does.</summary>
    internal void ShowSection(SettingsSection section)
    {
        var index = _pages.FindIndex(page => page.Section == section);
        if (index >= 0) _navigation.SelectedIndex = index;
    }

    internal ComboBox LanguageSelector { get; }

    internal ComboBox PrimaryDriveSelector { get; }

    internal ComboBox SecondaryDriveSelector { get; }

    internal Button SaveButton => _saveButton!;
    internal AutoCaptureSection AutoCapture { get; }

    // Ctrl+Tab and Ctrl+Shift+Tab step through the sections, except while the shortcut box records a combination.
    private void OnSectionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
            _shortcutBox.IsKeyboardFocusWithin) return;
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
        _navigation.SelectedIndex = (_navigation.SelectedIndex + step + _pages.Count) % _pages.Count;
        (_navigation.ItemContainerGenerator.ContainerFromIndex(_navigation.SelectedIndex) as ListBoxItem)?.Focus();
        e.Handled = true;
    }

    private void FollowDrives()
    {
        AutoCapture.UpdateDrives((PrimaryDriveSelector.SelectedItem as DriveChoice)?.Name,
            (SecondaryDriveSelector.SelectedItem as DriveChoice)?.Name);
    }

    private ListBoxItem CreateNavItem(SettingsSection section, string label, Geometry icon)
    {
        var path = new Path
        {
            Data = icon, Width = 16, Height = 16, StrokeThickness = 1.5, Stretch = Stretch.None,
            Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center
        };
        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(path, Dock.Left);
        content.Children.Add(path);
        var item = new ListBoxItem { Content = content };
        path.SetBinding(Shape.StrokeProperty, new Binding(nameof(Control.Foreground)) { Source = item });
        if (section == SettingsSection.AutomaticCaptures)
        {
            DockPanel.SetDock(_captureState, Dock.Right);
            content.Children.Add(_captureState);
            AutoCapture.EnabledChanged += ShowCaptureState;
            item.Selected += (_, _) => ShowCaptureState(AutoCapture.IsOn);
            item.Unselected += (_, _) => ShowCaptureState(AutoCapture.IsOn);
        }

        var labelBlock = new TextBlock
        {
            Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        labelBlock.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground)) { Source = item });
        content.Children.Add(labelBlock);
        item.SetResourceReference(StyleProperty, "SettingsNavItem");
        AutomationProperties.SetName(item, label);
        if (section == SettingsSection.AutomaticCaptures) ShowCaptureState(AutoCapture.IsOn);
        return item;

        void ShowCaptureState(bool on)
        {
            _captureState.Text = on ? Strings.SettingsOn : Strings.SettingsOff;
            _captureState.SetResourceReference(TextBlock.ForegroundProperty,
                item.IsSelected ? "SelectedText" : on ? "Accent" : "Muted");
            AutomationProperties.SetName(item, $"{label}, {_captureState.Text}");
        }

    }

    private StackPanel CreateGeneralPage()
    {
        var page = Page(Strings.SettingsGeneral);
        page.Children.Add(Field(Strings.Language, LanguageSelector));
        page.Children.Add(Note(Strings.LanguageApplyNote));
        page.Children.Add(CreateHeading(Strings.SettingsStartupAndTray));
        if (StartupRegistration.IsSupported) page.Children.Add(_startupCheckBox);
        page.Children.Add(_minimizeToTrayCheckBox);
        page.Children.Add(CreateHeading(Strings.KeyboardShortcut));
        page.Children.Add(Field(Strings.ShowHideWinMon, _shortcutBox));
        var resetButton = new Button
        {
            Content = Strings.Default, MinWidth = 70, Margin = new Thickness(12, 4, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        resetButton.SetResourceReference(BackgroundProperty, "Raised");
        resetButton.SetResourceReference(ForegroundProperty, "Text");
        AutomationProperties.SetName(resetButton, Strings.RestoreTheDefaultShortcut);
        resetButton.Click += (_, _) => RecordShortcut(HotkeyGesture.Default);
        var statusRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(resetButton, Dock.Right);
        statusRow.Children.Add(resetButton);
        statusRow.Children.Add(_shortcutStatus);
        page.Children.Add(statusRow);
        return page;
    }

    private StackPanel CreateAppearancePage()
    {
        var page = Page(Strings.Appearance);
        page.Children.Add(Field(Strings.Theme, _themeSelector));
        page.Children.Add(Field(Strings.ChartPalette, _paletteSelector));
        page.Children.Add(Field(Strings.ResourceGauges, _gaugeSelector));
        page.Children.Add(new PreviewHost
        {
            Child = _appearancePreview, Margin = new Thickness(0, 8, 0, 4)
        });
        return page;
    }

    private StackPanel CreateMonitoringPage()
    {
        var page = Page(Strings.SettingsMonitoring);
        page.Children.Add(CreateHeading(Strings.SettingsHistory, true));
        page.Children.Add(Field(Strings.HistoricalRetention, _retentionSelector));
        page.Children.Add(Note(Strings.SettingsRetentionNote));
        page.Children.Add(CreateHeading(Strings.DiskMonitoring));
        page.Children.Add(Note(Strings.ChooseUpTo2Volumes));
        page.Children.Add(Field(Strings.PrimaryDrive, PrimaryDriveSelector));
        page.Children.Add(Field(Strings.SecondDrive, SecondaryDriveSelector));
        page.Children.Add(Note(Strings.SelectNoneToMonitorOneDriveActiveTimeAndRead));
        return page;
    }

    private StackPanel CreateCapturePage()
    {
        var page = Page(Strings.AutoCaptureHeading, AutoCapture.EnabledSwitch);
        page.Children.Add(AutoCapture);
        return page;
    }

    private StackPanel CreateUpdatesPage(Func<Task<string>>? checkForUpdates, DateTimeOffset? lastUpdateCheck)
    {
        var page = Page(Strings.Updates);
        var facts = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        facts.ColumnDefinitions.Add(new ColumnDefinition());
        facts.RowDefinitions.Add(new RowDefinition());
        facts.RowDefinitions.Add(new RowDefinition());
        ShowLastChecked(lastUpdateCheck);
        foreach (var (row, label, value) in new[]
                 {
                     (0, Strings.SettingsInstalledVersion, new TextBlock { Text = AppInfo.Version }),
                     (1, Strings.SettingsLastChecked, _lastChecked)
                 })
        {
            var name = new TextBlock { Text = label, Margin = new Thickness(0, 3, 12, 3) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            value.Margin = new Thickness(0, 3, 0, 3);
            AutomationProperties.SetLabeledBy(value, name);
            Grid.SetRow(name, row);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            facts.Children.Add(name);
            facts.Children.Add(value);
        }

        page.Children.Add(facts);
        _updateCheckStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _updateCheckStatus.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetLiveSetting(_updateCheckStatus, AutomationLiveSetting.Polite);
        var check = new DockPanel { LastChildFill = true };
        if (checkForUpdates is not null)
        {
            var button = new Button
                { Content = Strings.CheckForUpdates, MinWidth = 130, Margin = new Thickness(0, 0, 12, 0) };
            button.SetResourceReference(BackgroundProperty, "Raised");
            button.SetResourceReference(ForegroundProperty, "Text");
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                _updateCheckStatus.Text = Strings.UpdateChecking;
                try
                {
                    _updateCheckStatus.Text = await checkForUpdates();
                    ShowLastChecked(DateTimeOffset.Now);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("WinMon manual update check: {0}", ex.Message);
                    _updateCheckStatus.Text = Strings.UpdateFailed;
                }
                finally
                {
                    button.IsEnabled = true;
                }
            };
            DockPanel.SetDock(button, Dock.Left);
            check.Children.Add(button);
        }

        check.Children.Add(_updateCheckStatus);
        page.Children.Add(check);
        page.Children.Add(CreateHeading(Strings.SettingsAutomaticChecks));
        page.Children.Add(_automaticUpdateCheckBox);
        page.Children.Add(Note(Strings.SettingsUpdatesNote));
        return page;

        void ShowLastChecked(DateTimeOffset? time)
        {
            _lastChecked.Text = time is { } checkedAt
                ? Strings.Format(Strings.SettingsCheckedAt, checkedAt.LocalDateTime)
                : Strings.SettingsNotChecked;
        }
    }

    /// <summary>A section page: its title, with an optional control such as a switch at the right.</summary>
    private static StackPanel Page(string title, FrameworkElement? headerControl = null)
    {
        var page = new StackPanel { Margin = new Thickness(26, 18, 26, 18) };
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        if (headerControl is not null)
        {
            DockPanel.SetDock(headerControl, Dock.Right);
            header.Children.Add(headerControl);
        }

        var heading = new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        header.Children.Add(heading);
        page.Children.Add(header);
        return page;
    }

    private static TextBlock CreateHeading(string text, bool first = false)
    {
        TextBlock heading = new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, first ? 2 : 16, 0, 4)
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        return heading;
    }

    private static TextBlock Note(string text)
    {
        var note = new TextBlock
            { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return note;
    }

    private static string RetentionLabel(int seconds)
    {
        return seconds switch
        {
            >= 3600 => Strings.Value60Minutes,
            >= 1800 => Strings.Value30Minutes,
            _ => Strings.Value10Minutes
        };
    }

    private static Grid Field(string label, Control control)
    {
        Grid field = new() { Margin = new Thickness(0, 3, 0, 3) };
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
        return field;
    }

    private StackPanel CreateButtons()
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
        _saveButton = saveButton;
        cancelButton.SetResourceReference(BackgroundProperty, "Raised");
        ButtonIcon.SetData(cancelButton, ButtonIcon.Cancel);
        ButtonIcon.SetData(saveButton, ButtonIcon.Save);
        cancelButton.SetResourceReference(ForegroundProperty, "Text");
        saveButton.SetResourceReference(BackgroundProperty, "Raised");
        saveButton.SetResourceReference(ForegroundProperty, "Text");
        saveButton.SetResourceReference(BorderBrushProperty, "Accent");
        saveButton.Click += SaveButton_Click;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);
        return buttons;
    }

    /// <summary>Show the section holding a field that failed validation, then focus that field.</summary>
    private void Reject(SettingsSection section, Control? field, string message, string title)
    {
        ShowSection(section);
        UpdateLayout();
        MessageDialog.ShowNotice(this, message, title);
        field?.Focus();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var primary = (PrimaryDriveSelector.SelectedItem as DriveChoice)?.Name;
        var secondary = (SecondaryDriveSelector.SelectedItem as DriveChoice)?.Name;
        if (primary is null || primary == secondary)
        {
            Reject(SettingsSection.Monitoring, primary is null ? PrimaryDriveSelector : SecondaryDriveSelector,
                Strings.ChooseAPrimaryVolumeAndADifferentSecondVolumeOr, Strings.DiskMonitoring);
            return;
        }

        if (AutoCapture.Read(out var captureError, out var invalid) is not { } autoCapture)
        {
            Reject(SettingsSection.AutomaticCaptures, invalid, captureError!, Strings.AutoCaptureHeading);
            return;
        }

        // Only a chosen folder is probed; the default is created when the first report is written.
        if (autoCapture is { Enabled: true, Folder: { } folder } && !AutoCaptureSection.CanWrite(folder))
        {
            Reject(SettingsSection.AutomaticCaptures, null, Strings.AutoCaptureFolderNotWritable,
                Strings.AutoCaptureHeading);
            return;
        }

        Result = ApplySettings(_initialPreferences, new MonitorPreferences
        {
            PrimaryDrive = primary,
            SecondaryDrive = secondary,
            Theme = (_themeSelector.SelectedItem as ThemeChoice)?.Name ?? "Dark",
            Language = LanguageSelector.SelectedValue as string ?? AppLanguages.SystemDefault,
            ChartPalette = _paletteSelector.SelectedValue as string ?? "Classic",
            GaugeStyle = SelectedGaugeStyle,
            RetentionSeconds = (_retentionSelector.SelectedItem as string) switch
            {
                var label when label == Strings.Value30Minutes => 1800,
                var label when label == Strings.Value60Minutes => 3600,
                _ => 600
            },
            MinimizeToTray = _minimizeToTrayCheckBox.IsChecked == true,
            StartWithWindows = _startupCheckBox.IsChecked == true,
            AutomaticallyCheckForUpdates = _automaticUpdateCheckBox.IsChecked == true,
            ShowShortcut = _shortcut?.ToString() ?? HotkeyGesture.NoneText,
            AutoCapture = autoCapture
        });
        DialogResult = true;
    }
}
