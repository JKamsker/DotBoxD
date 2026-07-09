using System.Reflection;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class PipelineValidationTests
{
    private sealed record GuardEvent(int Value);

    private sealed record GuardContext(HookContext Raw);

    [Fact]
    public void Hook_pipeline_context_overloads_throw_for_null_inputs()
    {
        using var server = PluginServer.Create();
        var pipeline = server.Hooks.On<GuardEvent, GuardContext>(CreateContext);

        Assert.Throws<ArgumentNullException>(
            () => pipeline.Where((Func<GuardEvent, GuardContext, bool>)null!));
        Assert.Throws<ArgumentNullException>(
            () => pipeline.Where((Func<GuardEvent, GuardContext, ValueTask<bool>>)null!));
        Assert.Throws<ArgumentNullException>(
            () => pipeline.InvokeHostHandler((Func<GuardEvent, GuardContext, ValueTask>)null!));
        Assert.Throws<ArgumentNullException>(
            () => pipeline.InvokeHostHandler((Action<GuardEvent, GuardContext>)null!));
        Assert.Throws<ArgumentNullException>(() => pipeline.Use((InstalledKernel)null!));
    }

    [Fact]
    public void Subscription_pipeline_context_overloads_throw_for_null_delegates()
    {
        using var server = PluginServer.Create();
        var pipeline = server.Subscriptions.On<GuardEvent, GuardContext>(CreateContext);

        Assert.Throws<ArgumentNullException>(
            () => pipeline.Where((Func<GuardEvent, GuardContext, bool>)null!));
        Assert.Throws<ArgumentNullException>(
            () => pipeline.Where((Func<GuardEvent, GuardContext, ValueTask<bool>>)null!));
        Assert.Throws<ArgumentNullException>(
            () => pipeline.InvokeHostHandler((Func<GuardEvent, GuardContext, ValueTask>)null!));
        Assert.Throws<ArgumentNullException>(
            () => pipeline.InvokeHostHandler((Action<GuardEvent, GuardContext>)null!));
        Assert.Throws<ArgumentNullException>(() => pipeline.Use((InstalledKernel)null!));
    }

    [Fact]
    public void Element_only_filter_and_projection_surfaces_expose_ir_body_companions()
    {
        Type[] pipelineTypes =
        [
            typeof(HookPipeline<GuardEvent, GuardContext>),
            typeof(HookStage<GuardEvent, int, GuardContext>),
            typeof(SubscriptionPipeline<GuardEvent, GuardContext>),
            typeof(SubscriptionStage<GuardEvent, int, GuardContext>),
            typeof(RemoteHookPipeline<GuardEvent>),
            typeof(RemoteHookPipeline<GuardEvent, GuardContext>),
            typeof(RemoteHookStage<GuardEvent, int>),
            typeof(RemoteHookStage<GuardEvent, int, GuardContext>),
            typeof(RemoteSubscriptionPipeline<GuardEvent>),
            typeof(RemoteSubscriptionPipeline<GuardEvent, GuardContext>),
            typeof(RemoteSubscriptionStage<GuardEvent, int>),
            typeof(RemoteSubscriptionStage<GuardEvent, int, GuardContext>),
        ];

        foreach (var pipelineType in pipelineTypes)
        {
            AssertIrBodyCompanion(pipelineType, "Where", "filter", "irFilter");
            AssertIrBodyCompanion(pipelineType, "Select", "projection", "irProjection");
        }
    }

    [Fact]
    public void Pipeline_stage_surfaces_accept_handwritten_ir_companions()
    {
        using var server = PluginServer.Create();
        var eventFilter = Ir<GuardEvent, bool>(LoweredPipelineStepKind.Filter);
        var eventContextFilter = Ir<GuardEvent, GuardContext, bool>(LoweredPipelineStepKind.Filter);
        var eventHookContextFilter = Ir<GuardEvent, HookContext, bool>(LoweredPipelineStepKind.Filter);
        var eventProjection = Ir<GuardEvent, int>(LoweredPipelineStepKind.Projection);
        var eventContextProjection = Ir<GuardEvent, GuardContext, int>(LoweredPipelineStepKind.Projection);
        var eventHookContextProjection = Ir<GuardEvent, HookContext, int>(LoweredPipelineStepKind.Projection);
        var intFilter = Ir<int, bool>(LoweredPipelineStepKind.Filter);
        var intContextFilter = Ir<int, GuardContext, bool>(LoweredPipelineStepKind.Filter);
        var intHookContextFilter = Ir<int, HookContext, bool>(LoweredPipelineStepKind.Filter);
        var intProjection = Ir<int, string>(LoweredPipelineStepKind.Projection);
        var intContextProjection = Ir<int, GuardContext, string>(LoweredPipelineStepKind.Projection);
        var intHookContextProjection = Ir<int, HookContext, string>(LoweredPipelineStepKind.Projection);

        var hook = server.Hooks.On<GuardEvent, GuardContext>(CreateContext);
        hook.Where(e => e.Value > 0, eventFilter)
            .Where((e, _) => e.Value > 0, eventContextFilter);
        var hookStage = hook.Select(e => e.Value, eventProjection);
        hook.Select((e, _) => e.Value, eventContextProjection);
        hookStage.Where(value => value > 0, intFilter)
            .Where((value, _) => value > 0, intContextFilter)
            .Select(value => value.ToString(), intProjection)
            .Select((value, _) => value, Ir<string, GuardContext, string>(LoweredPipelineStepKind.Projection));
        hookStage.Select((value, _) => value.ToString(), intContextProjection);

        var subscription = server.Subscriptions.On<GuardEvent, GuardContext>(CreateContext);
        subscription.Where(e => e.Value > 0, eventFilter)
            .Where((e, _) => e.Value > 0, eventContextFilter);
        var subscriptionStage = subscription.Select(e => e.Value, eventProjection);
        subscription.Select((e, _) => e.Value, eventContextProjection);
        subscriptionStage.Where(value => value > 0, intFilter)
            .Where((value, _) => value > 0, intContextFilter)
            .Select(value => value.ToString(), intProjection)
            .Select((value, _) => value, Ir<string, GuardContext, string>(LoweredPipelineStepKind.Projection));
        subscriptionStage.Select((value, _) => value.ToString(), intContextProjection);

        var remoteHook = new RemoteHookPipeline<GuardEvent>(_ => ValueTask.FromResult("hook"));
        remoteHook.Where(e => e.Value > 0, eventFilter)
            .Where((e, _) => e.Value > 0, eventHookContextFilter);
        var remoteHookStage = remoteHook.Select(e => e.Value, eventProjection);
        remoteHook.Select((e, _) => e.Value, eventHookContextProjection);
        remoteHookStage.Where(value => value > 0, intFilter)
            .Where((value, _) => value > 0, intHookContextFilter)
            .Select(value => value.ToString(), intProjection)
            .Select((value, _) => value, Ir<string, HookContext, string>(LoweredPipelineStepKind.Projection));
        remoteHookStage.Select((value, _) => value.ToString(), intHookContextProjection);

        var remoteHookWithContext = new RemoteHookPipeline<GuardEvent, GuardContext>(
            _ => ValueTask.FromResult("hook-context"),
            CreateContext);
        remoteHookWithContext.Where(e => e.Value > 0, eventFilter)
            .Where((e, _) => e.Value > 0, eventContextFilter);
        var remoteHookContextStage = remoteHookWithContext.Select(e => e.Value, eventProjection);
        remoteHookWithContext.Select((e, _) => e.Value, eventContextProjection);
        remoteHookContextStage.Where(value => value > 0, intFilter)
            .Where((value, _) => value > 0, intContextFilter)
            .Select(value => value.ToString(), intProjection)
            .Select((value, _) => value, Ir<string, GuardContext, string>(LoweredPipelineStepKind.Projection));
        remoteHookContextStage.Select((value, _) => value.ToString(), intContextProjection);

        var remoteSubscription = new RemoteSubscriptionPipeline<GuardEvent>(_ => ValueTask.FromResult("subscription"));
        remoteSubscription.Where(e => e.Value > 0, eventFilter)
            .Where((e, _) => e.Value > 0, eventHookContextFilter);
        var remoteSubscriptionStage = remoteSubscription.Select(e => e.Value, eventProjection);
        remoteSubscription.Select((e, _) => e.Value, eventHookContextProjection);
        remoteSubscriptionStage.Where(value => value > 0, intFilter)
            .Where((value, _) => value > 0, intHookContextFilter)
            .Select(value => value.ToString(), intProjection)
            .Select((value, _) => value, Ir<string, HookContext, string>(LoweredPipelineStepKind.Projection));
        remoteSubscriptionStage.Select((value, _) => value.ToString(), intHookContextProjection);

        var remoteSubscriptionWithContext = new RemoteSubscriptionPipeline<GuardEvent, GuardContext>(
            _ => ValueTask.FromResult("subscription-context"),
            CreateContext);
        remoteSubscriptionWithContext.Where(e => e.Value > 0, eventFilter)
            .Where((e, _) => e.Value > 0, eventContextFilter);
        var remoteSubscriptionContextStage = remoteSubscriptionWithContext.Select(e => e.Value, eventProjection);
        remoteSubscriptionWithContext.Select((e, _) => e.Value, eventContextProjection);
        remoteSubscriptionContextStage.Where(value => value > 0, intFilter)
            .Where((value, _) => value > 0, intContextFilter)
            .Select(value => value.ToString(), intProjection)
            .Select((value, _) => value, Ir<string, GuardContext, string>(LoweredPipelineStepKind.Projection));
        remoteSubscriptionContextStage.Select((value, _) => value.ToString(), intContextProjection);
    }

    [Fact]
    public void Local_terminal_validation_distinguishes_map_interfaces_from_lists()
    {
        var map = PackageWithProjection("map");
        var list = PackageWithProjection("list");

        LocalTerminalManifestValidator.ValidateRunLocal<IDictionary<string, int>>(map);
        Assert.Throws<InvalidOperationException>(
            () => LocalTerminalManifestValidator.ValidateRunLocal<IDictionary<string, int>>(list));
    }

    [Fact]
    public void Local_terminal_validation_accepts_read_only_dictionary_map_projection()
    {
        var map = PackageWithProjection("map");

        // Regression: a RunLocal map projection whose handler parameter is IReadOnlyDictionary<,> (or a concrete
        // Dictionary<,>, which implements it) must validate as a map — the decoder materializes a Dictionary
        // assignable to that parameter. Previously only IDictionary / IDictionary<,> were recognized, so such a
        // package failed ValidateRunLocal before its callback was ever registered.
        LocalTerminalManifestValidator.ValidateRunLocal<IReadOnlyDictionary<string, int>>(map);
        LocalTerminalManifestValidator.ValidateRunLocal<Dictionary<string, int>>(map);
    }

    private static GuardContext CreateContext(HookContext context) => new(context);

    private static IRFunc<TInput, TOutput> Ir<TInput, TOutput>(LoweredPipelineStepKind kind)
        => IRFunc<TInput, TOutput>.FromStep(Step(kind, typeof(TInput), typeof(TOutput)));

    private static IRFunc<TInput, TContext, TOutput> Ir<TInput, TContext, TOutput>(
        LoweredPipelineStepKind kind)
        => IRFunc<TInput, TContext, TOutput>.FromStep(Step(kind, typeof(TInput), typeof(TOutput)));

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
            [new Parameter("$dotboxd.current", SandboxType.I32)],
            [],
            value,
            [],
            []);
    }

    private static string TypeName(Type type)
        => type.FullName ?? type.Name;

    private static void AssertIrBodyCompanion(
        Type declaringType,
        string methodName,
        string sourceParameterName,
        string irParameterName)
    {
        var methods = declaringType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(
            candidate => candidate.Name == methodName &&
                candidate.GetParameters().Any(parameter => parameter.Name == irParameterName))
            .ToArray();

        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            var irParameter = Assert.Single(method.GetParameters(), parameter => parameter.Name == irParameterName);
            var attribute = Assert.Single(irParameter.GetCustomAttributes<IRBodyOfAttribute>());

            Assert.Equal(sourceParameterName, attribute.ParameterName);
            Assert.True(irParameter.HasDefaultValue);
            Assert.Null(irParameter.DefaultValue);
            Assert.True(irParameter.ParameterType.IsGenericType);
            Assert.Contains(
                irParameter.ParameterType.GetGenericTypeDefinition(),
                new[] { typeof(IRFunc<,>), typeof(IRFunc<,,>) });
        }
    }

    private static PluginPackage PackageWithProjection(string projectedType)
    {
        var package = FireDamagePluginPackage.Create();
        return package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions =
                [
                    new HookSubscriptionManifest(typeof(GuardEvent).FullName!, "GuardKernel")
                    {
                        LocalTerminal = true,
                        ProjectedType = projectedType
                    }
                ]
            }
        };
    }
}
