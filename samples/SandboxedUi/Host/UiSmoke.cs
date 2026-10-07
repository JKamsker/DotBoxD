using Avalonia;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.UI;
using DotBoxD.UI.Avalonia;
using DotBoxD.UI.Runtime;
using Examples.SandboxedUi.Contracts;

namespace Examples.SandboxedUi.Host;

internal static class UiSmoke
{
    public static async Task RunAsync(IUiPlugin plugin, UiPackage package, Func<Task> crash, CancellationToken token)
    {
        using var sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings().AddBinding(ScoreBinding()));
        var kernelPolicy = SandboxPolicyBuilder.Create().Grant("game.score.read", new Dictionary<string, string>(), SandboxEffect.HostStateRead).Build();
        var host = new UiHost(sandbox, kernelPolicy, new UiPolicy { MaxPackageBytes = 128 * 1024 });
        var renderer = new AvaloniaUiRenderer();
        var remote = new SearchTransport(plugin);
        await using var session = await host.InstallAsync(package, renderer, remote, token);
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
        await crash();
        try
        {
            await session.DispatchAsync(2, token);
            throw new InvalidOperationException("Disconnected worker was accepted.");
        }
        catch (Exception) when (session.IsDisconnected && renderer.IsDisposed) { }
        await using var fresh = await host.InstallAsync(package, new AvaloniaUiRenderer(), remote, token);
        Require(fresh.Id != session.Id && (await fresh.DispatchAsync(1, token)).State[0].Value.Integer == 1, "fresh session after crash");
        await RejectOldPatchAsync(fresh, session.Id, token);
    }

    public static async Task ReconnectAsync(IUiPlugin plugin, UiPackage package, CancellationToken token)
    {
        using var sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings().AddBinding(ScoreBinding()));
        var policy = SandboxPolicyBuilder.Create().Grant("game.score.read", new Dictionary<string, string>(), SandboxEffect.HostStateRead).Build();
        await using var session = await new UiHost(sandbox, policy).InstallAsync(package, new AvaloniaUiRenderer(), new SearchTransport(plugin), token);
        var node = package.Nodes.Single(n => n.Primitive == UiPrimitive.TextBox).Id;
        await session.SetInputAsync(node, UiPropertyId.Text, UiValue.FromString("ap"), token);
        Require((await session.DispatchAsync(2, token)).State.Single(s => s.SlotId == 3).Value.Text == "apple, apricot", "reconnected remote search");
    }

    private static async Task RejectOldPatchAsync(UiSession fresh, Guid previousId, CancellationToken token)
    {
        try
        {
            await fresh.ApplyPatchAsync(new UiStatePatch(previousId, 0, []), token);
            throw new InvalidOperationException("Old session patch was accepted.");
        }
        catch (UiValidationException) { }
    }

    private static BindingDescriptor ScoreBinding() => new("ui.game.score", SemVersion.One, [], SandboxType.I32,
        SandboxEffect.Cpu | SandboxEffect.HostStateRead, "game.score.read", BindingCostModel.Fixed(1), AuditLevel.PerResource,
        BindingSafety.ReadOnlyExternal, InvokeScore,

        CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"), GrantValidator: static (_, _) => { });

    private static ValueTask<SandboxValue> InvokeScore(SandboxContext context, IReadOnlyList<SandboxValue> arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var started = DateTimeOffset.UtcNow;
        context.Audit.Write(new SandboxAuditEvent(context.RunId, "BindingCall", started, true,
            BindingId: "ui.game.score", CapabilityId: "game.score.read", Effect: SandboxEffect.HostStateRead,
            ResourceId: "score", Fields: context.BindingAuditFields("ui-score", started)));
        return ValueTask.FromResult(SandboxValue.FromInt32(42));
    }

    private static void Require(bool condition, string operation)
    { if (!condition) { throw new InvalidOperationException("UI smoke failed: " + operation); } }
}
