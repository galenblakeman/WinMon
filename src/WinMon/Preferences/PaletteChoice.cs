using System.ComponentModel;

namespace WinMon.Preferences;

/// <summary>A named palette whose preview follows the theme selected in Settings.</summary>
internal sealed class PaletteChoice(string name) : INotifyPropertyChanged
{
    public string Name { get; } = name;

    public string DisplayName => ChartPalettes.DisplayName(Name);

    public string Description => ChartPalettes.Description(Name);

    public IReadOnlyList<string> Colors { get; private set; } = ChartPalettes.Colors(name, false);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void UpdateTheme(bool light, ThemeFinishes.Finish? finish = null)
    {
        Colors = ChartPalettes.ColorsForTheme(Name, light, finish);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Colors)));
    }

    public override string ToString()
    {
        return DisplayName;
    }
}
