using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginServerDisposalRegistrationTests
{
    [Fact]
    public void RegisterEventAdapter_after_dispose_throws_object_disposed()
    {
        using var server = PluginServer.Create();
        server.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => server.RegisterEventAdapter(TestEventAdapter.Instance));
    }

    [Fact]
    public async Task Install_racing_dispose_never_leaves_an_unrevoked_kernel()
    {
        // Dispose revokes a snapshot of the registry; an install that registers after that snapshot must not
        // return a live kernel bound to a disposed host.
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var server = PluginAddendumTestPolicies.CreateServer();
            using var start = new ManualResetEventSlim();
            var installs = Enumerable.Range(0, 3)
                .Select(index => Task.Run(async () =>
                {
                    start.Wait();
                    return await TryInstallAsync(server, index);
                }))
                .ToArray();
            var spin = iteration % 50 * 20;
            var dispose = Task.Run(() =>
            {
                start.Wait();
                Thread.SpinWait(spin);
                server.Dispose();
            });

            start.Set();
            await dispose;
            var kernels = await Task.WhenAll(installs);

            foreach (var kernel in kernels)
            {
                Assert.True(kernel is null || kernel.IsRevoked, $"iteration {iteration}: install returned a live kernel.");
            }

            Assert.Empty(server.Kernels.Snapshot());
        }
    }

    private static async Task<InstalledKernel?> TryInstallAsync(PluginServer server, int index)
    {
        try
        {
            return index == 2
                ? (await server.InstallPoolAsync(FireDamagePluginPackage.Create(), degreeOfParallelism: 1)).Kernels[0]
                : await server.InstallAsync(FireDamagePluginPackage.Create());
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private sealed record TestEvent(string TargetId);

    private sealed class TestEventAdapter : IPluginEventAdapter<TestEvent>
    {
        public static TestEventAdapter Instance { get; } = new();

        public string EventName => nameof(TestEvent);
        public IReadOnlyList<Parameter> Parameters { get; } = [new("targetId", SandboxType.String)];
        public IReadOnlyList<SandboxValue> ToSandboxValues(TestEvent e) => [SandboxValue.FromString(e.TargetId)];
    }
}
