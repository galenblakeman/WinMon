using System.Windows;
using System.Windows.Controls;

namespace WinMon.Controls;

internal sealed partial class ProcessAffinityDialog : Window
{
    private readonly List<CheckBox> _choices = [];
    private readonly bool _readOnly;
    internal ProcessAffinityDialog(ulong processMask, ulong systemMask, bool readOnly)
    {
        InitializeComponent(); _readOnly = readOnly; Title = Strings.SetAffinity; WindowTheme.Attach(this);
        SetResourceReference(BackgroundProperty, "Surface"); SetResourceReference(ForegroundProperty, "Text");
        Explanation.Text = readOnly ? Strings.CpuAffinityReadOnly : Strings.CpuAffinityNone;
        AllButton.Content = Strings.AllProcessors; NoneButton.Content = Strings.NoProcessors;
        OkButton.Content = Strings.SetAffinity; CancelButton.Content = Strings.Cancel;
        CancelButton.Click += (_, _) => Close(); OkButton.Click += (_, _) => { Result = Mask(); DialogResult = true; };
        AllButton.Click += (_, _) => SetAll(true); NoneButton.Click += (_, _) => SetAll(false);
        for (var bit = 0; bit < 64; bit++)
        {
            var mask = 1UL << bit; if ((systemMask & mask) == 0) continue;
            var choice = new CheckBox { Content = Strings.Format(Strings.LogicalProcessorLabel, bit), IsChecked = (processMask & mask) != 0,
                IsEnabled = !readOnly, Margin = new Thickness(0, 2, 0, 2), Tag = mask };
            choice.SetResourceReference(ForegroundProperty, "Text");
            choice.Checked += (_, _) => UpdateButtons(); choice.Unchecked += (_, _) => UpdateButtons();
            _choices.Add(choice); Processors.Items.Add(choice);
        }
        AllButton.IsEnabled = NoneButton.IsEnabled = !readOnly; OkButton.IsEnabled = !readOnly && Mask() != 0;
    }
    internal ulong Result { get; private set; }
    private ulong Mask() => _choices.Aggregate(0UL, (mask, choice) => choice.IsChecked == true ? mask | (ulong)choice.Tag : mask);
    private void SetAll(bool value) { foreach (var choice in _choices) choice.IsChecked = value; }
    private void UpdateButtons() => OkButton.IsEnabled = !_readOnly && Mask() != 0;
}
