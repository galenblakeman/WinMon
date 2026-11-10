using System.Text;
using WinMon.Cli;

var encoding = Console.OutputEncoding;
try
{
    if (!Console.IsOutputRedirected) Console.OutputEncoding = new UTF8Encoding(false);
    return await CliCommand.Create().Parse(args.Length == 0 ? ["watch"] : args).InvokeAsync();
}
finally
{
    if (!Console.IsOutputRedirected) Console.OutputEncoding = encoding;
}
