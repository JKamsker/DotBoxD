using System.Collections.Generic;
using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Generation;

internal static class ProxyInvocationEmitter
{
    private static readonly Dictionary<MethodReturnKind, ProxyReturnEmitter> ReturnEmitters = new()
    {
        [MethodReturnKind.Void] = static (context) =>
            EmitSyncVoidReturn(context.Builder, context.Method, context.Invocation, context.CancellationToken, context.Indent),
        [MethodReturnKind.Sync] = static (context) =>
            EmitSyncValueReturn(
                context.Builder,
                context.Method,
                context.Invocation,
                context.Locals,
                context.CancellationToken,
                context.Indent),
        [MethodReturnKind.Stream] = static (context) =>
            EmitSyncValueReturn(
                context.Builder,
                context.Method,
                context.Invocation,
                context.Locals,
                context.CancellationToken,
                context.Indent),
        [MethodReturnKind.Pipe] = static (context) =>
            EmitSyncValueReturn(
                context.Builder,
                context.Method,
                context.Invocation,
                context.Locals,
                context.CancellationToken,
                context.Indent),
        [MethodReturnKind.AsyncEnumerable] = static (context) =>
            context.Builder.AppendLine($"{context.Indent}return {context.Invocation};"),
        [MethodReturnKind.Task] = static (context) => EmitTaskLikeReturn(context),
        [MethodReturnKind.ValueTask] = static (context) => EmitTaskLikeReturn(context),
        [MethodReturnKind.TaskOf] = static (context) => EmitTaskLikeReturn(context),
        [MethodReturnKind.TaskOfStream] = static (context) => EmitTaskLikeReturn(context),
        [MethodReturnKind.TaskOfPipe] = static (context) => EmitTaskLikeReturn(context),
        [MethodReturnKind.TaskOfAsyncEnumerable] = static (context) => EmitTaskLikeReturn(context),
        [MethodReturnKind.ValueTaskOf] = static (context) => EmitTaskLikeReturn(context),
        [MethodReturnKind.ValueTaskOfStream] = static (context) => EmitWrappedValueTaskReturn(context),
        [MethodReturnKind.ValueTaskOfPipe] = static (context) => EmitWrappedValueTaskReturn(context),
        [MethodReturnKind.ValueTaskOfAsyncEnumerable] = static (context) => EmitWrappedValueTaskReturn(context),
        [MethodReturnKind.TaskOfSubService] = static (context) => EmitSubServiceReturn(context),
        [MethodReturnKind.ValueTaskOfSubService] = static (context) => EmitSubServiceReturn(context),
        [MethodReturnKind.SyncSubService] = static (context) =>
            EmitSyncSubServiceReturn(
                context.Builder,
                context.Method,
                context.Invocation,
                context.Locals,
                context.CancellationToken,
                context.Indent),
    };

    private delegate void ProxyReturnEmitter(ProxyReturnContext context);

    private readonly record struct ProxyReturnContext(
        StringBuilder Builder,
        MethodModel Method,
        string Invocation,
        GeneratedLocalNames Locals,
        CancellationToken CancellationToken,
        string Indent,
        bool CaptureSynchronousExceptions);

    public static void Emit(
        StringBuilder sb,
        MethodModel method,
        string invocation,
        GeneratedLocalNames locals,
        CancellationToken ct,
        string indent = "            ",
        bool captureSynchronousExceptions = true)
    {
        if (ReturnEmitters.TryGetValue(method.ReturnKind, out var emit))
        {
            emit(new ProxyReturnContext(sb, method, invocation, locals, ct, indent, captureSynchronousExceptions));
        }
    }

    private static void EmitTaskLikeReturn(ProxyReturnContext context)
        => EmitTaskLikeReturn(
            context.Builder,
            context.Method,
            context.Invocation,
            context.Locals,
            context.CancellationToken,
            context.Indent,
            context.CaptureSynchronousExceptions);

    private static void EmitWrappedValueTaskReturn(ProxyReturnContext context)
        => EmitTaskLikeReturn(
            context.Builder,
            context.Method,
            $"new {ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalValueTask, ProxyFaultedReturnEmitter.GetValueTaskResultType(context.Method))}({context.Invocation})",
            context.Locals,
            context.CancellationToken,
            context.Indent,
            context.CaptureSynchronousExceptions);

    private static void EmitSubServiceReturn(ProxyReturnContext context)
        => EmitSubServiceReturn(
            context.Builder,
            context.Method,
            context.Invocation,
            context.Locals,
            context.CancellationToken,
            context.Indent);

    private static void EmitSyncVoidReturn(
        StringBuilder sb,
        MethodModel method,
        string invocation,
        CancellationToken ct,
        string indent)
    {
        sb.AppendLine($"{indent}{invocation}.GetAwaiter().GetResult();");
        EmitCallerCancellationRecheck(sb, method, ct, indent);
    }

    private static void EmitSyncValueReturn(
        StringBuilder sb,
        MethodModel method,
        string invocation,
        GeneratedLocalNames locals,
        CancellationToken ct,
        string indent)
    {
        if (!method.HasCancellationToken)
        {
            sb.AppendLine($"{indent}return {invocation}.GetAwaiter().GetResult();");
            return;
        }

        var resultName = locals.Reserve("__dotboxd_result", ct);
        sb.AppendLine($"{indent}var {resultName} = {invocation}.GetAwaiter().GetResult();");
        EmitCallerCancellationRecheck(sb, method, ct, indent);
        sb.AppendLine($"{indent}return {resultName};");
    }

    private static void EmitTaskLikeReturn(
        StringBuilder sb,
        MethodModel method,
        string returnExpression,
        GeneratedLocalNames locals,
        CancellationToken ct,
        string indent,
        bool captureSynchronousExceptions)
    {
        returnExpression = BuildCallerCancellationObservedReturnExpression(method, returnExpression, ct);
        if (!captureSynchronousExceptions)
        {
            sb.AppendLine($"{indent}return {returnExpression};");
            return;
        }

        var exceptionName = locals.Reserve("__dotboxd_ex", ct);
        sb.AppendLine($"{indent}try");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    return {returnExpression};");
        sb.AppendLine($"{indent}}}");
        var canceledName = locals.Reserve("__dotboxd_canceled", ct);
        var cancellationFilter = ProxyFaultedReturnEmitter.BuildCancellationCatchFilter(method, canceledName, ct);
        var cancellationToken = ProxyFaultedReturnEmitter.BuildCancellationTokenExpression(method, canceledName, ct);
        sb.AppendLine($"{indent}catch ({ServicesGeneratorTypeNames.GlobalOperationCanceledException} {canceledName}) when ({cancellationFilter})");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    return {ProxyFaultedReturnEmitter.BuildCanceled(method, cancellationToken)};");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine($"{indent}catch ({ServicesGeneratorTypeNames.GlobalException} {exceptionName})");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    return {ProxyFaultedReturnEmitter.Build(method, exceptionName)};");
        sb.AppendLine($"{indent}}}");
    }

    private static string BuildCallerCancellationObservedReturnExpression(
        MethodModel method,
        string returnExpression,
        CancellationToken ct)
    {
        if (!method.HasCancellationToken)
        {
            return returnExpression;
        }

        var callerToken = ProxyGenerationHelpers.GetCancellationTokenArgument(method.Parameters, ct);
        return $"{ProxyGenerationHelpers.CallerCancellationHelperName}({returnExpression}, {callerToken})";
    }

    private static void EmitSubServiceReturn(
        StringBuilder sb,
        MethodModel method,
        string invocation,
        GeneratedLocalNames locals,
        CancellationToken ct,
        string indent)
    {
        var info = method.SubService!;
        var subProxyType = ProxyGenerationHelpers.BuildSubProxyTypeName(info.QualifiedInterfaceName);
        var handleName = locals.Reserve("__dotboxd_handle", ct);
        sb.AppendLine($"{indent}var {handleName} = await {invocation};");
        EmitCallerCancellationRecheck(sb, method, ct, indent);
        if (info.AllowsNull)
        {
            // ServiceHandle is a struct, so the nullable wire type is Nullable<ServiceHandle>;
            // unwrap via .Value before reading InstanceId.
            var valueName = locals.Reserve("__dotboxd_handleValue", ct);
            sb.AppendLine($"{indent}if ({handleName} is null)");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    return null;");
            sb.AppendLine($"{indent}}}");
            sb.AppendLine($"{indent}var {valueName} = {handleName}.Value;");
            EmitSubServiceHandleValidation(sb, valueName, info.ServiceName, indent);
            sb.AppendLine($"{indent}return new {subProxyType}(this._invoker, {valueName}.{ServicesGeneratorMemberNames.ServiceHandle.InstanceId});");
        }
        else
        {
            EmitSubServiceHandleValidation(sb, handleName, info.ServiceName, indent);
            sb.AppendLine($"{indent}return new {subProxyType}(this._invoker, {handleName}.{ServicesGeneratorMemberNames.ServiceHandle.InstanceId});");
        }
    }

    private static void EmitSyncSubServiceReturn(
        StringBuilder sb,
        MethodModel method,
        string invocation,
        GeneratedLocalNames locals,
        CancellationToken ct,
        string indent)
    {
        var info = method.SubService!;
        var subProxyType = ProxyGenerationHelpers.BuildSubProxyTypeName(info.QualifiedInterfaceName);
        var handleName = locals.Reserve("__dotboxd_handle", ct);
        sb.AppendLine($"{indent}var {handleName} = {invocation}.GetAwaiter().GetResult();");
        EmitCallerCancellationRecheck(sb, method, ct, indent);
        if (info.AllowsNull)
        {
            var valueName = locals.Reserve("__dotboxd_handleValue", ct);
            sb.AppendLine($"{indent}if ({handleName} is null)");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    return null;");
            sb.AppendLine($"{indent}}}");
            sb.AppendLine($"{indent}var {valueName} = {handleName}.Value;");
            EmitSubServiceHandleValidation(sb, valueName, info.ServiceName, indent);
            sb.AppendLine($"{indent}return new {subProxyType}(this._invoker, {valueName}.{ServicesGeneratorMemberNames.ServiceHandle.InstanceId});");
        }
        else
        {
            EmitSubServiceHandleValidation(sb, handleName, info.ServiceName, indent);
            sb.AppendLine($"{indent}return new {subProxyType}(this._invoker, {handleName}.{ServicesGeneratorMemberNames.ServiceHandle.InstanceId});");
        }
    }

    private static void EmitCallerCancellationRecheck(
        StringBuilder sb,
        MethodModel method,
        CancellationToken ct,
        string indent)
    {
        if (!method.HasCancellationToken)
        {
            return;
        }

        var ctArg = ProxyGenerationHelpers.GetCancellationTokenArgument(method.Parameters, ct);
        sb.AppendLine($"{indent}{ctArg}.ThrowIfCancellationRequested();");
    }

    private static void EmitSubServiceHandleValidation(StringBuilder sb, string handleName, string serviceName, string indent)
    {
        var actual = handleName + "." + ServicesGeneratorMemberNames.ServiceHandle.ServiceName;
        var expected = LiteralHelpers.EscapeStringLiteral(serviceName);
        sb.AppendLine($"{indent}if (!global::System.String.Equals({actual}, \"{expected}\", global::System.StringComparison.Ordinal))");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    throw new {ServicesGeneratorTypeNames.GlobalServiceProtocolException}(\"ServiceHandle.ServiceName did not match expected sub-service '{expected}'.\");");
        sb.AppendLine($"{indent}}}");
    }
}
