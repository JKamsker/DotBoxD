using System.Reflection;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;
using DotBoxD.Shared.HostBindings;

namespace DotBoxD.Hosting.Execution;

internal static partial class HostServiceBindingFactory
{
    public static BindingDescriptor CreateBinding(
        MethodInfo interfaceMethod,
        MethodInfo targetMethod,
        object target,
        HostCapabilityAttribute? capability,
        HostBindingAttribute? binding)
    {
        var payloadType = UnwrapReturnType(interfaceMethod.ReturnType);
        var parameters = interfaceMethod.GetParameters()
            .Select(HostBindingParameterSandboxTypeOf)
            .ToArray();
        var returnType = payloadType is null ? SandboxType.Unit : ServerExtensionSandboxTypeOf(payloadType);
        var (id, requiredCapability, effects, isAsync) =
            MethodBindingMetadata(interfaceMethod, returnType, capability, binding);
        var callTarget = new HostServiceCallTarget(targetMethod);

        return CreateDescriptor(
            id,
            parameters,
            returnType,
            effects,
            requiredCapability,
            isAsync || IsTaskLike(interfaceMethod.ReturnType),
            (context, args, cancellationToken) =>
                InvokeAsync(context, args, cancellationToken, id, requiredCapability, effects, callTarget, target, payloadType));
    }

    public static Type? UnwrapReturnType(Type type)
        => HostServiceCallTarget.UnwrapReturnType(type);

    private static SandboxType ServerExtensionSandboxTypeOf(Type type)
    {
        KernelRpcMarshaller.RejectNullableValueTypesForServerExtension(type);
        return KernelRpcMarshaller.SandboxTypeOf(type);
    }

    private static SandboxType HostBindingParameterSandboxTypeOf(ParameterInfo parameter)
    {
        if (parameter.ParameterType.IsByRef)
        {
            var method = (MethodInfo)parameter.Member;
            throw new InvalidOperationException(
                $"HostBinding method '{method.DeclaringType?.FullName}.{method.Name}' declares unsupported " +
                $"{ByRefParameterKind(parameter)} parameter '{parameter.Name}'; " +
                "by-reference HostBinding parameters are not supported.");
        }

        return ServerExtensionSandboxTypeOf(parameter.ParameterType);
    }

    private static string ByRefParameterKind(ParameterInfo parameter)
        => parameter.IsOut ? "out" : parameter.IsIn ? "in" : "ref";

    private static BindingDescriptor CreateDescriptor(
        string id,
        IReadOnlyList<SandboxType> parameters,
        SandboxType returnType,
        SandboxEffect effects,
        string capability,
        bool isAsync,
        BindingInvoker binding)
    {
        var safety = (effects & SandboxEffect.HostStateWrite) != SandboxEffect.None
            ? BindingSafety.SideEffectingExternal
            : BindingSafety.ReadOnlyExternal;

        return new BindingDescriptor(
            id,
            SemVersion.One,
            parameters,
            returnType,
            effects,
            capability,
            BindingCostModel.Fixed(BaseFuel(returnType)),
            AuditLevel.PerResource,
            safety,
            binding,
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { })
        {
            IsAsync = isAsync
        };
    }

    private static async ValueTask<SandboxValue> InvokeAsync(
        SandboxContext context,
        IReadOnlyList<SandboxValue> args,
        CancellationToken cancellationToken,
        string bindingId,
        string capability,
        SandboxEffect effects,
        HostServiceCallTarget callTarget,
        object target,
        Type? payloadType)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = DateTimeOffset.UtcNow;
        var values = ConvertArguments(callTarget.ParameterTypes, args, startIndex: 0);
        var result = callTarget.Invoke(target, values);
        var payload = await callTarget.ReadReturnAsync(result, cancellationToken).ConfigureAwait(false);
        var value = MarshalReturn(payload, payloadType);
        WriteAudit(context, bindingId, capability, effects, startedAt, values.Length > 0 ? values[0] : null);
        return value;
    }

    private static SandboxValue MarshalReturn(object? payload, Type? payloadType)
        => payloadType is null ? SandboxValue.Unit : KernelRpcMarshaller.ToSandboxValue(payload, payloadType);

    private static object?[] ConvertArguments(
        Type[] parameterTypes,
        IReadOnlyList<SandboxValue> args,
        int startIndex)
    {
        if (parameterTypes.Length == 0)
        {
            return Array.Empty<object?>();
        }

        var values = new object?[parameterTypes.Length];
        for (var i = 0; i < parameterTypes.Length; i++)
        {
            values[i] = KernelRpcMarshaller.FromSandboxValue(args[startIndex + i], parameterTypes[i]);
        }

        return values;
    }

    private static void WriteAudit(
        SandboxContext context,
        string bindingId,
        string capability,
        SandboxEffect effects,
        DateTimeOffset startedAt,
        object? firstArgument)
    {
        context.Checkpoint();
        var resourceId = firstArgument is string id ? $"entity:{id}" : bindingId;
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "BindingCall",
            startedAt,
            true,
            BindingId: bindingId,
            CapabilityId: capability,
            Effect: effects & (SandboxEffect.HostStateRead | SandboxEffect.HostStateWrite),
            ResourceId: resourceId,
            Fields: context.BindingAuditFields("host-service", startedAt)));
    }

    private static bool IsTaskLike(Type type)
        => HostServiceCallTarget.IsTaskLike(type);

    private static SandboxEffect DeclaredEffects(
        MethodInfo method,
        SandboxType returnType,
        HostCapabilityAttribute capability)
    {
        var declaredEffects = HostBindingMetadataRules.ValidateDeclaredEffects(
            (long)capability.Effects,
            ReturnAllocates(returnType),
            $"Host capability on '{method.DeclaringType?.FullName}.{method.Name}'");
        var effects = SandboxEffect.Cpu;
        if ((declaredEffects & HostBindingMetadataRules.Allocates) == HostBindingMetadataRules.Allocates)
        {
            effects |= SandboxEffect.Alloc;
        }

        return (declaredEffects & HostBindingMetadataRules.HostStateWrite) == HostBindingMetadataRules.HostStateWrite
            ? effects | SandboxEffect.HostStateWrite
            : effects | SandboxEffect.HostStateRead;
    }

    private static bool ReturnAllocates(SandboxType type)
        => HostBindingMetadataRules.ReturnAllocatesSandboxTypeName(type.Name);

    private static long BaseFuel(SandboxType returnType) => ReturnAllocates(returnType) ? 3 : 2;

    private static (string Id, string Capability, SandboxEffect Effects, bool IsAsync) MethodBindingMetadata(
        MethodInfo method,
        SandboxType returnType,
        HostCapabilityAttribute? capability,
        HostBindingAttribute? binding)
    {
        if (binding is not null)
        {
            return (
                HostBindingId(method.DeclaringType!, method, binding),
                binding.Capability,
                DeclaredEffects(method, returnType, binding),
                binding.IsAsync);
        }

        if (capability is null)
        {
            throw new InvalidOperationException(
                $"Host service method '{method.DeclaringType?.FullName}.{method.Name}' must declare [HostCapability] on its service contract.");
        }

        return (
            HostBindingRoute(method.DeclaringType!, method),
            capability.Capability,
            DeclaredEffects(method, returnType, capability),
            IsAsync: false);
    }

    private static SandboxEffect DeclaredEffects(
        MethodInfo method,
        SandboxType returnType,
        HostBindingAttribute binding)
    {
        var effects = binding.Effects;
        if (!effects.ContainsOnlyKnownBits())
        {
            throw new InvalidOperationException(
                $"Host binding method '{method.DeclaringType?.FullName}.{method.Name}' declares unknown effects.");
        }

        var access = effects & (SandboxEffect.HostStateRead | SandboxEffect.HostStateWrite);
        if (access is not SandboxEffect.HostStateRead and not SandboxEffect.HostStateWrite)
        {
            throw new InvalidOperationException(
                $"Host binding method '{method.DeclaringType?.FullName}.{method.Name}' must declare exactly one of HostStateRead or HostStateWrite.");
        }

        var allocates = (effects & SandboxEffect.Alloc) == SandboxEffect.Alloc;
        var returnAllocates = ReturnAllocates(returnType);
        if (allocates != returnAllocates)
        {
            throw new InvalidOperationException(
                returnAllocates
                    ? $"Host binding method '{method.DeclaringType?.FullName}.{method.Name}' must declare Alloc because its return shape allocates."
                    : $"Host binding method '{method.DeclaringType?.FullName}.{method.Name}' must not declare Alloc because its return shape does not allocate.");
        }

        return effects;
    }

    private static string HostBindingRoute(Type type, MethodInfo method)
        => HostBindingMetadataRules.BindingId(type.Namespace, type.Name, method.Name);

    private static string HostBindingId(Type type, MethodInfo method, HostBindingAttribute binding)
        => binding.IsAutoBinding ? HostBindingRoute(type, method) : binding.BindingId;
}
