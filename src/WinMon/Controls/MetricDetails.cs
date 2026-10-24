using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WinMon.Controls;

/// <summary>One shared set of live readings, rendered separately by full and compact tooltips.</summary>
public sealed class MetricDetails : INotifyPropertyChanged
{
    private string _title = "";
    private string _definition = "";
    private string _live = "";
    private Brush? _accent;

    public string Title { get => _title; set => Set(ref _title, value); }
    public string Definition { get => _definition; set => Set(ref _definition, value); }
    public string Live { get => _live; set => Set(ref _live, value); }
    public Brush? Accent { get => _accent; set => Set(ref _accent, value); }
    public ObservableCollection<DetailRow> Rows { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

/// <summary>A fixed tooltip row whose value changes only when the text changes.</summary>
public sealed class DetailRow : INotifyPropertyChanged
{
    private string _label = "";
    private string _value = "";

    public string Label { get => _label; set => Set(ref _label, value); }
    public string Value { get => _value; set => Set(ref _value, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string value, [CallerMemberName] string? property = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
