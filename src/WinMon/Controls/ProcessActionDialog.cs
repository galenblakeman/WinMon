namespace WinMon.Controls;

/// <summary>Process confirmations and outcomes use the shared application dialog style.</summary>
internal sealed class ProcessActionDialog(string message, bool confirmation)
    : MessageDialog(Strings.EndProcess, message, confirmation ? Strings.EndProcess : null);
