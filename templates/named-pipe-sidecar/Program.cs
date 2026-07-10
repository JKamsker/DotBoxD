using DotBoxD.Pushdown.Services;

var pipeName = "dotboxd-sidecar-" + Guid.NewGuid().ToString("N");
await using var host = RpcMessagePackIpc.ListenNamedPipe(pipeName, _ => { });
using var shutdown = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, args) =>
{
    args.Cancel = true;
    shutdown.Cancel();
};
Console.CancelKeyPress += cancelHandler;

try
{
    // The transport enforces current-user-only access; the random name is defense in depth.
    await host.StartAsync(shutdown.Token);
    Console.WriteLine($"Listening on trusted-local pipe {pipeName}. Press Ctrl+C to stop.");

    if (Console.IsInputRedirected)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
    }
    else
    {
        Console.WriteLine("Press Enter to stop.");
        await Task.WhenAny(
            Task.Run(Console.ReadLine),
            Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token));
    }
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
