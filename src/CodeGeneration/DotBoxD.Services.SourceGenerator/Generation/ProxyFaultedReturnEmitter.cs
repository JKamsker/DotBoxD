using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Generation;

internal static class ProxyFaultedReturnEmitter
{
    public static bool CanReturnFaulted(MethodReturnKind returnKind) =>
        returnKind is MethodReturnKind.Task
            or MethodReturnKind.TaskOf
            or MethodReturnKind.TaskOfStream
            or MethodReturnKind.TaskOfPipe
            or MethodReturnKind.TaskOfAsyncEnumerable
            or MethodReturnKind.ValueTask
            or MethodReturnKind.ValueTaskOf
            or MethodReturnKind.ValueTaskOfStream
            or MethodReturnKind.ValueTaskOfPipe
            or MethodReturnKind.ValueTaskOfAsyncEnumerable;

    public static string Build(MethodModel method, string exceptionName) =>
        method.ReturnKind switch
        {
            MethodReturnKind.Task =>
                $"{ServicesGeneratorTypeNames.GlobalTask}.FromException({exceptionName})",
            MethodReturnKind.TaskOf or
                MethodReturnKind.TaskOfStream or
                MethodReturnKind.TaskOfPipe or
                MethodReturnKind.TaskOfAsyncEnumerable =>
                $"{ServicesGeneratorTypeNames.GlobalTask}.FromException<{GetTaskResultType(method)}>({exceptionName})",
            MethodReturnKind.ValueTask =>
                $"new {ServicesGeneratorTypeNames.GlobalValueTask}({ServicesGeneratorTypeNames.GlobalTask}.FromException({exceptionName}))",
            MethodReturnKind.ValueTaskOf or
                MethodReturnKind.ValueTaskOfStream or
                MethodReturnKind.ValueTaskOfPipe or
                MethodReturnKind.ValueTaskOfAsyncEnumerable =>
                $"new {ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalValueTask, GetValueTaskResultType(method))}({ServicesGeneratorTypeNames.GlobalTask}.FromException<{GetValueTaskResultType(method)}>({exceptionName}))",
            _ => throw new System.InvalidOperationException("Return kind cannot carry a faulted task."),
        };

    public static string BuildCanceled(MethodModel method, string exceptionName) =>
        method.ReturnKind switch
        {
            MethodReturnKind.Task =>
                $"{ServicesGeneratorTypeNames.GlobalTask}.FromCanceled({exceptionName}.CancellationToken)",
            MethodReturnKind.TaskOf or
                MethodReturnKind.TaskOfStream or
                MethodReturnKind.TaskOfPipe or
                MethodReturnKind.TaskOfAsyncEnumerable =>
                $"{ServicesGeneratorTypeNames.GlobalTask}.FromCanceled<{GetTaskResultType(method)}>({exceptionName}.CancellationToken)",
            MethodReturnKind.ValueTask =>
                $"new {ServicesGeneratorTypeNames.GlobalValueTask}({ServicesGeneratorTypeNames.GlobalTask}.FromCanceled({exceptionName}.CancellationToken))",
            MethodReturnKind.ValueTaskOf or
                MethodReturnKind.ValueTaskOfStream or
                MethodReturnKind.ValueTaskOfPipe or
                MethodReturnKind.ValueTaskOfAsyncEnumerable =>
                $"new {ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalValueTask, GetValueTaskResultType(method))}({ServicesGeneratorTypeNames.GlobalTask}.FromCanceled<{GetValueTaskResultType(method)}>({exceptionName}.CancellationToken))",
            _ => throw new System.InvalidOperationException("Return kind cannot carry a canceled task."),
        };

    public static string GetValueTaskResultType(MethodModel method) =>
        method.ReturnKind switch
        {
            MethodReturnKind.ValueTaskOf => method.UnwrappedReturnType!,
            MethodReturnKind.ValueTaskOfStream => ServicesGeneratorTypeNames.GlobalStream,
            MethodReturnKind.ValueTaskOfPipe => ServicesGeneratorTypeNames.GlobalPipe,
            MethodReturnKind.ValueTaskOfAsyncEnumerable =>
                ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalIAsyncEnumerable, method.UnwrappedReturnType!),
            _ => throw new System.InvalidOperationException("Return kind is not a generic ValueTask."),
        };

    private static string GetTaskResultType(MethodModel method) =>
        method.ReturnKind switch
        {
            MethodReturnKind.TaskOf => method.UnwrappedReturnType!,
            MethodReturnKind.TaskOfStream => ServicesGeneratorTypeNames.GlobalStream,
            MethodReturnKind.TaskOfPipe => ServicesGeneratorTypeNames.GlobalPipe,
            MethodReturnKind.TaskOfAsyncEnumerable =>
                ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalIAsyncEnumerable, method.UnwrappedReturnType!),
            _ => throw new System.InvalidOperationException("Return kind is not a generic Task."),
        };
}
