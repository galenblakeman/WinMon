using System.Windows;
using System.Windows.Media;

namespace WinMon.Controls;

/// <summary>Shared vector icons displayed beside button labels by the application style.</summary>
public static class ButtonIcon
{
    public static readonly DependencyProperty FilledProperty = DependencyProperty.RegisterAttached(
        "Filled", typeof(bool), typeof(ButtonIcon), new PropertyMetadata(false));

    public static bool GetFilled(DependencyObject element)
    {
        return (bool)element.GetValue(FilledProperty);
    }

    public static void SetFilled(DependencyObject element, bool value)
    {
        element.SetValue(FilledProperty, value);
    }

    public static readonly DependencyProperty DataProperty = DependencyProperty.RegisterAttached(
        "Data", typeof(Geometry), typeof(ButtonIcon), new PropertyMetadata(null));

    public static Geometry? GetData(DependencyObject element)
    {
        return (Geometry?)element.GetValue(DataProperty);
    }

    public static void SetData(DependencyObject element, Geometry? value)
    {
        element.SetValue(DataProperty, value);
    }

    public static Geometry Compact { get; } = Create("M 2,3 L 14,3 L 14,13 L 2,13 Z M 2,7 L 14,7");
    public static Geometry ChevronUp { get; } = Create("M 3,10 L 8,5 L 13,10");
    public static Geometry ChevronDown { get; } = Create("M 3,6 L 8,11 L 13,6");

    public static Geometry Expand { get; } =
        Create("M 2,6 L 2,2 L 6,2 M 10,2 L 14,2 L 14,6 M 14,10 L 14,14 L 10,14 M 6,14 L 2,14 L 2,10");

    public static Geometry CaptureFrame { get; } =
        Create("M 5,2 L 2,2 L 2,5 M 11,2 L 14,2 L 14,5 M 14,11 L 14,14 L 11,14 M 5,14 L 2,14 L 2,11");

    public static Geometry Pin { get; } =
        Create("M 5,2 L 11,2 M 6,2 L 6,6 L 3,9 L 3,10 L 13,10 L 13,9 L 10,6 L 10,2 M 8,10 L 8,15");

    public static Geometry AlwaysOnTop { get; } =
        Create("M 1,1 L 11,1 L 11,11 L 1,11 Z M 1,4 L 11,4 M 5,11 L 5,15 L 15,15 L 15,5 L 11,5");

    public static Geometry PinFilled { get; } =
        Create(
            "M 5,2 L 11,2 L 11,3 L 10,3 L 10,6 L 13,9 L 13,10 L 9,10 L 8,15 L 7,10 L 3,10 L 3,9 L 6,6 L 6,3 L 5,3 Z");

    public static Geometry Live { get; } = Create("M 13,5 A 6,6 0 1 0 14,10 M 13,1 L 13,5 L 9,5");

    public static Geometry Settings { get; } =
        Create(
            "M 2,4 L 5,4 M 9,4 L 14,4 M 2,12 L 9,12 M 13,12 L 14,12 M 5,2 L 9,2 L 9,6 L 5,6 Z M 9,10 L 13,10 L 13,14 L 9,14 Z");

    public static Geometry Info { get; } = Create("M 8,1 A 7,7 0 1 1 8,15 A 7,7 0 1 1 8,1 M 8,7 L 8,12 M 8,4 L 8,5");
    public static Geometry Cancel { get; } = Create("M 3,3 L 13,13 M 13,3 L 3,13");

    public static Geometry Save { get; } =
        Create("M 2,2 L 12,2 L 14,4 L 14,14 L 2,14 Z M 5,2 L 5,6 L 11,6 L 11,2 M 5,14 L 5,9 L 11,9 L 11,14");

    public static Geometry Copy { get; } = Create("M 5,5 L 14,5 L 14,14 L 5,14 Z M 2,11 L 2,2 L 11,2");
    public static Geometry Folder { get; } = Create("M 1,13 L 1,3 L 6,3 L 8,5 L 15,5 L 15,13 Z M 1,7 L 15,7");
    public static Geometry Start { get; } = Create("M 4,2 L 13,8 L 4,14 Z");
    public static Geometry Stop { get; } = Create("M 3,3 L 13,3 L 13,13 L 3,13 Z");

    public static Geometry Process { get; } =
        Create("M 1,2 L 15,2 L 15,11 L 1,11 Z M 8,11 L 8,14 M 4,14 L 12,14 M 4,8 L 4,6 M 8,8 L 8,4 M 12,8 L 12,5");

    public static Geometry More { get; } =
        Create(
            "M 3,7 A 1,1 0 1 1 3,9 A 1,1 0 1 1 3,7 M 8,7 A 1,1 0 1 1 8,9 A 1,1 0 1 1 8,7 M 13,7 A 1,1 0 1 1 13,9 A 1,1 0 1 1 13,7");

    private static Geometry Create(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}