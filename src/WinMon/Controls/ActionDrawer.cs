using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinMon.Controls;

/// <summary>
///     Holds a tab's action bar. The chevron at the end of the bar slides it away, leaving a strip that names the
///     selection and brings the bar back; see docs/design/ACTION-DRAWER.md.
/// </summary>
[ContentProperty(nameof(Bar))]
public sealed class ActionDrawer : Grid
{
    private const double PeekHeight = 26;
    private static readonly Duration Slide = new(TimeSpan.FromMilliseconds(140));
    private static readonly Duration Rise = new(TimeSpan.FromMilliseconds(100));
    private static readonly IEasingFunction Ease = CreateEase();

    // Clipped, so the bar and the strip slide out of view below its bottom edge.
    private readonly Grid _body = new() { Margin = new Thickness(0, 6, 0, 0), ClipToBounds = true };

    private readonly Border _peek = new()
    {
        Height = PeekHeight, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
        Visibility = Visibility.Collapsed
    };

    private readonly Border _peekAccent = new() { CornerRadius = new CornerRadius(3, 0, 0, 3) };

    private readonly TextBlock _peekTitle = new()
    {
        FontSize = 12, FontWeight = FontWeights.SemiBold, MaxWidth = 280, VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private readonly TextBlock _peekDetail = new()
    {
        FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private readonly Button _show = new() { Height = 22, FontSize = 11, Margin = new Thickness(0, 0, 2, 0) };
    private readonly TranslateTransform _barShift = new(), _peekShift = new();
    private UIElement? _bar;
    private bool _selected;

    // Each move takes a new number, so a superseded animation's completion does nothing.
    private int _motion;

    // Set when the control that had keyboard focus is about to be hidden.
    private bool _refocus;

    /// <summary>
    ///     What the chevron adds to a bar's buttons: its divider, the chevron and the previous button's margin. Bars
    ///     take it from the selection's share of the width, so they wrap at the same widths as without it.
    /// </summary>
    internal const double CollapseButtonSpace = 53;

    /// <summary>Sent by the chevron at the end of a bar's buttons (style <c>ActionBarCollapseButton</c>).</summary>
    public static RoutedCommand CollapseCommand { get; } = new(nameof(CollapseCommand), typeof(ActionDrawer));

    /// <summary>Raised when the user collapses or restores the bar; the window keeps the preference.</summary>
    internal event Action<bool>? CollapsedChanged;

    internal bool Collapsed { get; private set; }
    internal Button ShowButton => _show;
    internal Border Peek => _peek;
    internal string PeekTitle => _peekTitle.Text;

    public UIElement? Bar
    {
        get => _bar;
        set
        {
            if (_bar is not null) _body.Children.Remove(_bar);
            _bar = value;
            if (value is null) return;
            value.RenderTransform = _barShift;
            _body.Children.Insert(0, value);
            Settle();
        }
    }

    public ActionDrawer()
    {
        _peek.SetResourceReference(Border.BackgroundProperty, "SectionHeaderSurface");
        _peek.SetResourceReference(Border.BorderBrushProperty, "Line");
        _peek.RenderTransform = _peekShift;
        _peekAccent.SetResourceReference(Border.BackgroundProperty, "Line");
        _peekTitle.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        _peekDetail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _show.SetResourceReference(StyleProperty, "TitleActionButton");
        _show.SetResourceReference(Control.ForegroundProperty, "Muted");
        ButtonIcon.SetData(_show, ButtonIcon.ChevronUp);
        _show.Click += (_, _) => Request(false);
        CommandBindings.Add(new CommandBinding(CollapseCommand, (_, _) => Request(true),
            (_, e) => e.CanExecute = !Collapsed));
        var peekLayout = new Grid();
        peekLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        peekLayout.ColumnDefinitions.Add(new ColumnDefinition());
        peekLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new DockPanel { Margin = new Thickness(10, 0, 10, 0) };
        text.Children.Add(_peekTitle);
        text.Children.Add(_peekDetail);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(_show, 2);
        peekLayout.Children.Add(_peekAccent);
        peekLayout.Children.Add(text);
        peekLayout.Children.Add(_show);
        _peek.Child = peekLayout;
        _body.Children.Add(_peek);
        Children.Add(_body);
        RefreshLabels();
    }

    /// <summary>Show the saved state without animating or raising <see cref="CollapsedChanged"/>.</summary>
    internal void SetCollapsed(bool collapsed)
    {
        RefreshLabels();
        if (collapsed != Collapsed) Move(collapsed, false);
    }

    /// <summary>The selection the collapsed strip names. Unchanged text costs no layout.</summary>
    internal void Summarize(string title, string detail, bool selected)
    {
        _peekTitle.Text = title;
        _peekDetail.Text = detail;
        if (_selected == selected) return;
        _selected = selected;
        _peekAccent.SetResourceReference(Border.BackgroundProperty, selected ? "Accent" : "Line");
    }

    private void Request(bool collapsed)
    {
        if (collapsed == Collapsed) return;
        _refocus = IsKeyboardFocusWithin;
        Move(collapsed, IsVisible && SystemParameters.ClientAreaAnimation);
        CollapsedChanged?.Invoke(collapsed);
    }

    private void RefreshLabels()
    {
        _show.Content = Strings.ShowActions;
    }

    // Only transforms move while sliding, so the table lays out once, when the bar and the strip swap.
    private void Move(bool collapsed, bool animate)
    {
        Collapsed = collapsed;
        var motion = ++_motion;
        if (_bar is not { } bar || !animate)
        {
            Settle();
            return;
        }

        if (collapsed)
        {
            Animate(_barShift, bar.RenderSize.Height, Slide, () =>
            {
                Settle();
                _peekShift.Y = PeekHeight;
                Animate(_peekShift, 0, Rise);
            });
        }
        else
        {
            Animate(_peekShift, PeekHeight, Rise, () =>
            {
                Settle();
                bar.Measure(new Size(_body.ActualWidth, double.PositiveInfinity));
                _barShift.Y = bar.DesiredSize.Height;
                Animate(_barShift, 0, Slide);
            });
        }

        void Animate(TranslateTransform shift, double to, Duration duration, Action? then = null)
        {
            var animation = new DoubleAnimation(to, duration) { EasingFunction = Ease };
            animation.Completed += (_, _) =>
            {
                if (motion != _motion) return;
                shift.BeginAnimation(TranslateTransform.YProperty, null);
                shift.Y = to;
                then?.Invoke();
            };
            shift.BeginAnimation(TranslateTransform.YProperty, animation);
        }
    }

    // Swaps the bar and the strip. Keyboard focus follows to Show actions, or to the bar's first button.
    private void Settle()
    {
        foreach (var shift in new[] { _barShift, _peekShift })
        {
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = 0;
        }

        if (_bar is not null) _bar.Visibility = Collapsed ? Visibility.Collapsed : Visibility.Visible;
        _peek.Visibility = Collapsed ? Visibility.Visible : Visibility.Collapsed;
        if (!_refocus) return;
        _refocus = false;
        if (Collapsed) _show.Focus();
        else _bar?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private static IEasingFunction CreateEase()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }
}
