using System.Text;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal static class ProxyInvocationEmitter
{
    public static void Emit(
        StringBuilder sb,
        MethodModel method,
        string invocation,
        GeneratedLocalNames locals,
        CancellationToken ct,
        string indent = "            ")
    {
        switch (method.ReturnKind)
        {
            case MethodReturnKind.Void:
                sb.AppendLine($"{indent}{invocation}.GetAwaiter().GetResult();");
                break;
            case MethodReturnKind.Sync:
                sb.AppendLine($"{indent}return {invocation}.GetAwaiter().GetResult();");
                break;
            case MethodReturnKind.Stream:
            case MethodReturnKind.Pipe:
                sb.AppendLine($"{indent}return {invocation}.GetAwaiter().GetResult();");
                break;
            case MethodReturnKind.AsyncEnumerable:
                sb.AppendLine($"{indent}return {invocation};");
                break;
            case MethodReturnKind.Task:
            case MethodReturnKind.ValueTask:
                sb.AppendLine($"{indent}await {invocation};");
                break;
            case MethodReturnKind.TaskOf:
            case MethodReturnKind.ValueTaskOf:
            case MethodReturnKind.TaskOfStream:
            case MethodReturnKind.ValueTaskOfStream:
            case MethodReturnKind.TaskOfPipe:
            case MethodReturnKind.ValueTaskOfPipe:
                sb.AppendLine($"{indent}return await {invocation};");
                break;
            case MethodReturnKind.TaskOfAsyncEnumerable:
            case MethodReturnKind.ValueTaskOfAsyncEnumerable:
                sb.AppendLine($"{indent}return await {invocation};");
                break;
            case MethodReturnKind.TaskOfSubService:
            case MethodReturnKind.ValueTaskOfSubService:
                EmitSubServiceReturn(sb, method, invocation, locals, ct, indent);
                break;
        }
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
        var handleName = locals.Reserve("__sharpc_handle", ct);
        sb.AppendLine($"{indent}var {handleName} = await {invocation};");
        if (info.AllowsNull)
        {
            // ServiceHandle is a struct, so the nullable wire type is Nullable<ServiceHandle>;
            // unwrap via .Value before reading InstanceId.
            sb.AppendLine($"{indent}return {handleName} is null ? null : new {subProxyType}(this._invoker, {handleName}.Value.InstanceId);");
        }
        else
        {
            sb.AppendLine($"{indent}return new {subProxyType}(this._invoker, {handleName}.InstanceId);");
        }
    }
}
