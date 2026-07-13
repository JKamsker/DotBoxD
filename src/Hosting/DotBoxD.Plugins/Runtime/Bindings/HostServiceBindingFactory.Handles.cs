using System.Reflection;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Hosting.Execution;

internal static partial class HostServiceBindingFactory
{
    public static BindingDescriptor CreateHandleBinding(
        MethodInfo factoryInterfaceMethod,
        MethodInfo factoryTargetMethod,
        object factoryTarget,
        MethodInfo handleInterfaceMethod,
        HostBindingAttribute binding)
    {
        var payloadType = UnwrapReturnType(handleInterfaceMethod.ReturnType);
        var parameters = HandleParameters(factoryInterfaceMethod, handleInterfaceMethod);
        var returnType = payloadType is null ? SandboxType.Unit : ServerExtensionSandboxTypeOf(payloadType);
        var effects = DeclaredEffects(handleInterfaceMethod, returnType, binding);
        var id = HostBindingId(handleInterfaceMethod.DeclaringType!, handleInterfaceMethod, binding);
        var isAsync = IsTaskLike(factoryInterfaceMethod.ReturnType) ||
            binding.IsAsync ||
            IsTaskLike(handleInterfaceMethod.ReturnType);

        return CreateHandleDescriptor(
            factoryInterfaceMethod,
            factoryTargetMethod,
            factoryTarget,
            handleInterfaceMethod,
            payloadType,
            new HandleBindingMetadata(id, binding.Capability, effects, isAsync),
            parameters,
            returnType);
    }

    public static BindingDescriptor CreateHandleBinding(
        MethodInfo factoryInterfaceMethod,
        MethodInfo factoryTargetMethod,
        object factoryTarget,
        MethodInfo handleInterfaceMethod,
        HostCapabilityAttribute capability)
    {
        var payloadType = UnwrapReturnType(handleInterfaceMethod.ReturnType);
        var parameters = HandleParameters(factoryInterfaceMethod, handleInterfaceMethod);
        var returnType = payloadType is null ? SandboxType.Unit : ServerExtensionSandboxTypeOf(payloadType);
        var effects = DeclaredEffects(handleInterfaceMethod, returnType, capability);
        var id = HostBindingRoute(handleInterfaceMethod.DeclaringType!, handleInterfaceMethod);
        var isAsync = IsTaskLike(factoryInterfaceMethod.ReturnType) || IsTaskLike(handleInterfaceMethod.ReturnType);

        return CreateHandleDescriptor(
            factoryInterfaceMethod,
            factoryTargetMethod,
            factoryTarget,
            handleInterfaceMethod,
            payloadType,
            new HandleBindingMetadata(id, capability.Capability, effects, isAsync),
            parameters,
            returnType);
    }

    private static SandboxType[] HandleParameters(
        MethodInfo factoryInterfaceMethod,
        MethodInfo handleInterfaceMethod)
        => factoryInterfaceMethod.GetParameters()
            .Concat(handleInterfaceMethod.GetParameters())
            .Select(HostBindingParameterSandboxTypeOf)
            .ToArray();

    private static BindingDescriptor CreateHandleDescriptor(
        MethodInfo factoryInterfaceMethod,
        MethodInfo factoryTargetMethod,
        object factoryTarget,
        MethodInfo handleInterfaceMethod,
        Type? payloadType,
        HandleBindingMetadata metadata,
        IReadOnlyList<SandboxType> parameters,
        SandboxType returnType)
    {
        var factoryCallTarget = new HostServiceCallTarget(factoryTargetMethod);
        var handleCallTarget = new HostServiceCallTarget(handleInterfaceMethod);

        return CreateDescriptor(
            metadata.Id,
            parameters,
            returnType,
            metadata.Effects,
            metadata.Capability,
            metadata.IsAsync,
            (context, args, cancellationToken) =>
                InvokeHandleAsync(
                    context,
                    args,
                    cancellationToken,
                    metadata,
                    factoryInterfaceMethod,
                    factoryCallTarget,
                    factoryTarget,
                    handleCallTarget,
                    payloadType));
    }

    private static async ValueTask<SandboxValue> InvokeHandleAsync(
        SandboxContext context,
        IReadOnlyList<SandboxValue> args,
        CancellationToken cancellationToken,
        HandleBindingMetadata metadata,
        MethodInfo factoryInterfaceMethod,
        HostServiceCallTarget factoryCallTarget,
        object factoryTarget,
        HostServiceCallTarget handleCallTarget,
        Type? payloadType)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = DateTimeOffset.UtcNow;
        var factoryValues = ConvertArguments(factoryCallTarget.ParameterTypes, args, startIndex: 0);
        var factoryResult = factoryCallTarget.Invoke(factoryTarget, factoryValues);
        var handle = await factoryCallTarget.ReadReturnAsync(factoryResult, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Host service factory '{factoryInterfaceMethod.Name}' returned null.");
        cancellationToken.ThrowIfCancellationRequested();
        var handleValues = ConvertArguments(handleCallTarget.ParameterTypes, args, factoryCallTarget.ParameterTypes.Length);
        var result = handleCallTarget.Invoke(handle, handleValues);
        var payload = await handleCallTarget.ReadReturnAsync(result, cancellationToken).ConfigureAwait(false);
        var auditValue = factoryValues.Length > 0 ? factoryValues[0] : handleValues.Length > 0 ? handleValues[0] : null;
        WriteAudit(context, metadata.Id, metadata.Capability, metadata.Effects, startedAt, auditValue);
        return payloadType is null
            ? SandboxValue.Unit
            : KernelRpcMarshaller.ToSandboxValue(payload, payloadType);
    }

    private readonly record struct HandleBindingMetadata(
        string Id,
        string Capability,
        SandboxEffect Effects,
        bool IsAsync);
}
