using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WinMon.Monitoring;

namespace WinMon.Controls;

internal sealed class ProcessDumpDialog : Window
{
    private bool _writing;

    internal ProcessDumpDialog(ProcessUsage process, Func<bool> canDump)
    {
        Title = Strings.CreateDumpFile;
        Width = 500; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowTheme.Attach(this);
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Text");
        var layout = new StackPanel { Margin = new Thickness(24) };
        layout.Children.Add(new TextBlock { Text = $"{process.Name} (PID {process.ProcessId})", FontWeight = FontWeights.SemiBold });
        layout.Children.Add(new TextBlock { Text = Strings.ProcessDumpNote, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        var full = new CheckBox { Content = Strings.ProcessFullDump, Margin = new Thickness(0, 0, 0, 12) };
        layout.Children.Add(full);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        layout.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var create = new Button { Content = Strings.CreateDumpFile, MinWidth = 130 };
        ButtonIcon.SetData(create, ButtonIcon.Save);
        var close = new Button { Content = Strings.Close, IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        close.Click += (_, _) => Close();
        buttons.Children.Add(create); buttons.Children.Add(close); layout.Children.Add(buttons);
        Content = layout;
        Closing += (_, e) => e.Cancel = _writing;
        create.Click += async (_, _) =>
        {
            if (_writing || !canDump()) { status.Text = Strings.ProcessDumpUnavailable; return; }
            var name = string.Concat(process.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var save = new SaveFileDialog { Title = Strings.CreateDumpFile, Filter = Strings.ProcessDumpFilter,
                DefaultExt = ".dmp", AddExtension = true, OverwritePrompt = true,
                FileName = $"{name}-{process.ProcessId}-{DateTime.Now:yyyyMMdd-HHmmss}.dmp" };
            if (save.ShowDialog(this) != true) return;
            if (!canDump()) { status.Text = Strings.ProcessDumpUnavailable; return; }
            var fullMemory = full.IsChecked == true;
            _writing = true; create.IsEnabled = false; close.IsEnabled = false; full.IsEnabled = false;
            status.Text = Strings.ProcessDumpWriting;
            try
            {
                await Task.Run(() => ProcessDumpWriter.Write(process, save.FileName, fullMemory));
                status.Text = Strings.Format(Strings.ProcessDumpSaved, save.FileName);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
            { status.Text = Strings.Format(Strings.ProcessDumpFailed, ex.Message); }
            finally { _writing = false; close.IsEnabled = true; full.IsEnabled = true; create.IsEnabled = true; }
        };
    }
}
