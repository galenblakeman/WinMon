using System.Windows;

namespace WinMon.Controls;

internal enum SuspendedCloseChoice { Cancel, Resume, Leave }

internal sealed partial class SuspendedProcessCloseDialog : Window
{
    internal SuspendedCloseChoice Choice { get; private set; }
    internal SuspendedProcessCloseDialog()
    {
        InitializeComponent();
        Title = Strings.ResumeSuspendedProcesses;
        WindowTheme.Attach(this);
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Text");
        Prompt.Text = Strings.ResumeSuspendedProcessesPrompt;
        ResumeButton.Content = Strings.ResumeProcess;
        LeaveButton.Content = Strings.LeaveSuspended;
        CancelButton.Content = Strings.CancelClose;
        ResumeButton.Click += (_, _) => Select(SuspendedCloseChoice.Resume);
        LeaveButton.Click += (_, _) => Select(SuspendedCloseChoice.Leave);
        CancelButton.Click += (_, _) => Close();
    }

    private void Select(SuspendedCloseChoice choice)
    {
        Choice = choice;
        Close();
    }
}
