using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts;

/// <summary>
/// Guards the public fluent lowering contract: lowered methods expose an ordinary delegate plus an explicit
/// IR companion parameter that the source generator fills through interceptors.
/// </summary>
public sealed class PipelineExplicitIrContractTests
{
    private static readonly (Type Type, PipelineTransport Transport)[] Surfaces =
    {
        (typeof(HookPipeline<,>), PipelineTransport.Local),
        (typeof(HookStage<,,>), PipelineTransport.Local),
        (typeof(SubscriptionPipeline<,>), PipelineTransport.Local),
        (typeof(SubscriptionStage<,,>), PipelineTransport.Local),
        (typeof(RemoteHookPipeline<>), PipelineTransport.Remote),
        (typeof(RemoteHookPipeline<,>), PipelineTransport.Remote),
        (typeof(RemoteHookStage<,>), PipelineTransport.Remote),
        (typeof(RemoteHookStage<,,>), PipelineTransport.Remote),
        (typeof(RemoteSubscriptionPipeline<>), PipelineTransport.Remote),
        (typeof(RemoteSubscriptionPipeline<,>), PipelineTransport.Remote),
        (typeof(RemoteSubscriptionStage<,>), PipelineTransport.Remote),
        (typeof(RemoteSubscriptionStage<,,>), PipelineTransport.Remote),
    };

    public static TheoryData<Type, PipelineTransport> SurfaceTypes()
    {
        var data = new TheoryData<Type, PipelineTransport>();
        foreach (var (type, transport) in Surfaces)
        {
            data.Add(type, transport);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SurfaceTypes))]
    public void Pipeline_surface_type_is_marked_with_expected_transport(Type type, PipelineTransport transport)
    {
        var surface = type.GetCustomAttribute<PipelineSurfaceAttribute>(inherit: false);

        Assert.True(surface is not null, $"{type.Name} is missing [PipelineSurface].");
        Assert.Equal(transport, surface!.Transport);
    }

    [Fact]
    public void Pipeline_surface_attribute_rejects_unknown_transport()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new PipelineSurfaceAttribute((PipelineTransport)42));

        Assert.Equal("transport", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(SurfaceTypes))]
    public void Lowerable_stage_methods_expose_ir_func_companion(Type type, PipelineTransport _)
    {
        foreach (var method in LowerableStageMethods(type))
        {
            var parameters = method.GetParameters();
            Assert.True(parameters.Length >= 2, $"{Display(method)} must expose an IR companion parameter.");
            Assert.True(IsIrFunc(parameters[1].ParameterType), $"{Display(method)} companion must be IRFunc.");
            AssertIrBodyOf(method, parameters[1], parameters[0].Name);
        }
    }

    [Theory]
    [MemberData(nameof(SurfaceTypes))]
    public void Lowered_terminal_methods_expose_ir_kernel_companion(Type type, PipelineTransport transport)
    {
        foreach (var method in LoweredTerminalMethods(type, transport))
        {
            var parameters = method.GetParameters();
            Assert.True(parameters.Length >= 2, $"{Display(method)} must expose an IR companion parameter.");
            Assert.Same(typeof(IRKernel), parameters[1].ParameterType);
            Assert.True(parameters[1].HasDefaultValue, $"{Display(method)} IR companion should be optional.");
            AssertIrBodyOf(method, parameters[1], parameters[0].Name);
        }
    }

    [Fact]
    public void Plugin_server_invoke_methods_expose_ir_invocation_companion()
    {
        var methods = typeof(IPluginServer<>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => method.Name == "InvokeAsync");

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var lambdaIndex = parameters[0].ParameterType.IsSubclassOf(typeof(Delegate)) ? 0 : 1;
            var irParameter = parameters[lambdaIndex + 1];

            Assert.True(IsIrInvocation(irParameter.ParameterType), $"{Display(method)} companion must be IRInvocation.");
            Assert.True(irParameter.HasDefaultValue, $"{Display(method)} IR companion should be optional.");
            AssertIrBodyOf(method, irParameter, parameters[lambdaIndex].Name);
        }
    }

    [Fact]
    public void Registries_are_not_marked_as_surfaces()
    {
        foreach (var registry in new[]
                 {
                     typeof(HookRegistry), typeof(RemoteHookRegistry),
                     typeof(SubscriptionRegistry), typeof(RemoteSubscriptionRegistry),
                 })
        {
            Assert.Null(registry.GetCustomAttribute<PipelineSurfaceAttribute>(inherit: false));
        }
    }

    private static IEnumerable<MethodInfo> LowerableStageMethods(Type type)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => method.Name is "Where" or "Select")
            .Where(static method => method.GetParameters() is [{ ParameterType: { } parameterType }, ..] &&
                IsDelegate(parameterType) &&
                !ReturnsValueTask(parameterType));

    private static IEnumerable<MethodInfo> LoweredTerminalMethods(Type type, PipelineTransport transport)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name is "Run" or "Register" or "RegisterLocal" ||
                (transport == PipelineTransport.Remote && method.Name == "RunLocal"))
            .Where(static method => method.GetParameters() is [{ ParameterType: { } parameterType }, ..] &&
                IsDelegate(parameterType));

    private static void AssertIrBodyOf(MethodInfo method, ParameterInfo irParameter, string? delegateParameterName)
    {
        var bodyOf = irParameter.GetCustomAttribute<IRBodyOfAttribute>(inherit: false);

        Assert.True(bodyOf is not null, $"{Display(method)} companion is missing [IRBodyOf].");
        Assert.Equal(delegateParameterName, bodyOf!.ParameterName);
    }

    private static bool IsIrFunc(Type type)
        => type.IsGenericType &&
            (type.GetGenericTypeDefinition() == typeof(IRFunc<,>) ||
             type.GetGenericTypeDefinition() == typeof(IRFunc<,,>));

    private static bool IsIrInvocation(Type type)
        => type.IsGenericType &&
            (type.GetGenericTypeDefinition() == typeof(IRInvocation<,>) ||
             type.GetGenericTypeDefinition() == typeof(IRInvocation<,,>));

    private static bool IsDelegate(Type type)
        => typeof(Delegate).IsAssignableFrom(type);

    private static bool ReturnsValueTask(Type delegateType)
    {
        var returnType = delegateType.GetMethod("Invoke")?.ReturnType;
        return returnType == typeof(ValueTask) ||
            (returnType?.IsGenericType == true && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>));
    }

    private static string Display(MethodInfo method)
        => method.DeclaringType!.Name + "." + method.Name;
}
