using System.Linq;
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

    public static bool IsLookalikeTaskLike(ITypeSymbol type) =>
        type is INamedTypeSymbol named &&
        named.ContainingNamespace?.ToDisplayString() == SystemThreadingTasks &&
        (named.Name == "Task" || named.Name == "ValueTask") &&
        !IsFrameworkTaskLike(named);

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
            named.ContainingNamespace?.ToDisplayString() != SystemThreadingTasks ||
            !IsFrameworkTaskLike(named))
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
        if (returnType is not INamedTypeSymbol named ||
            named.ContainingNamespace?.ToDisplayString() != SystemThreadingTasks ||
            !IsFrameworkTaskLike(named))
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

    internal static bool IsFrameworkTaskLike(INamedTypeSymbol type)
    {
        if (type.ContainingAssembly is not { } assembly)
        {
            return false;
        }

        var token = assembly.Identity.PublicKeyToken;
        return type.Locations.Any(static location => location.IsInMetadata) &&
               token.Length == 8 &&
               (TokenEquals(token, 0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e) ||
                TokenEquals(token, 0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89) ||
                TokenEquals(token, 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a) ||
                TokenEquals(token, 0xcc, 0x7b, 0x13, 0xff, 0xcd, 0x2d, 0xdd, 0x51));
    }

    private static bool TokenEquals(
        System.Collections.Immutable.ImmutableArray<byte> token,
        byte b0,
        byte b1,
        byte b2,
        byte b3,
        byte b4,
        byte b5,
        byte b6,
        byte b7) =>
        token[0] == b0 && token[1] == b1 && token[2] == b2 && token[3] == b3 &&
        token[4] == b4 && token[5] == b5 && token[6] == b6 && token[7] == b7;

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
            named.ContainingNamespace?.ToDisplayString() != SystemCollectionsGeneric ||
            !IsFrameworkAsyncEnumerable(named))
        {
            return false;
        }

        itemType = named.TypeArguments[0];
        return true;
    }

    private static bool IsFrameworkAsyncEnumerable(INamedTypeSymbol type)
    {
        var token = type.ContainingAssembly.Identity.PublicKeyToken;
        return type.Locations.Any(static location => location.IsInMetadata) &&
               token.Length == 8 &&
               TokenEquals(token, 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a);
    }

    public static bool IsStream(ITypeSymbol type) =>
        type is INamedTypeSymbol named &&
        named.Name == "Stream" &&
        named.ContainingNamespace?.ToDisplayString() == SystemIO &&
        IsFrameworkStreamingType(named);

    public static bool IsPipe(ITypeSymbol type) =>
        type is INamedTypeSymbol named &&
        named.Name == "Pipe" &&
        named.ContainingNamespace?.ToDisplayString() == SystemIOPipelines &&
        IsFrameworkStreamingType(named);

    private static bool IsFrameworkStreamingType(INamedTypeSymbol type)
    {
        var assembly = type.ContainingAssembly;
        var token = assembly.Identity.PublicKeyToken;
        return type.Locations.Any(static location => location.IsInMetadata) &&
               ((token.Length == 8 &&
                 (TokenEquals(token, 0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e) ||
                  TokenEquals(token, 0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89) ||
                  TokenEquals(token, 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a) ||
                  TokenEquals(token, 0xcc, 0x7b, 0x13, 0xff, 0xcd, 0x2d, 0xdd, 0x51))) ||
                (token.Length == 0 && assembly.Identity.Name == "System.IO.Pipelines"));
    }

    internal static bool TryGetSubServiceInfo(ITypeSymbol type, CancellationToken ct, out SubServiceInfo info)
        => SubServiceReturnTypeReader.TryGetSubServiceInfo(type, ct, out info);
}
