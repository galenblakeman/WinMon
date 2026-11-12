using WinMon.Resources;
using WinMon.Updates;

namespace WinMon.Mcp;

internal static class McpCommandLine
{
    internal static async Task<int> RunAsync(string[] args, Func<string[], InstallerRegistrationExitCode> runRegistration,
        TextWriter output, TextWriter error,
        Func<ToolUpdateAction, CancellationToken, Task<int>>? runUpdate = null,
        CancellationToken cancellationToken = default)
    {
        if (args.Length > 0 && args[0] == "--installer-registration")
            return (int)runRegistration(args);

        if (IsHelp(args))
        {
            output.WriteLine(string.Format(Strings.ToolUpdateUsage, "WinMon.Mcp.exe"));
            return 0;
        }

        if (args.Length == 2 && args[0] == "update" && TryGetUpdateAction(args[1], out var action))
            return await (runUpdate?.Invoke(action, cancellationToken) ??
                ToolUpdateCommand.RunAsync(ToolUpdateProduct.Mcp, action, output, error, cancellationToken));

        error.WriteLine(string.Format(Strings.ToolUpdateUsage, "WinMon.Mcp.exe"));
        return 64;
    }

    private static bool IsHelp(string[] args) =>
        args is ["--help" or "-h"] or ["update", "--help" or "-h"];

    private static bool TryGetUpdateAction(string value, out ToolUpdateAction action)
    {
        switch (value)
        {
            case "check": action = ToolUpdateAction.Check; return true;
            case "install": action = ToolUpdateAction.Install; return true;
            default: action = default; return false;
        }
    }
}
