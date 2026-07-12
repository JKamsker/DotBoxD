using System.Collections.Generic;
using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Generation;

internal static class DispatcherCaseGenerator
{
    private static readonly Dictionary<MethodReturnKind, DispatcherReturnGenerator> ReturnGenerators = new()
    {
        [MethodReturnKind.Void] = static (sb, _, _, call) =>
        {
            sb.AppendLine($"                    {call};");
            sb.AppendLine("                    return;");
        },
        [MethodReturnKind.Sync] = static (sb, _, _, call) =>
        {
            sb.AppendLine($"                    var result = {call};");
            AppendCancellationCheckpoint(sb);
            sb.AppendLine($"                    serializer.{ServicesGeneratorMemberNames.Serializer.Serialize}(output, result);");
            sb.AppendLine("                    return;");
        },
        [MethodReturnKind.Task] = static (sb, _, _, call) => GenerateTaskReturn(sb, call),
        [MethodReturnKind.ValueTask] = static (sb, _, _, call) => GenerateValueTaskReturn(sb, call),
        [MethodReturnKind.ValueTaskOf] = static (sb, _, _, call) => GenerateSerializedAwaitedResult(sb, call),
        [MethodReturnKind.TaskOf] = static (sb, _, _, call) => GenerateSerializedAwaitedResult(sb, call),
        [MethodReturnKind.TaskOfSubService] = static (sb, _, method, call) => DispatcherSubServiceReturnGenerator.Generate(sb, method, call),
        [MethodReturnKind.ValueTaskOfSubService] = static (sb, _, method, call) => DispatcherSubServiceReturnGenerator.Generate(sb, method, call),
        [MethodReturnKind.SyncSubService] = static (sb, _, method, call) => DispatcherSubServiceReturnGenerator.Generate(sb, method, call),
        [MethodReturnKind.AsyncEnumerable] = static (sb, _, _, call) => GenerateStreamingReturn(sb, call),
        [MethodReturnKind.Stream] = static (sb, _, _, call) => GenerateStreamingReturn(sb, call),
        [MethodReturnKind.Pipe] = static (sb, _, _, call) => GenerateStreamingReturn(sb, call),
        [MethodReturnKind.TaskOfAsyncEnumerable] = static (sb, _, _, call) => GenerateAwaitedStreamingReturn(sb, call),
        [MethodReturnKind.ValueTaskOfAsyncEnumerable] = static (sb, _, _, call) => GenerateAwaitedStreamingReturn(sb, call),
        [MethodReturnKind.TaskOfStream] = static (sb, _, _, call) => GenerateAwaitedStreamingReturn(sb, call),
        [MethodReturnKind.ValueTaskOfStream] = static (sb, _, _, call) => GenerateAwaitedStreamingReturn(sb, call),
        [MethodReturnKind.TaskOfPipe] = static (sb, _, _, call) => GenerateAwaitedStreamingReturn(sb, call),
        [MethodReturnKind.ValueTaskOfPipe] = static (sb, _, _, call) => GenerateAwaitedStreamingReturn(sb, call),
    };

    private delegate void DispatcherReturnGenerator(
        StringBuilder sb,
        ServiceModel service,
        MethodModel method,
        string call);

    public static void Generate(
        StringBuilder sb,
        ServiceModel service,
        MethodModel method,
        string receiver,
        string instanceId,
        CancellationToken ct)
    {
        if (method.UnsupportedReason is not null)
        {
            return;
        }

        sb.AppendLine($"                case \"{method.RpcName}\":");
        sb.AppendLine("                {");

        var requestParameters = DispatcherGeneratorHelpers.GetRequestParameters(method.Parameters, ct);
        if (requestParameters.Count == 0)
        {
            AppendNoPayloadGuard(sb);
        }
        else if (requestParameters.Count == 1)
        {
            var wireType = ProxyGenerationHelpers.GetWireType(requestParameters[0]);
            sb.AppendLine($"                    var arg = serializer.{ServicesGeneratorMemberNames.Serializer.Deserialize}<{wireType}>(payload);");
        }
        else if (requestParameters.Count > 1)
        {
            AppendTupleArgumentReader(sb, requestParameters, ct);
        }

        AppendCancellationCheckpoint(sb);

        var locals = new GeneratedLocalNames(method.Parameters, ct);
        var argumentExpressions = BuildArgumentExpressions(
            sb,
            method,
            requestParameters.Count,
            locals,
            ct);
        var call = BuildCall(method, receiver, argumentExpressions, ct);

        GenerateReturn(sb, service, method, call, instanceId);
        sb.AppendLine("                }");
    }

    private static void AppendNoPayloadGuard(StringBuilder sb)
    {
        sb.AppendLine("                    if (payload.Length != 0)");
        sb.AppendLine("                    {");
        sb.AppendLine($"                        throw new {ServicesGeneratorTypeNames.GlobalServiceProtocolException}(\"Request payload is not allowed for a parameterless RPC method.\");");
        sb.AppendLine("                    }");
    }

    private static void AppendTupleArgumentReader(
        StringBuilder sb,
        List<ParameterModel> requestParameters,
        CancellationToken ct)
    {
        var tupleTypes = new StringBuilder();
        for (var i = 0; i < requestParameters.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (i > 0)
                tupleTypes.Append(", ");
            tupleTypes.Append(ProxyGenerationHelpers.GetWireType(requestParameters[i]));
        }

        sb.AppendLine($"                    var args = serializer.{ServicesGeneratorMemberNames.Serializer.Deserialize}<({tupleTypes})>(payload);");
    }

    private static string[] BuildArgumentExpressions(
        StringBuilder sb,
        MethodModel method,
        int requestParameterCount,
        GeneratedLocalNames locals,
        CancellationToken ct)
    {
        var argumentExpressions = new string[method.Parameters.Count];
        var argumentRequestIndex = 0;
        for (var i = 0; i < method.Parameters.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var parameter = method.Parameters[i];
            if (parameter.IsCancellationToken)
            {
                argumentExpressions[i] = "ct";
                continue;
            }

            argumentRequestIndex++;
            var source = requestParameterCount == 1
                ? "arg"
                : "args.Item" + argumentRequestIndex;
            if (parameter.StreamKind == ParameterStreamKind.None)
            {
                argumentExpressions[i] = source;
                continue;
            }

            var local = locals.Reserve("__dotboxd_arg" + argumentRequestIndex, ct);
            sb.AppendLine($"                    var {local} = {DispatcherGeneratorHelpers.BuildStreamingArgument(parameter, source)};");
            argumentExpressions[i] = local;
        }

        return argumentExpressions;
    }

    private static string BuildCall(
        MethodModel method,
        string receiver,
        string[] argumentExpressions,
        CancellationToken ct)
    {
        var argList = new StringBuilder();
        for (var i = 0; i < method.Parameters.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (i > 0)
                argList.Append(", ");
            argList.Append(argumentExpressions[i]);
        }

        var target = method.RequiresDispatcherReceiverCast
            ? $"(({method.ExplicitImplementationType}){receiver})"
            : receiver;
        return $"{target}.{method.Name}({argList})";
    }

    private static void GenerateReturn(
        StringBuilder sb,
        ServiceModel service,
        MethodModel method,
        string call,
        string instanceId)
    {
        if (DispatcherInstanceDisposeGenerator.IsAsyncDisposableDispose(method))
        {
            DispatcherInstanceDisposeGenerator.GenerateReturn(sb, service, call, instanceId);
            return;
        }

        if (ReturnGenerators.TryGetValue(method.ReturnKind, out var generate))
        {
            generate(sb, service, method, call);
        }
    }

    private static void GenerateTaskReturn(StringBuilder sb, string call)
    {
        sb.AppendLine($"                    var __dotboxd_task = {call};");
        sb.AppendLine("                    if (!__dotboxd_task.IsCompletedSuccessfully)");
        sb.AppendLine("                    {");
        sb.AppendLine("                        await __dotboxd_task;");
        sb.AppendLine("                    }");
        sb.AppendLine("                    return;");
    }

    private static void GenerateValueTaskReturn(StringBuilder sb, string call)
    {
        sb.AppendLine($"                    var __dotboxd_task = {call};");
        sb.AppendLine("                    if (!__dotboxd_task.IsCompletedSuccessfully)");
        sb.AppendLine("                    {");
        sb.AppendLine("                        await __dotboxd_task;");
        sb.AppendLine("                        return;");
        sb.AppendLine("                    }");
        sb.AppendLine("                    __dotboxd_task.GetAwaiter().GetResult();");
        sb.AppendLine("                    return;");
    }

    private static void GenerateSerializedAwaitedResult(StringBuilder sb, string call)
    {
        GenerateAwaitedResult(sb, call);
        AppendCancellationCheckpoint(sb);
        sb.AppendLine($"                    serializer.{ServicesGeneratorMemberNames.Serializer.Serialize}(output, __dotboxd_result);");
        sb.AppendLine("                    return;");
    }

    private static void GenerateStreamingReturn(StringBuilder sb, string call)
    {
        sb.AppendLine($"                    var result = {call};");
        AppendCancellationCheckpoint(sb);
        sb.AppendLine($"                    streaming.{ServicesGeneratorMemberNames.RpcStreamingContext.SetResponse}(result);");
        sb.AppendLine("                    return;");
    }

    private static void GenerateAwaitedStreamingReturn(StringBuilder sb, string call)
    {
        GenerateAwaitedResult(sb, call);
        AppendCancellationCheckpoint(sb);
        sb.AppendLine($"                    streaming.{ServicesGeneratorMemberNames.RpcStreamingContext.SetResponse}(__dotboxd_result);");
        sb.AppendLine("                    return;");
    }

    private static void GenerateAwaitedResult(StringBuilder sb, string call)
    {
        sb.AppendLine($"                    var __dotboxd_task = {call};");
        sb.AppendLine("                    var __dotboxd_result = __dotboxd_task.IsCompletedSuccessfully");
        sb.AppendLine("                        ? __dotboxd_task.Result");
        sb.AppendLine("                        : await __dotboxd_task;");
    }

    private static void AppendCancellationCheckpoint(StringBuilder sb)
    {
        sb.AppendLine("                    ct.ThrowIfCancellationRequested();");
    }
}
