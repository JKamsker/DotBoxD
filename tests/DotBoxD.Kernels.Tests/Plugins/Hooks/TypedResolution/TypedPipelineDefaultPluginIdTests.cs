using System.Reflection;
using System.Runtime.ExceptionServices;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class TypedPipelineDefaultPluginIdTests
{
    [Theory]
    [InlineData("[Plugin]", "ImplicitIdKernel", "implicit-id")]
    [InlineData("[Plugin(null)]", "NullIdKernel", "null-id")]
    public async Task Typed_hook_and_subscription_use_resolve_generator_derived_plugin_ids(
        string pluginAttribute,
        string kernelTypeName,
        string expectedPluginId)
    {
        var generated = CreateGeneratedPackage(pluginAttribute, kernelTypeName);
        Assert.Equal(expectedPluginId, generated.Package.Manifest.PluginId);

        using var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(generated.Package);

        var hookPipeline = OpenPipeline(server.Hooks, generated.EventType);
        var hookException = Record.Exception(() => InvokeTypedUse(hookPipeline, generated.KernelType));

        var subscriptionPipeline = OpenPipeline(server.Subscriptions, generated.EventType);
        var subscriptionException = Record.Exception(() => InvokeTypedUse(subscriptionPipeline, generated.KernelType));

        Assert.True(
            hookException is null && subscriptionException is null,
            $"""
            Expected typed pipeline Use<TKernel>() to resolve generated plugin id '{expectedPluginId}'.
            Hook failure: {hookException}
            Subscription failure: {subscriptionException}
            """);
    }

    private static GeneratedPackage CreateGeneratedPackage(string pluginAttribute, string kernelTypeName)
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly($$"""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message);

            {{pluginAttribute}}
            public sealed partial class {{kernelTypeName}} : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        const string kernelSuffix = "Kernel";
        var packageTypeName = "Sample." + kernelTypeName[..^kernelSuffix.Length] + "PluginPackage";
        var factoryType = assembly.GetType(packageTypeName, throwOnError: true)!;
        var create = factoryType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        var package = Assert.IsType<PluginPackage>(InvokeAndUnwrap(() => create.Invoke(null, null)));

        return new GeneratedPackage(
            package,
            assembly.GetType("Sample.DamageEvent", throwOnError: true)!,
            assembly.GetType("Sample." + kernelTypeName, throwOnError: true)!);
    }

    private static object OpenPipeline(object registry, Type eventType)
    {
        var on = registry.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method =>
                method.Name == "On" &&
                method.IsGenericMethodDefinition &&
                method.GetGenericArguments().Length == 1 &&
                method.GetParameters().Length == 0);

        return InvokeAndUnwrap(() => on.MakeGenericMethod(eventType).Invoke(registry, null))!;
    }

    private static void InvokeTypedUse(object pipeline, Type kernelType)
    {
        var use = pipeline.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method =>
                method.Name == "Use" &&
                method.IsGenericMethodDefinition &&
                method.GetParameters().Length == 0);

        InvokeAndUnwrap(() => use.MakeGenericMethod(kernelType).Invoke(pipeline, null));
    }

    private static object? InvokeAndUnwrap(Func<object?> action)
    {
        try
        {
            return action();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private sealed record GeneratedPackage(PluginPackage Package, Type EventType, Type KernelType);
}
