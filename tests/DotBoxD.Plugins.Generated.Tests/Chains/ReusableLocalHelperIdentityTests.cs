using DotBoxD.Abstractions;
using DotBoxD.Plugins.Generated.Tests.HookResults;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins.Generated.Tests;

public sealed class ReusableLocalHelperIdentityTests
{
    [Fact]
    public async Task RunLocal_helper_invoked_twice_keeps_both_hook_handlers()
    {
        using var h = new RunLocalHarness<ChainAggroEvent>();
        var first = new List<string>();
        var second = new List<string>();

        InstallRunLocalHelper(h.Hooks, first, "first:");
        InstallRunLocalHelper(h.Hooks, second, "second:");

        var kernels = LocalTerminalKernels(h.Server).ToArray();
        Assert.Equal(2, kernels.Length);
        Assert.Single(kernels.Select(k => k.Manifest.PluginId).Distinct(StringComparer.Ordinal));
        Assert.Equal(2, kernels.Select(k => k.InstallId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, kernels.Select(k => k.CallbackSubscriptionId).Distinct(StringComparer.Ordinal).Count());

        await h.PublishAsync(new ChainAggroEvent("m-1", 3));

        Assert.Equal(["first:m-1"], first);
        Assert.Equal(["second:m-1"], second);
    }

    [Fact]
    public async Task RunLocal_helper_invoked_twice_keeps_both_subscription_handlers()
    {
        using var h = new SubscriptionHarness<ChainAggroEvent>();
        var received = new List<string>();
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        void Signal()
        {
            if (Interlocked.Increment(ref count) == 2)
            {
                delivered.TrySetResult();
            }
        }

        InstallSubscriptionHelper(h.Subscriptions, received, "first:", Signal);
        InstallSubscriptionHelper(h.Subscriptions, received, "second:", Signal);

        var kernels = LocalTerminalKernels(h.Server).ToArray();
        Assert.Equal(2, kernels.Length);
        Assert.Single(kernels.Select(k => k.Manifest.PluginId).Distinct(StringComparer.Ordinal));
        Assert.Equal(2, kernels.Select(k => k.InstallId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, kernels.Select(k => k.CallbackSubscriptionId).Distinct(StringComparer.Ordinal).Count());

        h.Publish(new ChainAggroEvent("m-2", 2));
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["first:m-2", "second:m-2"], received.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RegisterLocal_helper_invoked_twice_keeps_both_result_handlers()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var localHandlers = new RemoteLocalHandlerRegistry();
        var registry = new RemoteHookRegistry(
            async package =>
            {
                var kernel = await server.InstallAsync(package).ConfigureAwait(false);
                var subscriptionId = kernel.CallbackSubscriptionId ?? kernel.Manifest.PluginId;
                var subscription = Assert.Single(package.Manifest.Subscriptions);
                server.Hooks.On<RemoteDamageContext>().UseProjectingResult(
                    kernel,
                    subscriptionId,
                    typeof(RemoteDamageResult),
                    (id, payload, token) => localHandlers.DispatchResultAsync(
                        id,
                        payload.ToArray(),
                        new HookContext(new InMemoryPluginMessageSink(), token),
                        token),
                    subscription.Priority);
                return subscriptionId;
            },
            localHandlers);

        InstallResultLocalHelper(registry, 11);
        InstallResultLocalHelper(registry, 22);

        var kernels = LocalTerminalKernels(server).ToArray();
        Assert.Equal(2, kernels.Length);
        Assert.Single(kernels.Select(k => k.Manifest.PluginId).Distinct(StringComparer.Ordinal));
        Assert.Equal(2, kernels.Select(k => k.InstallId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, kernels.Select(k => k.CallbackSubscriptionId).Distinct(StringComparer.Ordinal).Count());

        var damages = new List<int>();
        foreach (var kernel in kernels)
        {
            var response = await localHandlers.DispatchResultAsync(
                kernel.CallbackSubscriptionId!,
                Encode(new RemoteDamageContext(1)),
                new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None));
            var result = KernelRpcBinaryCodec.DecodeValue(response);
            damages.Add(result.Items[2].Int32Value);
        }

        Assert.Equal([11, 22], damages.Order());
    }

    private static void InstallRunLocalHelper(
        RemoteHookRegistry hooks,
        List<string> received,
        string prefix)
        => hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.MonsterId)
            .RunLocal((id, _) => received.Add(prefix + id));

    private static void InstallSubscriptionHelper(
        RemoteSubscriptionRegistry subscriptions,
        List<string> received,
        string prefix,
        Action signal)
        => subscriptions.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.MonsterId)
            .RunLocal((id, _) =>
            {
                lock (received)
                {
                    received.Add(prefix + id);
                }

                signal();
            });

    private static void InstallResultLocalHelper(RemoteHookRegistry hooks, int damage)
        => hooks.On<RemoteDamageContext>()
            .Where(ctx => ctx.Damage > 0)
            .RegisterLocal((ctx, _) => RemoteDamageResult.Ok().WithDamage(damage), priority: 0);

    private static IEnumerable<InstalledKernel> LocalTerminalKernels(PluginServer server)
        => server.Kernels.Snapshot().Where(k => Assert.Single(k.Manifest.Subscriptions) is
        { LocalTerminal: true } or { ResultLocalTerminal: true });

    private static byte[] Encode<T>(T value)
    {
        var sandboxValue = KernelRpcMarshaller.ToSandboxValue(value, typeof(T));
        return KernelRpcBinaryCodec.EncodeValue(sandboxValue);
    }
}
