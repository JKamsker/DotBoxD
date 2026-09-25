using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.RemoteLocal;

public sealed class UseProjectingResultCancellationSurpriseTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public async Task FireAsync_does_not_start_remote_request_after_event_writer_cancels_dispatch()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var package = ResultLocalTerminalPackage();
        var kernel = await server.InstallAsync(package);
        var eventName = Assert.Single(package.Manifest.Subscriptions).Event;
        using var cancellation = new CancellationTokenSource();
        var requestCount = 0;

        RemoteLocalResultRequest request = (_, _, token) =>
        {
            requestCount++;
            return ValueTask.FromCanceled<byte[]>(token);
        };
        server.Hooks.On(new CancelingDamageEventAdapter(eventName, cancellation))
            .UseProjectingResult(kernel, "result-local", typeof(DamageResult), request);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await server.Hooks.FireAsync<DamageEvent, DamageResult>(
                new DamageEvent(7),
                cancellation.Token));

        await Task.Yield();
        Assert.Equal(0, requestCount);
    }

    private static PluginPackage ResultLocalTerminalPackage()
    {
        const string pluginId = "projecting-result-cancellation";
        const string eventName = "test.damage";
        var parameters = new[] { new Parameter("damage", SandboxType.I32) };

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
                        ResultType = typeof(DamageResult).FullName
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
                    ["callbackSubscriptionId"] = "result-local",
                    ["kernel"] = "ResultKernel",
                    ["pluginId"] = pluginId
                }),
            new KernelEntrypoints("ShouldHandle", "Handle"));
    }

    [Hook("test.damage", typeof(DamageResult))]
    private sealed record DamageEvent(int Damage);

    private readonly record struct DamageResult(bool Success, string? Reason, int Damage) : IHookResult;

    private sealed class CancelingDamageEventAdapter(string eventName, CancellationTokenSource cancellation)
        : IPluginEventValueWriter<DamageEvent>
    {
        public string EventName { get; } = eventName;

        public IReadOnlyList<Parameter> Parameters { get; } = [new("damage", SandboxType.I32)];

        public int EventValueCount => 1;

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e) => [SandboxValue.FromInt32(e.Damage)];

        public SandboxValue ToSandboxValue(DamageEvent e, int index)
            => index == 0
                ? SandboxValue.FromInt32(e.Damage)
                : throw new ArgumentOutOfRangeException(nameof(index));

        public void CopySandboxValues(DamageEvent e, SandboxValue[] destination, int destinationIndex)
        {
            destination[destinationIndex] = SandboxValue.FromInt32(e.Damage);
            cancellation.Cancel();
        }
    }
}
