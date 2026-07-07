using System;
using System.Collections.Generic;
using DotBoxD.Services.SourceGenerator.Infrastructure;

namespace DotBoxD.Services.SourceGenerator.Models;

/// <summary>Shared helpers used by both the proxy and dispatcher emitters.</summary>
internal static class NamingHelpers
{
    private static readonly Dictionary<MethodReturnKind, Func<string?, string>> DeclaredReturnTypeFormatters = new()
    {
        [MethodReturnKind.Void] = static _ => "void",
        [MethodReturnKind.Sync] = static type => type!,
        [MethodReturnKind.SyncSubService] = static type => type!,
        [MethodReturnKind.Task] = static _ => ServicesGeneratorTypeNames.GlobalTask,
        [MethodReturnKind.TaskOf] = static type => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalTask, type!),
        [MethodReturnKind.ValueTask] = static _ => ServicesGeneratorTypeNames.GlobalValueTask,
        [MethodReturnKind.ValueTaskOf] = static type => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalValueTask, type!),
        [MethodReturnKind.TaskOfSubService] = static type => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalTask, type!),
        [MethodReturnKind.ValueTaskOfSubService] = static type => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalValueTask, type!),
        [MethodReturnKind.AsyncEnumerable] = static type => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalIAsyncEnumerable, type!),
        [MethodReturnKind.TaskOfAsyncEnumerable] = static type => ServicesGeneratorTypeNames.Generic(
            ServicesGeneratorTypeNames.GlobalTask,
            ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalIAsyncEnumerable, type!)),
        [MethodReturnKind.ValueTaskOfAsyncEnumerable] = static type => ServicesGeneratorTypeNames.Generic(
            ServicesGeneratorTypeNames.GlobalValueTask,
            ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalIAsyncEnumerable, type!)),
        [MethodReturnKind.Stream] = static _ => ServicesGeneratorTypeNames.GlobalStream,
        [MethodReturnKind.TaskOfStream] = static _ =>
            ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalTask, ServicesGeneratorTypeNames.GlobalStream),
        [MethodReturnKind.ValueTaskOfStream] = static _ =>
            ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalValueTask, ServicesGeneratorTypeNames.GlobalStream),
        [MethodReturnKind.Pipe] = static _ => ServicesGeneratorTypeNames.GlobalPipe,
        [MethodReturnKind.TaskOfPipe] = static _ =>
            ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalTask, ServicesGeneratorTypeNames.GlobalPipe),
        [MethodReturnKind.ValueTaskOfPipe] = static _ =>
            ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalValueTask, ServicesGeneratorTypeNames.GlobalPipe),
    };

    private static readonly HashSet<MethodReturnKind> AsyncReturnKinds =
    [
        MethodReturnKind.Task,
        MethodReturnKind.TaskOf,
        MethodReturnKind.ValueTask,
        MethodReturnKind.ValueTaskOf,
        MethodReturnKind.TaskOfSubService,
        MethodReturnKind.ValueTaskOfSubService,
        MethodReturnKind.AsyncEnumerable,
        MethodReturnKind.TaskOfAsyncEnumerable,
        MethodReturnKind.ValueTaskOfAsyncEnumerable,
        MethodReturnKind.TaskOfStream,
        MethodReturnKind.ValueTaskOfStream,
        MethodReturnKind.TaskOfPipe,
        MethodReturnKind.ValueTaskOfPipe,
    ];

    private static readonly HashSet<MethodReturnKind> ValueReturnKinds =
    [
        MethodReturnKind.Sync,
        MethodReturnKind.SyncSubService,
        MethodReturnKind.TaskOf,
        MethodReturnKind.ValueTaskOf,
        MethodReturnKind.TaskOfSubService,
        MethodReturnKind.ValueTaskOfSubService,
        MethodReturnKind.AsyncEnumerable,
        MethodReturnKind.TaskOfAsyncEnumerable,
        MethodReturnKind.ValueTaskOfAsyncEnumerable,
        MethodReturnKind.Stream,
        MethodReturnKind.TaskOfStream,
        MethodReturnKind.ValueTaskOfStream,
        MethodReturnKind.Pipe,
        MethodReturnKind.TaskOfPipe,
        MethodReturnKind.ValueTaskOfPipe,
    ];

    /// <summary>
    /// Strips a leading <c>I</c> if it is followed by an uppercase letter (the C# convention
    /// for interface names). Avoids accidentally stripping the <c>I</c> from names like
    /// <c>Identity</c> or <c>Internal</c>.
    /// </summary>
    public static string StripInterfacePrefix(string interfaceName)
    {
        if (interfaceName.Length > 1 && interfaceName[0] == 'I' && char.IsUpper(interfaceName[1]))
        {
            return interfaceName.Substring(1);
        }

        return interfaceName;
    }

    /// <summary>
    /// Reconstructs the literal return-type text as it would appear on the user's interface
    /// declaration, so the generated proxy signature exactly matches.
    /// </summary>
    public static string GetDeclaredReturnTypeText(MethodReturnKind kind, string? unwrappedReturnType)
        => DeclaredReturnTypeFormatters.TryGetValue(kind, out var format)
            ? format(unwrappedReturnType)
            : "void";

    public static bool IsAsync(MethodReturnKind kind) => AsyncReturnKinds.Contains(kind);

    public static bool HasReturnValue(MethodReturnKind kind) => ValueReturnKinds.Contains(kind);

    public static bool IsSubServiceReturn(MethodReturnKind kind) =>
        kind == MethodReturnKind.SyncSubService ||
        kind == MethodReturnKind.TaskOfSubService ||
        kind == MethodReturnKind.ValueTaskOfSubService;

    public static bool IsStreamReturn(MethodReturnKind kind) =>
        kind == MethodReturnKind.Stream ||
        kind == MethodReturnKind.TaskOfStream ||
        kind == MethodReturnKind.ValueTaskOfStream;

    public static bool IsPipeReturn(MethodReturnKind kind) =>
        kind == MethodReturnKind.Pipe ||
        kind == MethodReturnKind.TaskOfPipe ||
        kind == MethodReturnKind.ValueTaskOfPipe;

    public static bool IsAsyncEnumerableReturn(MethodReturnKind kind) =>
        kind == MethodReturnKind.AsyncEnumerable ||
        kind == MethodReturnKind.TaskOfAsyncEnumerable ||
        kind == MethodReturnKind.ValueTaskOfAsyncEnumerable;

    public static string AsyncSiblingInterfaceName(string interfaceName) =>
        interfaceName.EndsWith("Async", System.StringComparison.Ordinal)
            ? interfaceName
            : interfaceName + "Async";

    public static bool CanGenerateAsyncSiblingInterface(string interfaceName) =>
        !interfaceName.EndsWith("Async", System.StringComparison.Ordinal);

    public static string AsyncSiblingMethodName(string name)
    {
        var unescapedName = IdentifierHelpers.UnescapeIdentifier(name);
        var siblingName = unescapedName.EndsWith("Async", System.StringComparison.Ordinal)
            ? unescapedName
            : unescapedName + "Async";
        return IdentifierHelpers.EscapeIdentifier(siblingName);
    }
}
