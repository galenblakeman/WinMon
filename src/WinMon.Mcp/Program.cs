using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using WinMon.Mcp;

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
