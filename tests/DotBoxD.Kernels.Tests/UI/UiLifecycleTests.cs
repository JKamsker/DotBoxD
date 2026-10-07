using DotBoxD.UI;

namespace DotBoxD.Kernels.Tests.UI;

public sealed class UiLifecycleTests
{
    [Fact]
    public void Disposed_sessions_release_renderer_and_connection_adapter_references()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var (session, renderer, remote) = CreateDisposedSession(sandbox);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(renderer.IsAlive);
        Assert.False(remote.IsAlive);
        GC.KeepAlive(session);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (DotBoxD.UI.Runtime.UiSession, WeakReference, WeakReference) CreateDisposedSession(SandboxHost sandbox)
    {
        var renderer = new RecordingUiRenderer();
        var remote = TestUiTransport.Echo();
        var session = UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, remote).AsTask().GetAwaiter().GetResult();
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return (session, new WeakReference(renderer), new WeakReference(remote));
    }

    [Fact]
    public async Task Validation_and_kernel_signature_checks_precede_materialization()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var package = UiTestFixture.Counter();
        foreach (var invalid in new[]
        {
            package with { FormatVersion = 999 },
            package with { Kernels = [package.Kernels[0] with { InputSlotId = 2 }, package.Kernels[1]] },
            package with { Events = [package.Events[0] with { OutputSlotId = 2 }, package.Events[1]] },
            package with { Kernels = [package.Kernels[0] with { Entrypoint = "missing" }, package.Kernels[1]] }
        })
        {
            var renderer = new RecordingUiRenderer();
            await Assert.ThrowsAsync<UiValidationException>(async () =>
                await UiTestFixture.Host(sandbox).InstallAsync(invalid, renderer, TestUiTransport.Echo()));
            Assert.Equal(0, renderer.Materializations);
            Assert.Equal(1, renderer.Disposals);
        }
    }

    [Fact]
    public async Task Remote_routes_require_an_explicit_adapter()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new RecordingUiRenderer();
        await Assert.ThrowsAsync<UiValidationException>(async () =>
            await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer));
        Assert.Equal(0, renderer.Materializations);
    }

    [Fact]
    public async Task Renderer_install_failure_releases_resources()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new RecordingUiRenderer { ThrowOnMaterialize = true };
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, TestUiTransport.Echo()));
        Assert.Equal(1, renderer.Disposals);
    }

    [Fact]
    public async Task Renderer_update_failure_disconnects_the_session()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new RecordingUiRenderer { ThrowOnUpdate = true };
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, TestUiTransport.Echo());
        await Assert.ThrowsAnyAsync<Exception>(async () => await session.DispatchAsync(1));
        Assert.True(session.IsDisconnected);
        Assert.Equal(1, renderer.Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await session.SnapshotAsync());
    }

    [Fact]
    public async Task Transport_disconnect_is_contained_and_releases_the_renderer()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var remote = new TestUiTransport((_, _) => throw new IOException("plugin disconnected"));
        var renderer = new RecordingUiRenderer();
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, remote);
        await Assert.ThrowsAsync<IOException>(async () => await session.DispatchAsync(2));
        Assert.True(session.IsDisconnected);
        Assert.Equal(1, renderer.Disposals);
    }

    [Fact]
    public async Task Reinstall_issues_new_identity_and_disposal_is_idempotent()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new RecordingUiRenderer();
        var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, TestUiTransport.Echo());
        var old = await session.SnapshotAsync();
        await Task.WhenAll(session.DisposeAsync().AsTask(), session.DisposeAsync().AsTask());
        Assert.Equal(1, renderer.Disposals);
        await using var replacement = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), new RecordingUiRenderer(), TestUiTransport.Echo());
        Assert.NotEqual(old.SessionId, replacement.Id);
        await Assert.ThrowsAsync<UiValidationException>(async () => await replacement.ApplyPatchAsync(new UiStatePatch(old.SessionId, 0, [])));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await session.DispatchAsync(1));
    }
}
