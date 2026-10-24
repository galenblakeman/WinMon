using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>Collapsible actions for the selected process, with captured targets in popup menus.</summary>
public sealed class ProcessActionBar : Border
{
    private readonly Grid _layout = new();
    private readonly Border _accent = new() { CornerRadius = new CornerRadius(3, 0, 0, 3) };

    private readonly StackPanel _identity = new()
        { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };

    private readonly WrapPanel _buttons = new()
    {
        Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly TextBlock _name = new()
        { FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };

    private readonly TextBlock _detail = new()
        { FontSize = 11, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

    private readonly Image _icon = new()
        { Width = 28, Height = 28, Margin = new Thickness(0, 0, 12, 0), Source = ProcessIconProvider.Fallback };

    private readonly TextBlock _status = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };

    private readonly Button _properties = ActionButton(),
        _copy = ActionButton(),
        _more = ActionButton(),
        _end = ActionButton();

    internal bool ActionsHidden
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            UpdateVisibility();
        }
    }

    private ProcessDetailsSnapshot? _snapshot;
    private bool _canDump;
    private Func<ProcessUsage, bool>? _canSetPriority;
    private Func<ProcessUsage, ProcessPriorityClass?>? _readPriority;
    private Action<ProcessUsage, ProcessPriorityClass>? _setPriority;
    private (int, long)? _iconIdentity;
    internal event Action<string, ProcessDetailsSnapshot>? ActionRequested;

    public ProcessActionBar()
    {
        MinHeight = 54;
        Margin = new Thickness(0, 6, 0, 0);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        SetResourceReference(BackgroundProperty, "SectionHeaderSurface");
        SetResourceReference(BorderBrushProperty, "Line");
        var outer = new Grid();
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        outer.ColumnDefinitions.Add(new ColumnDefinition());
        outer.Children.Add(_accent);
        _layout.Margin = new Thickness(14, 6, 14, 6);
        Grid.SetColumn(_layout, 1);
        outer.Children.Add(_layout);
        Child = outer;
        _layout.ColumnDefinitions.Add(new ColumnDefinition());
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = new Border
        {
            Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(10, 0, 0, 0),
            CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = _status
        };
        badge.SetResourceReference(BorderBrushProperty, "Line");
        Grid.SetColumn(badge, 1);
        heading.Children.Add(_name);
        heading.Children.Add(badge);
        _identity.Children.Add(heading);
        _identity.Children.Add(_detail);
        var identityLayout = new DockPanel();
        identityLayout.Children.Add(_icon);
        identityLayout.Children.Add(_identity);
        _layout.Children.Add(identityLayout);
        Grid.SetColumn(_buttons, 1);
        _layout.Children.Add(_buttons);
        foreach (var text in new[] { _name, _status }) text.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        _detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        foreach (var button in new[] { _properties, _copy, _more }) _buttons.Children.Add(button);
        var divider = new Border();
        divider.SetResourceReference(StyleProperty, "ActionBarDivider");
        _end.SetResourceReference(StyleProperty, "ActionBarLastButton");
        _buttons.Children.Add(divider);
        _buttons.Children.Add(_end);
        ButtonIcon.SetData(_properties, ButtonIcon.Info);
        ButtonIcon.SetData(_copy, ButtonIcon.Copy);
        ButtonIcon.SetData(_more, ButtonIcon.More);
        ButtonIcon.SetData(_end, ButtonIcon.Cancel);
        _properties.Click += (_, _) => Request("properties");
        _copy.Click += (_, _) => Request("copy");
        _end.Click += (_, _) => Request("end");
        _more.Click += (_, _) => OpenMore();
        SizeChanged += (_, _) => ArrangeButtons();
        Update(null, false, false);
    }

    private void UpdateVisibility()
    {
        Visibility = ActionsHidden ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Button ActionButton()
    {
        var button = new Button();
        button.SetResourceReference(StyleProperty, "ActionBarButton");
        return button;
    }

    private void ArrangeButtons()
    {
        _buttons.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var wide = ActualWidth >= _buttons.DesiredSize.Width + 300;
        Grid.SetColumn(_buttons, wide ? 1 : 0);
        Grid.SetRow(_buttons, wide ? 0 : 1);
        _layout.ColumnDefinitions[1].Width = wide ? GridLength.Auto : new GridLength(0);
        _buttons.Margin = wide ? new Thickness(0) : new Thickness(0, 6, 0, 0);
    }

    internal void Update(ProcessDetailsSnapshot? snapshot, bool canEnd, bool canDump)
    {
        _snapshot = snapshot;
        _canDump = canDump;
        UpdateVisibility();
        var process = snapshot?.Process;
        _accent.SetResourceReference(BackgroundProperty, process is null ? "Line" : "Accent");
        _icon.Visibility = process is null ? Visibility.Collapsed : Visibility.Visible;
        ((Border)_status.Parent).Visibility = process is null ? Visibility.Collapsed : Visibility.Visible;
        _name.Text = process?.Name ?? Strings.SelectProcess;
        _name.ToolTip = _name.Text;
        _detail.Text = process is null
            ? string.Empty
            : (snapshot!.Grouped
                  ? Strings.Format(Strings.Processes, process.MemberCount)
                  : Strings.Format(Strings.PID, process.ProcessId)) +
              (string.IsNullOrWhiteSpace(process.AppName) ? "" : " · " + process.AppName);
        _detail.ToolTip = _detail.Text;
        _status.Text = snapshot is null ? Strings.MissingReading :
            snapshot.State.Length > 0 ? snapshot.State :
            snapshot.Historical ? Strings.ProcessHistoricalView : Strings.LiveMode;
        _properties.Content = Strings.Properties;
        _copy.Content = Strings.CopyDetails;
        _more.Content = Strings.MoreActions + " ▾";
        _end.Content = snapshot?.Grouped == true ? Strings.EndProcessGroup : Strings.EndProcess;
        _end.ToolTip = Strings.AvailableForLiveProcesses;
        _end.BorderBrush = SystemParameters.HighContrast
            ? SystemColors.WindowTextBrush
            : new SolidColorBrush(Color.FromRgb(150, 78, 78));
        _properties.IsEnabled = _copy.IsEnabled = _more.IsEnabled = snapshot is not null;
        _end.IsEnabled = snapshot is { Available: true, Historical: false } && canEnd;
        var identity = process is null ? ((int, long)?)null : (process.ProcessId, process.CreationTicks);
        if (_iconIdentity != identity)
        {
            _iconIdentity = identity;
            _icon.Source = ProcessIconProvider.Fallback;
            if (process is not null) _ = LoadIconAsync(process, identity);
        }

        ArrangeButtons();
    }

    private async Task LoadIconAsync(ProcessUsage process, (int, long)? identity)
    {
        var icon = await ProcessIconProvider.GetAsync(process);
        if (_iconIdentity == identity) _icon.Source = icon;
    }

    private void Request(string action)
    {
        if (_snapshot is { } snapshot) ActionRequested?.Invoke(action, snapshot);
    }

    internal void EnablePriorityActions(Func<ProcessUsage, bool> canSet,
        Func<ProcessUsage, ProcessPriorityClass?> read, Action<ProcessUsage, ProcessPriorityClass> set)
    {
        _canSetPriority = canSet;
        _readPriority = read;
        _setPriority = set;
    }

    internal ContextMenu? CreateMoreMenu()
    {
        if (_snapshot is not { } snapshot) return null;
        var menu = new ContextMenu();
        menu.SetResourceReference(BackgroundProperty, "Raised");
        menu.SetResourceReference(Control.ForegroundProperty, "Text");
        foreach (var (label, action, enabled, icon) in new[]
                 {
                     (Strings.OpenFileLocation, "location", snapshot.Process.ImagePath.Length > 0, ButtonIcon.Folder),
                     (Strings.CreateDumpFile + "…", "dump",
                         snapshot is { Available: true, Historical: false } && _canDump, ButtonIcon.Save)
                 })
        {
            var item = ProcessRow.ActionItem(label, icon);
            item.IsEnabled = enabled;
            if (action == "dump") item.ToolTip = Strings.ProcessDumpUnavailable;
            item.Click += (_, _) => ActionRequested?.Invoke(action, snapshot);
            menu.Items.Add(item);
        }

        if (_canSetPriority is null || _readPriority is null || _setPriority is null) return menu;
        menu.Items.Add(new Separator());
        menu.Items.Add(ProcessPriorityMenu.Create(snapshot.Process,
            snapshot is { Available: true, Historical: false } && _canSetPriority(snapshot.Process),
            _readPriority, _setPriority));

        return menu;
    }

    private void OpenMore()
    {
        if (CreateMoreMenu() is not { } menu) return;
        menu.PlacementTarget = _more;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }
}