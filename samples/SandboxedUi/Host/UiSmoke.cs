using Avalonia;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Services.Peer;
using DotBoxD.UI;
using DotBoxD.UI.Avalonia;
using DotBoxD.UI.Runtime;
using Examples.SandboxedUi.Contracts;
using Examples.SandboxedUi.Shared;

namespace Examples.SandboxedUi.Host;

internal static class UiSmoke
{
    public static async Task<Guid> RunAsync(IUiPlugin plugin, UiPackage package, RpcPeer peer, Func<Task> crash, CancellationToken token)
    {
        using var sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings().AddBinding(SampleScoreBinding.Create()));
        var kernelPolicy = SandboxPolicyBuilder.Create().Grant("game.score.read", new Dictionary<string, string>(), SandboxEffect.HostStateRead).Build();
        var host = new UiHost(sandbox, kernelPolicy, new UiPolicy { MaxPackageBytes = 128 * 1024 });
        var renderer = new AvaloniaUiRenderer();
        var remote = new SearchTransport(plugin);
        await using var session = await host.InstallAsync(package, renderer, remote, token);
        await using var connection = new UiSessionConnection(peer, session);
        var root = renderer.Root;
        var counter = await session.DispatchAsync(1, token);
        Require(counter.State.Single(s => s.SlotId == 1).Value.Integer == 1 && remote.Calls == 0, "local counter");
        var score = await session.DispatchAsync(3, token);
        Require(score.State.Single(s => s.SlotId == 4).Value.Integer == 42 && remote.Calls == 0, "host capability");
        var queryNode = package.Nodes.Single(n => n.Primitive == UiPrimitive.TextBox).Id;
        await session.SetInputAsync(queryNode, UiPropertyId.Text, UiValue.FromString("ap"), token);
        var updates = renderer.Updates;
        var search = await session.DispatchAsync(2, token);
        Require(search.State.Single(s => s.SlotId == 3).Value.Text == "apple, apricot" && renderer.Updates == updates + 1, "remote search batch");
        Require(renderer.Materializations == 1 && ReferenceEquals(root, renderer.Root), "incremental controls");
        using var frame = await renderer.CaptureAsync(new PixelSize(400, 300), new Vector(96, 96), token);
        Require(frame.PixelSize.Width == 400, "offscreen rendering");
        Require(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Examples.SandboxedUi.Plugin"), "assembly boundary");
        var calls = remote.Calls;
        await crash();
        await connection.Released.WaitAsync(TimeSpan.FromSeconds(5), token);
        Require(session.IsDisconnected && renderer.IsDisposed && renderer.Root is null && remote.Calls == calls, "idle worker release without RPC");
        try
        {
            await session.DispatchAsync(1, token);
            throw new InvalidOperationException("Disconnected worker was accepted.");
        }
        catch (ObjectDisposedException) { }
        return session.Id;
    }

    public static async Task ReconnectAsync(IUiPlugin plugin, UiPackage package, RpcPeer peer, Guid previousId, CancellationToken token)
    {
        using var sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings().AddBinding(SampleScoreBinding.Create()));
        var policy = SandboxPolicyBuilder.Create().Grant("game.score.read", new Dictionary<string, string>(), SandboxEffect.HostStateRead).Build();
        await using var session = await new UiHost(sandbox, policy).InstallAsync(package, new AvaloniaUiRenderer(), new SearchTransport(plugin), token);
        await using var connection = new UiSessionConnection(peer, session);
        Require(session.Id != previousId && (await session.DispatchAsync(1, token)).State[0].Value.Integer == 1, "fresh session after crash");
        await RejectOldPatchAsync(session, previousId, token);
        var node = package.Nodes.Single(n => n.Primitive == UiPrimitive.TextBox).Id;
        await session.SetInputAsync(node, UiPropertyId.Text, UiValue.FromString("ap"), token);
        Require((await session.DispatchAsync(2, token)).State.Single(s => s.SlotId == 3).Value.Text == "apple, apricot", "reconnected remote search");
    }

    private static async Task RejectOldPatchAsync(UiSession fresh, Guid previousId, CancellationToken token)
    {
        try
        {
            await fresh.ApplyPatchAsync(new UiStatePatch(previousId, (await fresh.SnapshotAsync(token)).Version, []), token);
            throw new InvalidOperationException("Old session patch was accepted.");
        }
        catch (UiValidationException) { }
    }

    private static void Require(bool condition, string operation)
    { if (!condition) { throw new InvalidOperationException("UI smoke failed: " + operation); } }
}
