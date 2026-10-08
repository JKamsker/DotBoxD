using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Conformance;

internal static class UiRendererConformance
{
    public static UiPackage Package() => new(1, 1,
        [new(1, UiPrimitive.Stack, [2, 3, 4, 5, 6], [new(UiPropertyId.Enabled, StateSlotId: 5), new(UiPropertyId.Visible, StateSlotId: 6)]),
         new(2, UiPrimitive.Text, [], [new(UiPropertyId.Text, StateSlotId: 3)]),
         new(3, UiPrimitive.Button, [], [new(UiPropertyId.Text, UiValue.FromString("Increment"))]),
         new(4, UiPrimitive.TextBox, [], [new(UiPropertyId.Text, StateSlotId: 2, TwoWay: true)]),
         new(5, UiPrimitive.Button, [], [new(UiPropertyId.Text, UiValue.FromString("Search"))]),
         new(6, UiPrimitive.Items, [], [new(UiPropertyId.Items, StateSlotId: 4)])],
        [new(1, UiValue.FromInt32(0)), new(2, UiValue.FromString("")), new(3, UiValue.FromString("")),
         new(4, UiValue.FromItems([new("a", "Alpha"), new("b", "Beta")])),
         new(5, UiValue.FromBoolean(true)), new(6, UiValue.FromBoolean(true))],
        [new(1, """
         {"id":"ui-conformance","version":"1.0.0","functions":[{"id":"main","visibility":"entrypoint",
         "parameters":[{"name":"value","type":"I32"}],"returnType":"I32",
         "body":[{"op":"return","value":{"op":"add","left":{"var":"value"},"right":{"i32":1}}}]}]}
         """, "main", 1)],
        [new(1, 3, UiEventKind.Click, UiEventTarget.LocalKernel, KernelId: 1, OutputSlotId: 1),
         new(2, 5, UiEventKind.Click, UiEventTarget.Remote, RemoteEndpointId: 7)], [7]);

    // The exact same package, mutation sequence and assertions run on fake, Avalonia and Blazor.
    public static async Task RunAsync(IUiRenderer renderer)
    {
        using var sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings());
        var transport = new Remote();
        var package = UiPackageJson.Import(UiPackageJson.Export(Package(), new UiPolicy()), new UiPolicy());
        var session = await new UiHost(sandbox, SandboxPolicyBuilder.Create().Build()).InstallAsync(package, renderer, transport);
        try
        {
            await session.DispatchAsync(1);
            Assert.Equal(0, transport.Calls);
            await session.SetInputAsync(4, UiPropertyId.Text, UiValue.FromString("ap"));
            var remoteResult = await session.DispatchAsync(2);
            Assert.Equal(3, remoteResult.Version);
            Assert.Equal(1, transport.Calls);
            Assert.Equal(2, transport.Last!.EventId);
            Assert.Equal(5, transport.Last.NodeId);
            Assert.Equal(7, transport.Last.EndpointId);
            Assert.Equal("ap", transport.Last.Snapshot.State[1].Value.Text);
            Assert.Equal("apple", remoteResult.State[2].Value.Text);
            var reordered = await session.ApplyPatchAsync(new UiStatePatch(session.Id, 3,
                [new(4, UiValue.FromItems([new("b", "Updated"), new("a", "Alpha")]))]));
            Assert.Equal(new[] { "b", "a" }, reordered.State[3].Value.Items.Select(i => i.Key));
            await Assert.ThrowsAsync<UiValidationException>(() => session.ApplyPatchAsync(new UiStatePatch(session.Id, 3, [])).AsTask());
            await Assert.ThrowsAsync<UiValidationException>(() => session.ApplyPatchAsync(new UiStatePatch(Guid.NewGuid(), 4, [])).AsTask());
            await session.ApplyPatchAsync(new UiStatePatch(session.Id, 4, [new(5, UiValue.FromBoolean(false))]));
            await Assert.ThrowsAsync<UiValidationException>(() => session.DispatchAsync(1).AsTask());
            await Assert.ThrowsAsync<UiValidationException>(() => session.SetInputAsync(4, UiPropertyId.Text, UiValue.FromString("blocked")).AsTask());
            await session.ApplyPatchAsync(new UiStatePatch(session.Id, 5, [new(5, UiValue.FromBoolean(true)), new(6, UiValue.FromBoolean(false))]));
            await Assert.ThrowsAsync<UiValidationException>(() => session.DispatchAsync(2).AsTask());
            var final = await session.SnapshotAsync();
            Assert.Equal(6, final.Version);
            Assert.Equal(1, final.State[0].Value.Integer);
            Assert.Equal("ap", final.State[1].Value.Text);
        }
        finally { await session.DisposeAsync(); }
        Assert.True(session.IsDisconnected);
        Assert.Null(session.Renderer);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.DispatchAsync(1).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ApplyPatchAsync(new UiStatePatch(session.Id, 6, [])).AsTask());
    }

    private sealed class Remote : IUiRemoteTransport
    {
        public int Calls { get; private set; }
        public UiRemoteEvent? Last { get; private set; }
        public ValueTask<UiStatePatch> DispatchAsync(UiRemoteEvent message, CancellationToken cancellationToken)
        {
            Calls++;
            Last = message;
            return ValueTask.FromResult(new UiStatePatch(message.Snapshot.SessionId, message.Snapshot.Version,
                [new(3, UiValue.FromString("apple"))]));
        }
    }
}
