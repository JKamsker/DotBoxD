using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Generation;

internal static partial class ProxyGenerator
{
    /// <summary>
    /// Builds the call to <c>_invoker.InvokeAsync</c> or <c>_invoker.InvokeOnInstanceAsync</c>.
    /// For sub-service-returning methods, the wire response type is always
    /// <c>ServiceHandle</c>; the caller wraps it in a generated sub-proxy. The emitted
    /// expression branches on <c>_instanceId</c> so the same proxy class can serve both
    /// the top-level and the nested-instance call paths.
    /// </summary>
    private static (string Invocation, System.Collections.Generic.List<(string HandleName, string ReservedName)>? Reservations) BuildClientInvocation(
        StringBuilder sb,
        ServiceModel service,
        MethodModel method,
        string ctArg,
        GeneratedLocalNames locals,
        CancellationToken ct,
        string indent = "            ",
        bool checkCancellationBeforeStreamReserve = true)
    {
        var hasReturn = NamingHelpers.HasReturnValue(method.ReturnKind);
        var returnType = ClientReturnType(method);
        var requestParameters = ProxyGenerationHelpers.GetRequestParameters(method.Parameters, ct);
        var streamSetup = ProxyStreamSetupEmitter.Emit(
            sb,
            method,
            requestParameters,
            ctArg,
            checkCancellationBeforeStreamReserve,
            locals,
            ct,
            indent);
        var streamArgument = streamSetup.ArgumentName;
        var useStreamAwareTaskValueInvocation =
            streamArgument is not null && (method.ReturnKind is MethodReturnKind.ValueTask or MethodReturnKind.ValueTaskOf);
        var svc = service.ServiceName;
        var rpc = method.RpcName;
        var singletonMethod = GetInvokerMethod(
            method.ReturnKind,
            isInstanceScoped: false,
            useStreamAwareTaskValueInvocation);
        var instanceMethod = GetInvokerMethod(
            method.ReturnKind,
            isInstanceScoped: true,
            useStreamAwareTaskValueInvocation);

        var call = ProxyClientInvocationCallBuilder.Build(
            method,
            requestParameters,
            streamSetup.Handles,
            returnType,
            hasReturn,
            svc,
            rpc,
            ctArg,
            streamArgument,
            ct);
        var invocation =
            $"(this._instanceId is null ? this._invoker.{singletonMethod}{call.TypeArgs}({call.SingletonArguments}) : this._invoker.{instanceMethod}{call.TypeArgs}({call.InstanceArguments}))";
        invocation = WrapStreamAwareValueTaskInvocation(invocation, method.ReturnKind, returnType, useStreamAwareTaskValueInvocation);

        return (invocation, streamSetup.Reservations);
    }

    private static string? ClientReturnType(MethodModel method)
    {
        if (NamingHelpers.IsSubServiceReturn(method.ReturnKind))
        {
            return GetServiceHandleType(method);
        }

        return method.UnwrappedReturnType is null
            ? null
            : ProxyGenerationHelpers.GetWireType(method.UnwrappedReturnType);
    }

    private static string GetServiceHandleType(MethodModel method) =>
        method.SubService?.AllowsNull == true
            ? ServicesGeneratorTypeNames.NullableOf(ServicesGeneratorTypeNames.GlobalServiceHandle)
            : ServicesGeneratorTypeNames.GlobalServiceHandle;

    private static string WrapStreamAwareValueTaskInvocation(
        string invocation,
        MethodReturnKind returnKind,
        string? returnType,
        bool useStreamAwareTaskValueInvocation)
    {
        if (!useStreamAwareTaskValueInvocation)
        {
            return invocation;
        }

        return returnKind == MethodReturnKind.ValueTask
            ? $"new {ServicesGeneratorTypeNames.GlobalValueTask}({invocation})"
            : $"new {ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalValueTask, returnType!)}({invocation})";
    }

    private static string GetInvokerMethod(
        MethodReturnKind returnKind,
        bool isInstanceScoped,
        bool useStreamAwareTaskValueInvocation = false)
    {
        if (TryGetStreamingInvoker(returnKind, isInstanceScoped, out var streamingInvoker))
        {
            return streamingInvoker;
        }

        if (TryGetValueTaskInvoker(
                returnKind,
                isInstanceScoped,
                useStreamAwareTaskValueInvocation,
                out var valueTaskInvoker))
        {
            return valueTaskInvoker;
        }

        return StandardInvoker(isInstanceScoped);
    }

    private static bool TryGetStreamingInvoker(
        MethodReturnKind returnKind,
        bool isInstanceScoped,
        out string invoker)
    {
        if (NamingHelpers.IsStreamReturn(returnKind))
        {
            invoker = isInstanceScoped
                ? ServicesGeneratorMemberNames.RpcInvoker.InvokeStreamOnInstanceAsync
                : ServicesGeneratorMemberNames.RpcInvoker.InvokeStreamAsync;
            return true;
        }

        if (NamingHelpers.IsPipeReturn(returnKind))
        {
            invoker = isInstanceScoped
                ? ServicesGeneratorMemberNames.RpcInvoker.InvokePipeOnInstanceAsync
                : ServicesGeneratorMemberNames.RpcInvoker.InvokePipeAsync;
            return true;
        }

        if (NamingHelpers.IsAsyncEnumerableReturn(returnKind))
        {
            invoker = AsyncEnumerableInvoker(returnKind, isInstanceScoped);
            return true;
        }

        invoker = string.Empty;
        return false;
    }

    private static string AsyncEnumerableInvoker(MethodReturnKind returnKind, bool isInstanceScoped)
    {
        var eager = returnKind == MethodReturnKind.TaskOfAsyncEnumerable ||
            returnKind == MethodReturnKind.ValueTaskOfAsyncEnumerable;
        if (isInstanceScoped)
        {
            return eager
                ? ServicesGeneratorMemberNames.RpcInvoker.InvokeAsyncEnumerableOnInstanceAsync
                : ServicesGeneratorMemberNames.RpcInvoker.InvokeAsyncEnumerableOnInstance;
        }

        return eager
            ? ServicesGeneratorMemberNames.RpcInvoker.InvokeAsyncEnumerableAsync
            : ServicesGeneratorMemberNames.RpcInvoker.InvokeAsyncEnumerable;
    }

    private static bool TryGetValueTaskInvoker(
        MethodReturnKind returnKind,
        bool isInstanceScoped,
        bool useStreamAwareTaskValueInvocation,
        out string invoker)
    {
        if (returnKind is MethodReturnKind.ValueTask or MethodReturnKind.ValueTaskOf &&
            !useStreamAwareTaskValueInvocation)
        {
            invoker = isInstanceScoped
                ? ServicesGeneratorMemberNames.RpcInvoker.InvokeValueOnInstanceAsync
                : ServicesGeneratorMemberNames.RpcInvoker.InvokeValueAsync;
            return true;
        }

        invoker = string.Empty;
        return false;
    }

    private static string StandardInvoker(bool isInstanceScoped)
        => isInstanceScoped
            ? ServicesGeneratorMemberNames.RpcInvoker.InvokeOnInstanceAsync
            : ServicesGeneratorMemberNames.RpcInvoker.InvokeAsync;
}
