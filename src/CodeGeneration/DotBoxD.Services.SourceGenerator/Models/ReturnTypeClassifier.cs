using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class ReturnTypeClassifier
{
    private const string SystemCollectionsGeneric = ServicesGeneratorTypeNames.SystemCollectionsGenericNamespace;
    private const string SystemIO = ServicesGeneratorTypeNames.SystemIoNamespace;
    private const string SystemIOPipelines = ServicesGeneratorTypeNames.SystemIoPipelinesNamespace;
    private const string SystemThreadingTasks = ServicesGeneratorTypeNames.SystemThreadingTasksNamespace;

    private static readonly SymbolDisplayFormat s_qualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string? GetUnsupportedServiceReturnReason(ITypeSymbol returnType, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!SubServiceReturnTypeReader.TryGetAsyncPayloadType(returnType, out var payloadType) ||
            !SubServiceReturnTypeReader.IsRpcServiceInterface(payloadType, ct))
        {
            return null;
        }

        if (payloadType is INamedTypeSymbol named)
        {
            if (named.IsGenericType)
            {
                return "generic sub-service return types are not supported";
            }

            if (named.ContainingType is not null)
            {
                return "nested sub-service return types are not supported";
            }
        }

        return null;
    }

    public static MethodReturnKind Classify(
        ITypeSymbol returnType,
        CancellationToken ct,
        out string? unwrappedReturnType,
        out SubServiceInfo? subService)
    {
        ct.ThrowIfCancellationRequested();

        subService = null;

        if (TryClassifyGenericTaskLike(returnType, ct, out var genericKind, out unwrappedReturnType, out subService))
        {
            return genericKind;
        }

        if (TryClassifyNonGenericTaskLike(returnType, out var taskKind))
        {
            unwrappedReturnType = null;
            return taskKind;
        }

        if (returnType.SpecialType == SpecialType.System_Void)
        {
            unwrappedReturnType = null;
            return MethodReturnKind.Void;
        }

        if (TryClassifyDirectShape(returnType, out var directKind, out unwrappedReturnType))
        {
            return directKind;
        }

        unwrappedReturnType = returnType.ToDisplayString(s_qualifiedFormat);
        if (TryGetSubServiceInfo(returnType, ct, out var syncSubService))
        {
            subService = syncSubService;
            return MethodReturnKind.SyncSubService;
        }

        return MethodReturnKind.Sync;
    }

    private static bool TryClassifyGenericTaskLike(
        ITypeSymbol returnType,
        CancellationToken ct,
        out MethodReturnKind kind,
        out string? unwrappedReturnType,
        out SubServiceInfo? subService)
    {
        kind = default;
        unwrappedReturnType = null;
        subService = null;
        if (returnType is not INamedTypeSymbol { IsGenericType: true } named ||
            named.ContainingNamespace?.ToDisplayString() != SystemThreadingTasks)
        {
            return false;
        }

        if (named.Name == "Task")
        {
            kind = ClassifyTaskPayload(named.TypeArguments[0], valueTask: false, ct, out unwrappedReturnType, out subService);
            return true;
        }

        if (named.Name == "ValueTask")
        {
            kind = ClassifyTaskPayload(named.TypeArguments[0], valueTask: true, ct, out unwrappedReturnType, out subService);
            return true;
        }

        return false;
    }

    private static MethodReturnKind ClassifyTaskPayload(
        ITypeSymbol payloadType,
        bool valueTask,
        CancellationToken ct,
        out string unwrappedReturnType,
        out SubServiceInfo? subService)
    {
        subService = null;
        if (TryGetAsyncEnumerableItemType(payloadType, out var itemType))
        {
            unwrappedReturnType = itemType.ToDisplayString(s_qualifiedFormat);
            return valueTask ? MethodReturnKind.ValueTaskOfAsyncEnumerable : MethodReturnKind.TaskOfAsyncEnumerable;
        }

        if (IsStream(payloadType))
        {
            unwrappedReturnType = payloadType.ToDisplayString(s_qualifiedFormat);
            return valueTask ? MethodReturnKind.ValueTaskOfStream : MethodReturnKind.TaskOfStream;
        }

        if (IsPipe(payloadType))
        {
            unwrappedReturnType = payloadType.ToDisplayString(s_qualifiedFormat);
            return valueTask ? MethodReturnKind.ValueTaskOfPipe : MethodReturnKind.TaskOfPipe;
        }

        unwrappedReturnType = payloadType.ToDisplayString(s_qualifiedFormat);
        return ClassifyTaskPayloadCore(payloadType, valueTask, ct, ref subService);
    }

    private static MethodReturnKind ClassifyTaskPayloadCore(
        ITypeSymbol payloadType,
        bool valueTask,
        CancellationToken ct,
        ref SubServiceInfo? subService)
    {
        if (TryGetSubServiceInfo(payloadType, ct, out var sub))
        {
            subService = sub;
            return valueTask ? MethodReturnKind.ValueTaskOfSubService : MethodReturnKind.TaskOfSubService;
        }

        return valueTask ? MethodReturnKind.ValueTaskOf : MethodReturnKind.TaskOf;
    }

    private static bool TryClassifyNonGenericTaskLike(ITypeSymbol returnType, out MethodReturnKind kind)
    {
        kind = default;
        if (returnType.ContainingNamespace?.ToDisplayString() != SystemThreadingTasks)
        {
            return false;
        }

        if (returnType.Name == "Task")
        {
            kind = MethodReturnKind.Task;
            return true;
        }

        if (returnType.Name == "ValueTask")
        {
            kind = MethodReturnKind.ValueTask;
            return true;
        }

        return false;
    }

    private static bool TryClassifyDirectShape(
        ITypeSymbol returnType,
        out MethodReturnKind kind,
        out string? unwrappedReturnType)
    {
        if (TryGetAsyncEnumerableItemType(returnType, out var enumerableItemType))
        {
            kind = MethodReturnKind.AsyncEnumerable;
            unwrappedReturnType = enumerableItemType.ToDisplayString(s_qualifiedFormat);
            return true;
        }

        kind = IsStream(returnType) ? MethodReturnKind.Stream : MethodReturnKind.Pipe;
        unwrappedReturnType = returnType.ToDisplayString(s_qualifiedFormat);
        return IsStream(returnType) || IsPipe(returnType);
    }

    public static bool TryGetAsyncEnumerableItemType(ITypeSymbol type, out ITypeSymbol itemType)
    {
        itemType = null!;
        if (type is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return false;
        }

        if (named.Name != "IAsyncEnumerable" ||
            named.ContainingNamespace?.ToDisplayString() != SystemCollectionsGeneric)
        {
            return false;
        }

        itemType = named.TypeArguments[0];
        return true;
    }

    public static bool IsStream(ITypeSymbol type) =>
        type.Name == "Stream" &&
        type.ContainingNamespace?.ToDisplayString() == SystemIO;

    public static bool IsPipe(ITypeSymbol type) =>
        type.Name == "Pipe" &&
        type.ContainingNamespace?.ToDisplayString() == SystemIOPipelines;

    internal static bool TryGetSubServiceInfo(ITypeSymbol type, CancellationToken ct, out SubServiceInfo info)
        => SubServiceReturnTypeReader.TryGetSubServiceInfo(type, ct, out info);
}
