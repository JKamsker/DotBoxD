using DotBoxD.Services.SourceGenerator.Infrastructure;

namespace DotBoxD.Services.SourceGenerator.Models;

/// <summary>Shared helpers used by both the proxy and dispatcher emitters.</summary>
internal static class NamingHelpers
{
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
    {
        return kind switch
        {
            MethodReturnKind.Void => "void",
            MethodReturnKind.Sync => unwrappedReturnType!,
            MethodReturnKind.SyncSubService => unwrappedReturnType!,
            MethodReturnKind.Task => ServicesGeneratorTypeNames.GlobalTask,
            MethodReturnKind.TaskOf => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalTask, unwrappedReturnType!),
            MethodReturnKind.ValueTask => ServicesGeneratorTypeNames.GlobalValueTask,
            MethodReturnKind.ValueTaskOf => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalValueTask, unwrappedReturnType!),
            MethodReturnKind.TaskOfSubService => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalTask, unwrappedReturnType!),
            MethodReturnKind.ValueTaskOfSubService => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalValueTask, unwrappedReturnType!),
            MethodReturnKind.AsyncEnumerable => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalIAsyncEnumerable, unwrappedReturnType!),
            MethodReturnKind.TaskOfAsyncEnumerable => ServicesGeneratorTypeNames.Generic(
                ServicesGeneratorTypeNames.GlobalTask,
                ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalIAsyncEnumerable, unwrappedReturnType!)),
            MethodReturnKind.ValueTaskOfAsyncEnumerable => ServicesGeneratorTypeNames.Generic(
                ServicesGeneratorTypeNames.GlobalValueTask,
                ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalIAsyncEnumerable, unwrappedReturnType!)),
            MethodReturnKind.Stream => ServicesGeneratorTypeNames.GlobalStream,
            MethodReturnKind.TaskOfStream => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalTask, ServicesGeneratorTypeNames.GlobalStream),
            MethodReturnKind.ValueTaskOfStream => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalValueTask, ServicesGeneratorTypeNames.GlobalStream),
            MethodReturnKind.Pipe => ServicesGeneratorTypeNames.GlobalPipe,
            MethodReturnKind.TaskOfPipe => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalTask, ServicesGeneratorTypeNames.GlobalPipe),
            MethodReturnKind.ValueTaskOfPipe => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalValueTask, ServicesGeneratorTypeNames.GlobalPipe),
            _ => "void",
        };
    }

    public static bool IsAsync(MethodReturnKind kind) =>
        kind == MethodReturnKind.Task ||
        kind == MethodReturnKind.TaskOf ||
        kind == MethodReturnKind.ValueTask ||
        kind == MethodReturnKind.ValueTaskOf ||
        kind == MethodReturnKind.TaskOfSubService ||
        kind == MethodReturnKind.ValueTaskOfSubService ||
        kind == MethodReturnKind.AsyncEnumerable ||
        kind == MethodReturnKind.TaskOfAsyncEnumerable ||
        kind == MethodReturnKind.ValueTaskOfAsyncEnumerable ||
        kind == MethodReturnKind.TaskOfStream ||
        kind == MethodReturnKind.ValueTaskOfStream ||
        kind == MethodReturnKind.TaskOfPipe ||
        kind == MethodReturnKind.ValueTaskOfPipe;

    public static bool HasReturnValue(MethodReturnKind kind) =>
        kind == MethodReturnKind.Sync ||
        kind == MethodReturnKind.SyncSubService ||
        kind == MethodReturnKind.TaskOf ||
        kind == MethodReturnKind.ValueTaskOf ||
        kind == MethodReturnKind.TaskOfSubService ||
        kind == MethodReturnKind.ValueTaskOfSubService ||
        kind == MethodReturnKind.AsyncEnumerable ||
        kind == MethodReturnKind.TaskOfAsyncEnumerable ||
        kind == MethodReturnKind.ValueTaskOfAsyncEnumerable ||
        kind == MethodReturnKind.Stream ||
        kind == MethodReturnKind.TaskOfStream ||
        kind == MethodReturnKind.ValueTaskOfStream ||
        kind == MethodReturnKind.Pipe ||
        kind == MethodReturnKind.TaskOfPipe ||
        kind == MethodReturnKind.ValueTaskOfPipe;

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
