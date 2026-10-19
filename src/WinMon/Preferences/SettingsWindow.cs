using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace WinMon.Preferences;

/// <summary>
/// Provides controls for editing monitor preferences before they are persisted.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly MonitorPreferences _initialPreferences;
    private readonly ComboBox _themeSelector;
    private readonly ComboBox _compactHistorySelector;
    private readonly ComboBox _retentionSelector;
    private readonly CheckBox _closeToTrayCheckBox;
    private readonly CheckBox _startupCheckBox;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
    /// </summary>
    /// <param name="preferences">The preferences shown when the dialog opens.</param>
    public SettingsWindow(MonitorPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _initialPreferences = preferences;
        Controls.WindowTheme.Attach(this);

        Title = "WinMon settings";
        Width = 460;
        Height = 410;
        MinWidth = 420;
        MinHeight = 390;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Text");

        _themeSelector = CreateSelector(["Dark", "Light", "System"], preferences.Theme);
        _compactHistorySelector = CreateSelector(["1 minute", "2 minutes", "5 minutes"], CompactHistoryLabel(preferences.CompactHistorySeconds));
        _retentionSelector = CreateSelector(["10 minutes", "30 minutes", "60 minutes"], RetentionLabel(preferences.RetentionSeconds));
        _closeToTrayCheckBox = CreateCheckBox("Close to the notification area", preferences.CloseToTray);
        _startupCheckBox = CreateCheckBox("Start WinMon when I sign in", preferences.StartWithWindows);

        Grid layout = new()
        {
            Margin = new Thickness(24)
        };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock heading = CreateHeading("Appearance");
        Grid.SetRow(heading, 0);
        layout.Children.Add(heading);
        AddField(layout, "Theme", _themeSelector, 2);
        AddField(layout, "Compact graph", _compactHistorySelector, 4);
        AddField(layout, "Historical retention", _retentionSelector, 6);
        Grid.SetRow(_closeToTrayCheckBox, 8);
        layout.Children.Add(_closeToTrayCheckBox);
        Grid.SetRow(_startupCheckBox, 10);
        layout.Children.Add(_startupCheckBox);
        StackPanel buttons = CreateButtons();
        Grid.SetRow(buttons, 12);
        layout.Children.Add(buttons);

        Border surface = new() { Child = layout };
        surface.SetResourceReference(Border.BackgroundProperty, "Surface");
        Content = surface;
    }

    /// <summary>
    /// Gets the edited preferences when the dialog is accepted, or <see langword="null"/> when it is cancelled.
    /// </summary>
    public MonitorPreferences? Result { get; private set; }

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

    private static string CompactHistoryLabel(int seconds) => seconds switch
    {
        60 => "1 minute",
        300 => "5 minutes",
        _ => "2 minutes"
    };

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
        Result = _initialPreferences with
        {
            Theme = _themeSelector.SelectedItem as string ?? "Dark",
            CompactHistorySeconds = (_compactHistorySelector.SelectedItem as string) switch
            {
                "1 minute" => 60,
                "5 minutes" => 300,
                _ => 120
            },
            RetentionSeconds = (_retentionSelector.SelectedItem as string) switch
            {
                "30 minutes" => 1800,
                "60 minutes" => 3600,
                _ => 600
            },
            CloseToTray = _closeToTrayCheckBox.IsChecked == true,
            StartWithWindows = _startupCheckBox.IsChecked == true
        };
        DialogResult = true;
    }
}

