using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class RemotePipelineIrRuntimeTests
{
    private sealed record GuardEvent(int Value);

    [Fact]
    public void Remote_hook_stage_requires_explicit_ir_companion()
    {
        var pipeline = new RemoteHookPipeline<GuardEvent>(_ => ValueTask.FromResult("hook"));

        var missingFilter = Assert.Throws<ArgumentNullException>(
            () => pipeline.Where(_ => true));
        var missingProjection = Assert.Throws<ArgumentNullException>(
            () => pipeline.Select(_ => 1));

        Assert.Equal("irFilter", missingFilter.ParamName);
        Assert.Equal("irProjection", missingProjection.ParamName);
    }

    [Fact]
    public void Remote_subscription_stage_requires_explicit_ir_companion()
    {
        var pipeline = new RemoteSubscriptionPipeline<GuardEvent>(_ => ValueTask.FromResult("subscription"));

        var missingFilter = Assert.Throws<ArgumentNullException>(
            () => pipeline.Where(_ => true));
        var missingProjection = Assert.Throws<ArgumentNullException>(
            () => pipeline.Select(_ => 1));

        Assert.Equal("irFilter", missingFilter.ParamName);
        Assert.Equal("irProjection", missingProjection.ParamName);
    }

    [Fact]
    public void Remote_hook_runlocal_installs_runtime_composed_package_from_stage_ir()
    {
        PluginPackage? installed = null;
        var handlers = new RemoteLocalHandlerRegistry();
        var pipeline = new RemoteHookPipeline<GuardEvent>(
            package =>
            {
                installed = package;
                return ValueTask.FromResult("hook");
            },
            handlers);

        pipeline
            .Where(_ => true, Ir<GuardEvent, bool>(LoweredPipelineStepKind.Filter))
            .RunLocal(_ => { }, IRKernel.FromPackage(TerminalPackage("hook")));

        var package = AssertInstalled(installed);
        var shouldHandle = Function(package, "ShouldHandle");
        Assert.IsType<IfStatement>(Assert.Single(shouldHandle.Body.SkipLast(1)));
        Assert.Equal("runtime-hook", package.Module.Id);
        Assert.NotNull(package.CallbackSubscriptionId);
    }

    [Fact]
    public void Remote_subscription_runlocal_installs_runtime_composed_package_from_stage_ir()
    {
        PluginPackage? installed = null;
        var handlers = new RemoteLocalHandlerRegistry();
        var pipeline = new RemoteSubscriptionPipeline<GuardEvent>(
            package =>
            {
                installed = package;
                return ValueTask.FromResult("subscription");
            },
            handlers);

        pipeline
            .Where(_ => true, Ir<GuardEvent, bool>(LoweredPipelineStepKind.Filter))
            .RunLocal(_ => { }, IRKernel.FromPackage(TerminalPackage("subscription")));

        var package = AssertInstalled(installed);
        var shouldHandle = Function(package, "ShouldHandle");
        Assert.IsType<IfStatement>(Assert.Single(shouldHandle.Body.SkipLast(1)));
        Assert.Equal("runtime-subscription", package.Module.Id);
        Assert.NotNull(package.CallbackSubscriptionId);
    }

    private static IRFunc<TInput, TOutput> Ir<TInput, TOutput>(LoweredPipelineStepKind kind)
        => IRFunc<TInput, TOutput>.FromStep(Step(kind, typeof(TInput), typeof(TOutput)));

    private static LoweredPipelineStep Step(LoweredPipelineStepKind kind, Type input, Type output)
    {
        var span = new SourceSpan(1, 1);
        Expression value = kind == LoweredPipelineStepKind.Filter
            ? new LiteralExpression(SandboxValue.FromBool(true), span)
            : new VariableExpression("$dotboxd.current", span);

        return new LoweredPipelineStep(
            kind,
            TypeName(input),
            TypeName(output),
            [new Parameter("$dotboxd.current", KernelRpcMarshaller.SandboxTypeOf(input))],
            [],
            value,
            [],
            []);
    }

    private static PluginPackage TerminalPackage(string id)
    {
        var eventType = KernelRpcMarshaller.SandboxTypeOf(typeof(GuardEvent));
        var entrypoints = new KernelEntrypoints("ShouldHandle", "Handle");
        var manifest = new PluginManifest(
            id,
            "tests",
            ExecutionMode.Interpreted,
            [],
            [],
            [
                new HookSubscriptionManifest(typeof(GuardEvent).FullName!, id)
                {
                    LocalTerminal = true,
                    ProjectedType = TypeName(typeof(GuardEvent)),
                }
            ]);

        var module = new SandboxModule(
            "runtime-" + id,
            new SemVersion(1, 0, 0),
            new SemVersion(1, 0, 0),
            [],
            [
                new SandboxFunction(
                    "ShouldHandle",
                    IsEntrypoint: true,
                    [new Parameter("input", eventType)],
                    SandboxType.Bool,
                    [new ReturnStatement(new LiteralExpression(SandboxValue.FromBool(false), new SourceSpan(1, 1)), new SourceSpan(1, 1))]),
                new SandboxFunction(
                    "Handle",
                    IsEntrypoint: true,
                    [new Parameter("input", eventType)],
                    eventType,
                    [new ReturnStatement(new VariableExpression("input", new SourceSpan(1, 1)), new SourceSpan(1, 1))]),
            ],
            new Dictionary<string, string>());

        return PluginPackage.Create(manifest, module, entrypoints);
    }

    private static PluginPackage AssertInstalled(PluginPackage? package)
    {
        Assert.NotNull(package);
        return package;
    }

    private static SandboxFunction Function(PluginPackage package, string id)
        => package.Module.Functions.Single(function => string.Equals(function.Id, id, StringComparison.Ordinal));

    private static string TypeName(Type type)
        => type.FullName ?? type.Name;
}
