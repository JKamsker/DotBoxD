using System.Reflection;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class RemoteTypedLocalChainParityTests
{
    [Fact]
    public void Typed_subscription_pipeline_local_chain_overloads_match_hooks()
        => Assert.Equal(
            UseGeneratedLocalChainSignatures(typeof(RemoteHookPipeline<,>)),
            UseGeneratedLocalChainSignatures(typeof(RemoteSubscriptionPipeline<,>)));

    [Fact]
    public void Typed_subscription_stage_local_chain_overloads_match_hooks()
        => Assert.Equal(
            UseGeneratedLocalChainSignatures(typeof(RemoteHookStage<,,>)),
            UseGeneratedLocalChainSignatures(typeof(RemoteSubscriptionStage<,,>)));

    private static string[] UseGeneratedLocalChainSignatures(Type type)
        => type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "UseGeneratedLocalChain")
            .Select(method => string.Join(", ", method.GetParameters().Select(parameter => Describe(parameter.ParameterType))))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string Describe(Type type)
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var definitionName = type.GetGenericTypeDefinition().FullName ?? type.Name;
        return definitionName + "<" + string.Join(", ", type.GetGenericArguments().Select(Describe)) + ">";
    }
}
