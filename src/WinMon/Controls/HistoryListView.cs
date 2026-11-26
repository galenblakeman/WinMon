using System.Windows;
using System.Windows.Controls;

namespace WinMon.Controls;

/// <summary>Keeps row alignment independent of a parent while History replaces its snapshots.</summary>
internal sealed class HistoryListView : ListView
{
    protected override DependencyObject GetContainerForItemOverride() => new ListViewItem
    {
        // Local values survive detachment, when the window's implicit style is no longer in scope.
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Center
    };
}
