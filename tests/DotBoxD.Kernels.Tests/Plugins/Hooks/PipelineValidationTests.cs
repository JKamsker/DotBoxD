using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Subscriptions;
using System.Reflection;

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

    private static void AssertIrBodyCompanion(
        Type declaringType,
        string methodName,
        string sourceParameterName,
        string irParameterName)
    {
        var method = Assert.Single(
            declaringType.GetMethods(BindingFlags.Public | BindingFlags.Instance),
            candidate => candidate.Name == methodName &&
                candidate.GetParameters().Any(parameter => parameter.Name == irParameterName));
        var irParameter = Assert.Single(method.GetParameters(), parameter => parameter.Name == irParameterName);
        var attribute = Assert.Single(irParameter.GetCustomAttributes<IRBodyOfAttribute>());

        Assert.Equal(sourceParameterName, attribute.ParameterName);
        Assert.True(irParameter.HasDefaultValue);
        Assert.Null(irParameter.DefaultValue);
        Assert.True(irParameter.ParameterType.IsGenericType);
        Assert.Equal(typeof(IRFunc<,>), irParameter.ParameterType.GetGenericTypeDefinition());
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
