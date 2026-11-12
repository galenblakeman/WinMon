using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using WinMon.Mcp;

if (args.Length > 0)
{
    var encoding = Console.OutputEncoding;
    try
    {
        if (!Console.IsOutputRedirected) Console.OutputEncoding = new UTF8Encoding(false);
        if (args[0] == "--installer-registration")
        {
            Environment.ExitCode = await McpCommandLine.RunAsync(args, RunInstallerRegistration, Console.Out, Console.Error);
        }
        else
        {
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler handler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += handler;
            try
            {
                Environment.ExitCode = await McpCommandLine.RunAsync(args, RunInstallerRegistration, Console.Out, Console.Error,
                    cancellationToken: cancellation.Token);
            }
            finally
            {
                Console.CancelKeyPress -= handler;
            }
        }
    }
    finally
    {
        if (!Console.IsOutputRedirected) Console.OutputEncoding = encoding;
    }
    return;
}

// Native collectors trace exceptions containing OS metadata; never forward these to an agent.
Trace.Listeners.Clear();
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    DisableDefaults = true
});
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddSingleton<SamplingOwner>();
builder.Services.AddSingleton<DiagnosticTools>();
builder.Services.AddHostedService(services => services.GetRequiredService<SamplingOwner>());
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<DiagnosticTools>();
await builder.Build().RunAsync();

static InstallerRegistrationExitCode RunInstallerRegistration(string[] args)
{
    if (args.Length is not (4 or 5) || args[2] != "--install-path")
        return InstallerRegistrationExitCode.InvalidArguments;
    var client = args[1];
    var unregister = args.Length == 5 && args[4] == "--unregister";
    if (args.Length == 5 && !unregister) return InstallerRegistrationExitCode.InvalidArguments;
    var registration = new InstallerRegistration();
    return unregister
        ? registration.Unregister(client, args[3], Console.Error)
        : registration.Register(client, args[3], Console.Error);
}
