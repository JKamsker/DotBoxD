using DotBoxD.DebugAdapter.Diagnostics;

namespace DotBoxD.DebugAdapter;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var diagnosticArgument = Array.IndexOf(args, "--diagnostic-log");
        if (diagnosticArgument >= 0 && diagnosticArgument + 1 < args.Length)
        {
            AdapterDiagnostics.Configure(args[diagnosticArgument + 1]);
        }

        var connection = new DapConnection(Console.OpenStandardInput(), Console.OpenStandardOutput());
        await using var session = new DapSession(connection);
        await session.RunAsync(CancellationToken.None).ConfigureAwait(false);
        return 0;
    }
}

