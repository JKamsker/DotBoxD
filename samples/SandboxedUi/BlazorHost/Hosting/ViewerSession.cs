using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Peer;
using DotBoxD.Transports.NamedPipes;
using DotBoxD.UI;
using DotBoxD.UI.Blazor;
using DotBoxD.UI.Runtime;
using Examples.SandboxedUi.Contracts;
using Examples.SandboxedUi.Host;
using Examples.SandboxedUi.Shared;

namespace Examples.SandboxedUi.BlazorHost;

internal sealed class ViewerSession : IAsyncDisposable
{
    private readonly Process _worker;
    private readonly SandboxHost _sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings().AddBinding(SampleScoreBinding.Create()));
    private RpcPeerSession? _connection;
    private UiSessionConnection? _lifecycle;
    private Task<string>? _errors;
    private ViewerSession(Process worker) => _worker = worker;
    public UiSession Session { get; private set; } = null!;
    public UiPackage Package { get; private set; } = null!;
    public SearchTransport Transport { get; private set; } = null!;

    public static async Task<ViewerSession> CreateAsync(string workerPath, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var pipe = "dotboxd-blazor-" + Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.GetFullPath(workerPath));
        start.ArgumentList.Add(pipe);
        var owner = new ViewerSession(Process.Start(start) ?? throw new InvalidOperationException("Worker did not start."));
        try
        {
            owner._errors = owner._worker.StandardError.ReadToEndAsync(CancellationToken.None);
            if (await owner._worker.StandardOutput.ReadLineAsync(timeout.Token) != "ready")
            { throw new InvalidOperationException("Plugin worker did not become ready."); }
            owner._connection = await RpcMessagePackIpc.ConnectAsync(new NamedPipeClientTransport(pipe, maxMessageSize: 512 * 1024),
                new RpcPeerOptions { RejectInboundCalls = true, MaxInboundBytes = 512 * 1024, RequestTimeout = TimeSpan.FromSeconds(5) }, timeout.Token);
            var plugin = owner._connection.Get<IUiPlugin>();
            var policy = new UiPolicy { MaxPackageBytes = 128 * 1024 };
            owner.Package = UiPackageJson.Import(await plugin.GetPackageAsync(timeout.Token), policy);
            owner.Transport = new SearchTransport(plugin);
            var kernelPolicy = SandboxPolicyBuilder.Create().Grant("game.score.read", new Dictionary<string, string>(), SandboxEffect.HostStateRead).Build();
            owner.Session = await new UiHost(owner._sandbox, kernelPolicy, policy).InstallAsync(
                owner.Package, new BlazorUiRenderer(policy, resources: Examples.SandboxedUi.Host.SampleResources.Images()), owner.Transport, timeout.Token);
            owner._lifecycle = new UiSessionConnection(owner._connection.Peer, owner.Session);
            return owner;
        }
        catch { await owner.DisposeAsync(); throw; }
    }

    public async Task CrashAsync(CancellationToken token)
    {
        if (!_worker.HasExited)
        { _worker.Kill(entireProcessTree: true); }
        await _worker.WaitForExitAsync(token);
        if (_lifecycle is not null)
        { await _lifecycle.Released.WaitAsync(TimeSpan.FromSeconds(5), token); }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_lifecycle is not null)
            { await _lifecycle.DisposeAsync(); }
            if (Session is not null)
            { await Session.DisposeAsync(); }
            if (_connection is not null)
            { await _connection.DisposeAsync(); }
        }
        finally
        {
            if (!_worker.HasExited)
            { _worker.Kill(entireProcessTree: true); }
            await _worker.WaitForExitAsync(CancellationToken.None);
            if (_errors is not null)
            {
                var errors = await _errors;
                if (errors.Length > 0)
                { await Console.Error.WriteLineAsync(errors); }
            }
            _worker.Dispose();
            _sandbox.Dispose();
        }
    }
}
