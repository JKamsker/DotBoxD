using System.Reflection;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Server.Reentrancy;

public sealed class RemoteProjectingPipelineDisposalTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public async Task Hook_UseProjecting_rejects_registration_after_owner_server_disposal()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var pipeline = server.Hooks.On<DamageEvent>();

        server.Dispose();

        var exception = Record.Exception(() => pipeline.UseProjecting(kernel, "remote-hook", NoopPush));
        var handlerCount = HandlerCount(pipeline);

        AssertDisposedWithoutRegistration(exception, handlerCount);
    }

    [Fact]
    public async Task Subscription_UseProjecting_rejects_registration_after_owner_server_disposal()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var pipeline = server.Subscriptions.On<DamageEvent>();

        server.Dispose();

        var exception = Record.Exception(() => pipeline.UseProjecting(kernel, "remote-subscription", NoopPush));
        var handlerCount = HandlerCount(pipeline);

        AssertDisposedWithoutRegistration(exception, handlerCount);
    }

    [Fact]
    public async Task Hook_UseProjectingResult_rejects_registration_after_owner_server_disposal()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var package = ResultLocalTerminalPackage();
        var kernel = await server.InstallAsync(package);
        var eventName = Assert.Single(package.Manifest.Subscriptions).Event;
        var pipeline = server.Hooks.On(new ResultDamageEventAdapter(eventName));

        server.Dispose();

        RemoteLocalResultRequest request = (_, _, _) => new ValueTask<byte[]>([]);
        var exception = Record.Exception(() =>
            pipeline.UseProjectingResult(kernel, "remote-result", typeof(ResultDamageResult), request));
        var hasResultHandlers = HasResultHandlers(pipeline);

        Assert.True(
            exception is ObjectDisposedException,
            "Expected ObjectDisposedException after the server disposed the pipeline owner, " +
            $"but got {exception?.GetType().Name ?? "no exception"}; result handlers installed: {hasResultHandlers}.");
        Assert.False(hasResultHandlers);
    }

    private static ValueTask NoopPush(
        string subscriptionId,
        ReadOnlyMemory<byte> projectedValue,
        CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    private static void AssertDisposedWithoutRegistration(Exception? exception, int handlerCount)
    {
        Assert.True(
            exception is ObjectDisposedException,
            "Expected ObjectDisposedException after the server disposed the pipeline owner, " +
            $"but got {exception?.GetType().Name ?? "no exception"}; handler count: {handlerCount}.");
        Assert.Equal(0, handlerCount);
    }

    private static int HandlerCount(object pipeline)
    {
        var handlerSetField = pipeline.GetType().GetField(
            "_handlerSet",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(handlerSetField);

        var handlerSet = handlerSetField.GetValue(pipeline);
        Assert.NotNull(handlerSet);

        var snapshotProperty = handlerSet.GetType().GetProperty(
            "Snapshot",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(snapshotProperty);

        var snapshot = Assert.IsAssignableFrom<Array>(snapshotProperty.GetValue(handlerSet));
        return snapshot.Length;
    }

    private static bool HasResultHandlers(object pipeline)
    {
        var resultHooksField = pipeline.GetType().GetField(
            "_resultHooks",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(resultHooksField);

        var resultHooks = resultHooksField.GetValue(pipeline);
        Assert.NotNull(resultHooks);

        var hasHandlersProperty = resultHooks.GetType().GetProperty(
            "HasHandlers",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(hasHandlersProperty);

        return Assert.IsType<bool>(hasHandlersProperty.GetValue(resultHooks));
    }

    private static PluginPackage ResultLocalTerminalPackage()
    {
        const string pluginId = "remote-result-disposal";
        const string eventName = "test.damage";
        var parameters = DamageEventAdapter.Instance.Parameters;

        return PluginPackage.Create(
            new PluginManifest(
                pluginId,
                $"IEventKernel<{eventName}>",
                ExecutionMode.Interpreted,
                ["Cpu"],
                [],
                [
                    new HookSubscriptionManifest(eventName, "ResultKernel")
                    {
                        ResultLocalTerminal = true,
                        ResultType = typeof(ResultDamageResult).FullName
                    }
                ]),
            new SandboxModule(
                pluginId,
                SemVersion.One,
                SemVersion.One,
                [],
                [
                    new SandboxFunction(
                        "ShouldHandle",
                        IsEntrypoint: true,
                        parameters,
                        SandboxType.Bool,
                        [new ReturnStatement(new LiteralExpression(SandboxValue.FromBool(true), Span), Span)]),
                    new SandboxFunction(
                        "Handle",
                        IsEntrypoint: true,
                        parameters,
                        SandboxType.Unit,
                        [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)])
                ],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["callbackSubscriptionId"] = "callback-red-test",
                    ["kernel"] = "ResultKernel",
                    ["pluginId"] = pluginId
                }),
            new KernelEntrypoints("ShouldHandle", "Handle"));
    }

    [Hook("test.damage", typeof(ResultDamageResult))]
    private sealed record ResultDamageEvent(string DamageType, int Amount, string TargetId);

    private readonly record struct ResultDamageResult(bool Success, string? Reason, int Damage) : IHookResult;

    private sealed class ResultDamageEventAdapter(string eventName) : IPluginEventValueWriter<ResultDamageEvent>
    {
        public string EventName { get; } = eventName;

        public IReadOnlyList<Parameter> Parameters => DamageEventAdapter.Instance.Parameters;

        public int EventValueCount => DamageEventAdapter.Instance.EventValueCount;

        public IReadOnlyList<SandboxValue> ToSandboxValues(ResultDamageEvent e)
            => [
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromInt32(e.Amount),
                SandboxValue.FromString(e.TargetId)
            ];

        public SandboxValue ToSandboxValue(ResultDamageEvent e, int index)
            => index switch
            {
                0 => SandboxValue.FromString(e.DamageType),
                1 => SandboxValue.FromInt32(e.Amount),
                2 => SandboxValue.FromString(e.TargetId),
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };

        public void CopySandboxValues(ResultDamageEvent e, SandboxValue[] destination, int destinationIndex)
        {
            destination[destinationIndex] = SandboxValue.FromString(e.DamageType);
            destination[destinationIndex + 1] = SandboxValue.FromInt32(e.Amount);
            destination[destinationIndex + 2] = SandboxValue.FromString(e.TargetId);
        }
    }
}
