using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;

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
