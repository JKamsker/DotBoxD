using System.Diagnostics;
using Avalonia.Headless;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Peer;
using DotBoxD.Transports.NamedPipes;
using DotBoxD.UI;
using Examples.SandboxedUi.Contracts;
using Examples.SandboxedUi.Host;

if (args.Length != 1) { throw new ArgumentException("Pass the path to Examples.SandboxedUi.Plugin.dll."); }
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
var token = timeout.Token;
await using var avalonia = HeadlessUnitTestSession.StartNew(typeof(SampleApplication));
var previousId = Guid.Empty;
for (var attempt = 0; attempt < 2; attempt++)
{
    var pipe = "dotboxd-ui-" + Guid.NewGuid().ToString("N");
    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(Path.GetFullPath(args[0]));
    start.ArgumentList.Add(pipe);
    using var worker = Process.Start(start) ?? throw new InvalidOperationException("Worker did not start.");
    var errors = worker.StandardError.ReadToEndAsync(token);
    try
    {
        if (await worker.StandardOutput.ReadLineAsync(token) != "ready")
        { throw new InvalidOperationException("Plugin failed to start: " + await errors); }
        await using var connection = await RpcMessagePackIpc.ConnectAsync(new NamedPipeClientTransport(pipe, maxMessageSize: 512 * 1024),
            new RpcPeerOptions { RejectInboundCalls = true, MaxInboundBytes = 512 * 1024, RequestTimeout = TimeSpan.FromSeconds(5) }, token);
        var plugin = connection.Get<IUiPlugin>();
        var package = UiPackageJson.Import(await plugin.GetPackageAsync(token), new UiPolicy { MaxPackageBytes = 128 * 1024 });
        if (attempt == 0)
        {
            await avalonia.Dispatch(async () =>
            {
                previousId = await UiSmoke.RunAsync(plugin, package, connection.Peer, async () =>
                { worker.Kill(entireProcessTree: true); await worker.WaitForExitAsync(token); }, token);
                return true;
            }, token);
        }
        else
        {
            await avalonia.Dispatch(async () => { await UiSmoke.ReconnectAsync(plugin, package, connection.Peer, previousId, token); return true; }, token);
        }
    }
    finally
    {
        if (!worker.HasExited)
        { worker.Kill(entireProcessTree: true); }
        await worker.WaitForExitAsync(CancellationToken.None);
        var diagnostics = await errors;
        if (diagnostics.Length > 0)
        { await Console.Error.WriteLineAsync(diagnostics); }
    }
}
Console.WriteLine("PASS: generated components; local counter; two-way search; host capability; Avalonia offscreen; plugin assembly absent; idle crash release without RPC; fresh-session reconnect.");
