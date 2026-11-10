using System.CommandLine;
using System.Diagnostics;
using WinMon.Resources;

namespace WinMon.Cli;

internal static class SettingsCommands
{
    internal static Command Create(CliSettingsStore store, TextWriter output, TextWriter error)
    {
        var settings = new Command("settings", Strings.CliSettingsHeading);
        var show = new Command("show", Strings.CliSettingsShow);
        show.SetAction(_ =>
        {
            output.WriteLine(CliPreferences.Serialize(store.Load(() => error.WriteLine(Strings.CliSettingsLoadFailed))));
        });
        settings.Add(show);
        var reset = new Command("reset", Strings.CliSettingsReset);
        reset.SetAction(_ => Save(store, output, error, _ => { }, reset: true));
        settings.Add(reset);
        var network = new Option<bool>("--network") { Description = Strings.NetworkProcessMonitoringHeading, Required = true };
        var set = new Command("set", Strings.CliSettingsSet) { network };
        set.Validators.Add(result =>
        {
            if (result.GetResult(network) is not { Implicit: false })
                result.AddError(Strings.CliSettingsSet + " --network=true|false");
        });
        set.SetAction(result => Save(store, output, error, saved => saved.Network = result.GetValue(network)));
        settings.Add(set);
        return settings;
    }

    private static int Save(CliSettingsStore store, TextWriter output, TextWriter error,
        Action<CliPreferences> change, bool reset = false)
    {
        try
        {
            store.Update(change, reset);
            output.WriteLine(reset ? Strings.CliSettingsResetDone : Strings.CliSettingsUpdated);
            return 0;
        }
        catch (Exception exception) when (CliSettingsStore.IsSettingsError(exception))
        {
            Trace.TraceWarning("CLI settings command failed: {0}", exception);
            error.WriteLine(Strings.CliSettingsSaveFailed);
            return 1;
        }
    }
}
