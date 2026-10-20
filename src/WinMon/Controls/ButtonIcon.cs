using System.Windows;
using System.Windows.Media;

namespace WinMon.Controls;

/// <summary>Shared vector icons displayed beside button labels by the application style.</summary>
public static class ButtonIcon
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.RegisterAttached(
        "Data", typeof(Geometry), typeof(ButtonIcon), new PropertyMetadata(null));

    public static Geometry? GetData(DependencyObject element) => (Geometry?)element.GetValue(DataProperty);
    public static void SetData(DependencyObject element, Geometry? value) => element.SetValue(DataProperty, value);

    public static Geometry Pin { get; } = Create("M 5,2 L 11,2 M 6,2 L 6,6 L 3,9 L 3,10 L 13,10 L 13,9 L 10,6 L 10,2 M 8,10 L 8,15");
    public static Geometry Pause { get; } = Create("M 5,2 L 5,14 M 11,2 L 11,14");
    public static Geometry Resume { get; } = Create("M 4,2 L 13,8 L 4,14 Z");
    public static Geometry Live { get; } = Create("M 13,5 A 6,6 0 1 0 14,10 M 13,1 L 13,5 L 9,5");
    public static Geometry Tray { get; } = Create("M 2,10 L 2,14 L 14,14 L 14,10 M 8,1 L 8,10 M 4,6 L 8,10 L 12,6");
    public static Geometry Settings { get; } = Create("M 2,4 L 5,4 M 9,4 L 14,4 M 2,12 L 9,12 M 13,12 L 14,12 M 5,2 L 9,2 L 9,6 L 5,6 Z M 9,10 L 13,10 L 13,14 L 9,14 Z");
    public static Geometry Cancel { get; } = Create("M 3,3 L 13,13 M 13,3 L 3,13");
    public static Geometry Save { get; } = Create("M 2,2 L 12,2 L 14,4 L 14,14 L 2,14 Z M 5,2 L 5,6 L 11,6 L 11,2 M 5,14 L 5,9 L 11,9 L 11,14");

    private static Geometry Create(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
