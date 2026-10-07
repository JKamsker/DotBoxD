using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Peer;
using DotBoxD.Transports.NamedPipes;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;
using Examples.SandboxedUi.Contracts;
using Examples.SandboxedUi.Host;

if (args.Length != 1) { throw new ArgumentException("Pass the path to Examples.SandboxedUi.Plugin.dll."); }
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var token = timeout.Token;
var pipe = "dotboxd-ui-" + Guid.NewGuid().ToString("N");
var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
start.ArgumentList.Add(Path.GetFullPath(args[0]));
start.ArgumentList.Add(pipe);
using var worker = Process.Start(start) ?? throw new InvalidOperationException("Worker did not start.");
var errors = worker.StandardError.ReadToEndAsync(token);
try
{
    if (await worker.StandardOutput.ReadLineAsync(token) != "ready")
    {
        throw new InvalidOperationException("Plugin failed to start: " + await errors);
    }

    await using var connection = await RpcMessagePackIpc.ConnectAsync(new NamedPipeClientTransport(pipe, maxMessageSize: 512 * 1024),
        new RpcPeerOptions { RejectInboundCalls = true, MaxInboundBytes = 512 * 1024, RequestTimeout = TimeSpan.FromSeconds(5) }, token);
    var plugin = connection.Get<IUiPlugin>();
    var policy = new UiPolicy { MaxPackageBytes = 128 * 1024 };
    var package = UiPackageJson.Import(await plugin.GetPackageAsync(token), policy);
    using var sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings());
    var renderer = new HeadlessRenderer();
    var remote = new SearchTransport(plugin);
    await using var session = await new UiHost(sandbox, SandboxPolicyBuilder.Create().Build(), policy)
        .InstallAsync(package, renderer, remote, token);
    var counter = await session.DispatchAsync(1, token);
    if (counter.State.Single(s => s.SlotId == 1).Value.Integer != 1 || remote.Calls != 0)
    {
        throw new InvalidOperationException("Counter did not execute locally.");
    }

    await session.SetInputAsync(4, UiPropertyId.Text, UiValue.FromString("ap"), token);
    var updatesBeforeSearch = renderer.Updates;
    var search = await session.DispatchAsync(2, token);
    if (search.State.Single(s => s.SlotId == 3).Value.Text != "apple, apricot" || renderer.Materializations != 1 ||
        renderer.Updates != updatesBeforeSearch + 1)
    {
        throw new InvalidOperationException("Remote search or incremental rendering failed.");
    }

    if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Examples.SandboxedUi.Plugin"))
    {
        throw new InvalidOperationException("Plugin implementation assembly was loaded in the host.");
    }

    worker.Kill(entireProcessTree: true);
    await worker.WaitForExitAsync(token);
    try
    {
        await session.DispatchAsync(2, token);
        throw new InvalidOperationException("Disconnected plugin was accepted.");
    }
    catch (Exception) when (session.IsDisconnected && renderer.Disposed)
    {
        Console.WriteLine("PASS: local counter; two-way input; remote search; single materialization; plugin assembly absent; crash contained.");
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
