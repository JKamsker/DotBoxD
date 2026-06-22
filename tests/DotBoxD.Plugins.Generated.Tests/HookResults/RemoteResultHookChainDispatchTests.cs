using DotBoxD.Abstractions;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Generated.Tests.HookResults;

public sealed partial class RemoteResultHookChainTests
{
    [Fact]
    public async Task Remote_RegisterLocal_uses_hook_point_timeout_defaults_for_inferred_fire_async()
    {
        var faults = new List<ResultHookFault>();
        using var server = PluginServer.Create(
            defaultPolicy: TestPolicies.Chain(),
            onResultHookFault: faults.Add);
        var localHandlers = new RemoteLocalHandlerRegistry();
        var never = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Hooks.On<RemoteDamageContext>().ConfigureResultDispatch(
            ResultHookDispatchOptions<RemoteDamageResult>.FailClosedAfter(
                TimeSpan.FromMilliseconds(100),
                new RemoteDamageResult(true, "timeout", -2)));
        var registry = RemoteRegistry(server, localHandlers, (_, _, _) => new ValueTask<byte[]>(never.Task));

        RemoteDamagePlugin.ConfigureLocal(registry);
        var result = await server.Hooks.FireAsync(new RemoteDamageContext(12));

        Assert.Equal(-2, result!.Value.Damage);
        Assert.IsType<TimeoutException>(Assert.Single(faults).Exception);
    }

    [Fact]
    public async Task Remote_RegisterLocal_remote_cancellation_is_fault_not_timeout()
    {
        var faults = new List<ResultHookFault>();
        using var server = PluginServer.Create(
            defaultPolicy: TestPolicies.Chain(),
            onResultHookFault: faults.Add);
        var localHandlers = new RemoteLocalHandlerRegistry();
        var registry = RemoteRegistry(
            server,
            localHandlers,
            (_, _, _) => throw new OperationCanceledException("remote transport canceled"));

        RemoteDamagePlugin.ConfigureLocal(registry);
        var options = ResultHookDispatchOptions<RemoteDamageResult>.FailClosedAfter(
            TimeSpan.FromSeconds(30),
            new RemoteDamageResult(true, "timeout", -1));
        var result = await server.Hooks.FireAsync<RemoteDamageContext, RemoteDamageResult>(
            new RemoteDamageContext(12),
            options);

        Assert.Null(result);
        Assert.IsType<OperationCanceledException>(Assert.Single(faults).Exception);
    }

    [Fact]
    public async Task Remote_RegisterLocal_encodes_nullable_result_fields()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var localHandlers = new RemoteLocalHandlerRegistry();
        var registry = new RemoteHookRegistry(
            async package =>
            {
                var kernel = await server.InstallAsync(package).ConfigureAwait(false);
                server.Hooks.On<RemoteOptionalContext>().UseProjectingResult(
                    kernel,
                    package.Manifest.PluginId,
                    typeof(RemoteOptionalResult),
                    (id, payload, token) => localHandlers.DispatchResultAsync(
                        id,
                        payload.ToArray(),
                        new HookContext(new InMemoryPluginMessageSink(), token),
                        token),
                    Assert.Single(package.Manifest.Subscriptions).Priority);
                return package.Manifest.PluginId;
            },
            localHandlers);

        RemoteOptionalPlugin.ConfigureLocal(registry);

        var missing = await server.Hooks.FireAsync(new RemoteOptionalContext(0));
        var present = await server.Hooks.FireAsync(new RemoteOptionalContext(7));

        Assert.Null(missing!.Value.Damage);
        Assert.Equal(7, present!.Value.Damage);
    }

    [Fact]
    public async Task Remote_result_hook_json_round_trip_preserves_sandbox_metadata()
    {
        var imported = PluginPackageJsonSerializer.Import(
            PluginPackageJsonSerializer.Export(CaptureSandboxPackage()));
        var subscription = Assert.Single(imported.Manifest.Subscriptions);

        Assert.Equal("global::DotBoxD.Plugins.Generated.Tests.HookResults.RemoteDamageResult", subscription.ResultType);
        Assert.False(subscription.ResultLocalTerminal);
        Assert.Equal(11, subscription.Priority);

        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var kernel = await server.InstallAsync(imported);
        server.Hooks.On<RemoteDamageContext>().UseResult(
            kernel,
            typeof(RemoteDamageResult),
            subscription.Priority);

        var result = await server.Hooks.FireAsync(new RemoteDamageContext(12));

        Assert.Equal(36, result!.Value.Damage);
    }

    [Fact]
    public async Task Remote_result_hook_json_round_trip_preserves_local_metadata()
    {
        var imported = PluginPackageJsonSerializer.Import(
            PluginPackageJsonSerializer.Export(CaptureLocalPackage()));
        var subscription = Assert.Single(imported.Manifest.Subscriptions);
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var localHandlers = new RemoteLocalHandlerRegistry();
        var registry = RemoteRegistry(server, localHandlers);

        Assert.Equal("global::DotBoxD.Plugins.Generated.Tests.HookResults.RemoteDamageResult", subscription.ResultType);
        Assert.True(subscription.ResultLocalTerminal);
        Assert.Equal(7, subscription.Priority);

        registry.On<RemoteDamageContext>().UseGeneratedLocalResultChain(
            imported,
            (RemoteDamageContext context, HookContext _) => RemoteDamageResult.Ok().WithDamage(context.Damage * 2));
        var result = await server.Hooks.FireAsync(new RemoteDamageContext(12));

        Assert.Equal(24, result!.Value.Damage);
    }

    [Fact]
    public async Task Remote_RegisterLocal_result_request_receives_the_fire_token()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var localHandlers = new RemoteLocalHandlerRegistry();
        CancellationToken observed = default;
        var registry = RemoteRegistry(server, localHandlers, (id, payload, token) =>
        {
            observed = token;
            return localHandlers.DispatchResultAsync(
                id,
                payload.ToArray(),
                new HookContext(new InMemoryPluginMessageSink(), token),
                token);
        });
        using var cts = new CancellationTokenSource();

        RemoteDamagePlugin.ConfigureLocal(registry);
        var result = await server.Hooks.FireAsync(new RemoteDamageContext(12), cts.Token);

        Assert.Equal(cts.Token, observed);
        Assert.Equal(24, result!.Value.Damage);
    }
}
