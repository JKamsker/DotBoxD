using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Generation;

internal static class ProxyFaultedReturnEmitter
{
    public static bool CanReturnFaulted(MethodReturnKind returnKind) =>
        IsFaultableTask(returnKind) || IsFaultableValueTask(returnKind);

    public static string Build(MethodModel method, string exceptionName)
    {
        if (method.ReturnKind == MethodReturnKind.Task)
        {
            return $"{ServicesGeneratorTypeNames.GlobalTask}.FromException({exceptionName})";
        }

        if (IsTaskWithResult(method.ReturnKind))
        {
            return $"{ServicesGeneratorTypeNames.GlobalTask}.FromException<{GetTaskResultType(method)}>({exceptionName})";
        }

        if (method.ReturnKind == MethodReturnKind.ValueTask)
        {
            return $"new {ServicesGeneratorTypeNames.GlobalValueTask}({ServicesGeneratorTypeNames.GlobalTask}.FromException({exceptionName}))";
        }

        if (IsValueTaskWithResult(method.ReturnKind))
        {
            var valueTaskType = ServicesGeneratorTypeNames.Generic(
                ServicesGeneratorTypeNames.GlobalValueTask,
                GetValueTaskResultType(method));
            return $"new {valueTaskType}({ServicesGeneratorTypeNames.GlobalTask}.FromException<{GetValueTaskResultType(method)}>({exceptionName}))";
        }

        throw new System.InvalidOperationException("Return kind cannot carry a faulted task.");
    }

    public static string BuildCanceled(MethodModel method, string exceptionName)
    {
        if (method.ReturnKind == MethodReturnKind.Task)
        {
            return $"{ServicesGeneratorTypeNames.GlobalTask}.FromCanceled({exceptionName}.CancellationToken)";
        }

        if (IsTaskWithResult(method.ReturnKind))
        {
            return $"{ServicesGeneratorTypeNames.GlobalTask}.FromCanceled<{GetTaskResultType(method)}>({exceptionName}.CancellationToken)";
        }

        if (method.ReturnKind == MethodReturnKind.ValueTask)
        {
            return $"new {ServicesGeneratorTypeNames.GlobalValueTask}({ServicesGeneratorTypeNames.GlobalTask}.FromCanceled({exceptionName}.CancellationToken))";
        }

        if (IsValueTaskWithResult(method.ReturnKind))
        {
            var valueTaskType = ServicesGeneratorTypeNames.Generic(
                ServicesGeneratorTypeNames.GlobalValueTask,
                GetValueTaskResultType(method));
            return $"new {valueTaskType}({ServicesGeneratorTypeNames.GlobalTask}.FromCanceled<{GetValueTaskResultType(method)}>({exceptionName}.CancellationToken))";
        }

        throw new System.InvalidOperationException("Return kind cannot carry a canceled task.");
    }

    private static bool IsFaultableTask(MethodReturnKind returnKind) =>
        returnKind == MethodReturnKind.Task || IsTaskWithResult(returnKind);

    private static bool IsFaultableValueTask(MethodReturnKind returnKind) =>
        returnKind == MethodReturnKind.ValueTask || IsValueTaskWithResult(returnKind);

    private static bool IsTaskWithResult(MethodReturnKind returnKind) =>
        returnKind is MethodReturnKind.TaskOf
            or MethodReturnKind.TaskOfStream
            or MethodReturnKind.TaskOfPipe
            or MethodReturnKind.TaskOfAsyncEnumerable;

    private static bool IsValueTaskWithResult(MethodReturnKind returnKind) =>
        returnKind is MethodReturnKind.ValueTaskOf
            or MethodReturnKind.ValueTaskOfStream
            or MethodReturnKind.ValueTaskOfPipe
            or MethodReturnKind.ValueTaskOfAsyncEnumerable;

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
