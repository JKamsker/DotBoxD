using DotBoxD.Services.SourceGenerator.Infrastructure;

namespace DotBoxD.Services.SourceGenerator.Models;

/// <summary>
/// Classifies the return shape of an RPC-facing method as declared on the user's interface.
/// </summary>
internal enum MethodReturnKind
{
    /// <summary><c>void</c></summary>
    Void,
    /// <summary>A non-<see cref="System.Threading.Tasks.Task"/> / non-<see cref="System.Threading.Tasks.ValueTask"/> return — synchronous T.</summary>
    Sync,
    /// <summary>Non-generic <see cref="System.Threading.Tasks.Task"/> — async, no payload.</summary>
    Task,
    /// <summary><see cref="System.Threading.Tasks.Task{TResult}"/> — async with payload.</summary>
    TaskOf,
    /// <summary>Non-generic <see cref="System.Threading.Tasks.ValueTask"/> — async, no payload.</summary>
    ValueTask,
    /// <summary><see cref="System.Threading.Tasks.ValueTask{TResult}"/> — async with payload.</summary>
    ValueTaskOf,
    /// <summary><see cref="System.Threading.Tasks.Task{TResult}"/> where <c>TResult</c> is itself a <c>[DotBoxDService]</c> interface — nested sub-service.</summary>
    TaskOfSubService,
    /// <summary><see cref="System.Threading.Tasks.ValueTask{TResult}"/> where <c>TResult</c> is itself a <c>[DotBoxDService]</c> interface — nested sub-service.</summary>
    ValueTaskOfSubService,
    /// <summary><c>IAsyncEnumerable&lt;T&gt;</c> streamed item-by-item.</summary>
    AsyncEnumerable,
    /// <summary><see cref="System.Threading.Tasks.Task{TResult}"/> whose result is <c>IAsyncEnumerable&lt;T&gt;</c>.</summary>
    TaskOfAsyncEnumerable,
    /// <summary><see cref="System.Threading.Tasks.ValueTask{TResult}"/> whose result is <c>IAsyncEnumerable&lt;T&gt;</c>.</summary>
    ValueTaskOfAsyncEnumerable,
    /// <summary><see cref="System.IO.Stream"/> streamed as bytes.</summary>
    Stream,
    /// <summary><see cref="System.Threading.Tasks.Task{TResult}"/> whose result is <see cref="System.IO.Stream"/>.</summary>
    TaskOfStream,
    /// <summary><see cref="System.Threading.Tasks.ValueTask{TResult}"/> whose result is <see cref="System.IO.Stream"/>.</summary>
    ValueTaskOfStream,
    /// <summary><c>Pipe</c> streamed as bytes.</summary>
    Pipe,
    /// <summary><see cref="System.Threading.Tasks.Task{TResult}"/> whose result is <c>Pipe</c>.</summary>
    TaskOfPipe,
    /// <summary><see cref="System.Threading.Tasks.ValueTask{TResult}"/> whose result is <c>Pipe</c>.</summary>
    ValueTaskOfPipe,
}

internal enum ParameterStreamKind
{
    None,
    Stream,
    Pipe,
    AsyncEnumerable,
}

/// <summary>
/// Information needed to wire a method returning a nested sub-service: the fully-qualified
/// interface name (so the proxy can construct a sibling proxy) and the RPC service name
/// (so the wire instance dispatch hits the right registry slot).
/// </summary>
internal sealed record SubServiceInfo(string QualifiedInterfaceName, string ServiceName, bool AllowsNull);

/// <summary>
/// Immutable, value-equatable representation of a DotBoxD service.
/// </summary>
internal sealed record ServiceModel(
    string Namespace,
    string InterfaceName,
    string ServiceName,
    EquatableArray<MethodModel> Methods,
    EquatableArray<ServicePropertyModel> Properties,
    string RawServiceName = "");

/// <summary>Immutable, value-equatable representation of a get-only sub-service property.</summary>
internal sealed record ServicePropertyModel(
    string Name,
    string Type,
    string ProxyType);

/// <summary>
/// Method-insensitive shape used by the aggregate extension generator. A method rename should
/// regenerate the per-service proxy/dispatcher and metadata, but not the peer extension helpers.
/// </summary>
internal sealed record ServiceExtensionModel(
    string Namespace,
    string InterfaceName,
    string ServiceName,
    EquatableArray<ServicePropertyModel> Properties)
{
    public static ServiceExtensionModel From(ServiceModel service) =>
        new(
            service.Namespace,
            service.InterfaceName,
            service.ServiceName,
            service.Properties);
}

/// <summary>
/// Immutable, value-equatable representation of a service method. When
/// <see cref="UnsupportedReason"/> is non-null the method shape cannot be marshalled
/// over RPC, but the proxy class still has to implement the interface — so the proxy
/// emits a throwing stub and the dispatcher omits a switch case.
/// </summary>
internal sealed record MethodModel(
    string Name,
    string ExplicitImplementationType,
    string RpcName,
    MethodReturnKind ReturnKind,
    string DeclaredReturnType,
    string? UnwrappedReturnType,
    string ReturnRefKindKeyword,
    bool HasCancellationToken,
    EquatableArray<ParameterModel> Parameters,
    EquatableArray<string> AdditionalExplicitImplementationTypes,
    bool RequiresUnsafeSignature = false,
    int TypeParameterCount = 0,
    string TypeParameterList = "",
    string ConstraintClauses = "",
    bool RequiresDispatcherReceiverCast = false,
    string? UnsupportedReason = null,
    SubServiceInfo? SubService = null,
    string RawRpcName = "",
    string MetadataReturnType = "",
    string? MetadataResultType = null);

/// <summary>
/// Immutable, value-equatable representation of a method parameter.
/// <see cref="IsCancellationToken"/> marks parameters that are part of the user's
/// signature but are not serialized into the RPC payload.
/// <see cref="RefKindKeyword"/> holds the C# modifier text (<c>""</c>, <c>"ref "</c>,
/// <c>"in "</c>, or <c>"out "</c>).
/// <see cref="DefaultValueLiteral"/> holds the C# literal text of a non-cancellation-token
/// parameter's explicit default value (e.g. <c>"\"x\""</c>, <c>"5"</c>, <c>"null"</c>), so the
/// generated proxy and async-sibling signatures preserve it; empty when there is no default or it
/// cannot be expressed as a literal. Cancellation-token defaults are emitted as <c>= default</c>.
/// </summary>
internal sealed record ParameterModel(
    string Name,
    string Type,
    string SignatureType,
    string RefKindKeyword = "",
    bool IsCancellationToken = false,
    bool HasDefaultValue = false,
    string DefaultValueLiteral = "",
    ParameterStreamKind StreamKind = ParameterStreamKind.None,
    string? StreamItemType = null,
    string MetadataType = "");

/// <summary>
/// A <see cref="ServiceModel"/> paired with its computed async-sibling projection. Lives
/// as one value-equatable record so the per-service source-output step can be driven
/// from a single input without losing incrementality.
/// </summary>
internal sealed record ServiceBundle(
    ServiceModel Model,
    EquatableArray<AsyncSiblingMethod> SiblingMethods)
{
    public static ServiceBundle Empty(ServiceModel model) =>
        new(
            model,
            EquatableArray<AsyncSiblingMethod>.Empty);
}

internal sealed record ServiceProjection(
    ServiceBundle Bundle,
    EquatableArray<MethodDiagnostic> SiblingCollisions);

/// <summary>
/// Shape of one method as it should appear on the auto-generated async sibling interface.
/// </summary>
internal sealed record AsyncSiblingMethod(
    int SourceIndex,
    // Method name on the sibling (e.g. "Add" -> "AddAsync").
    string Name,
    // Original method this row was derived from — used by the proxy emitter to
    // pick the wire call shape and to suppress duplicate emission when the sibling row
    // is identical to the original method.
    MethodModel Source,
    // The return kind on the sibling — always Task / TaskOf / ValueTask / ValueTaskOf;
    // sync methods are projected onto MethodReturnKind.Task or
    // MethodReturnKind.TaskOf depending on whether they carry a payload.
    MethodReturnKind SiblingReturnKind,
    // Parameter list emitted on the sibling interface.
    EquatableArray<ParameterModel> Parameters,
    // True when this row materially differs from Source — i.e.
    // the proxy needs an extra method to satisfy the sibling interface. False when one
    // physical method on the proxy satisfies both interfaces (already-async methods
    // with the same name and signature).
    bool RequiresExtraProxyMethod);

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
            MethodReturnKind.Task => ServicesGeneratorTypeNames.GlobalTask,
            MethodReturnKind.TaskOf => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalTask, unwrappedReturnType!),
            MethodReturnKind.ValueTask => ServicesGeneratorTypeNames.GlobalValueTask,
            MethodReturnKind.ValueTaskOf => ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalValueTask, unwrappedReturnType!),
            // Sub-service returns surface as Task<TInterface>/ValueTask<TInterface> on the
            // interface; the proxy's body short-circuits to a generated sub-proxy.
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

    /// <summary>
    /// Returns true if the return kind represents an asynchronous return that should be
    /// awaited and emitted with the <c>async</c> keyword.
    /// </summary>
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

    /// <summary>
    /// Returns true if the return kind carries a response payload (a generic Task/ValueTask of T
    /// or a synchronous T) — i.e. the underlying wire call must deserialize a payload.
    /// </summary>
    public static bool HasReturnValue(MethodReturnKind kind) =>
        kind == MethodReturnKind.Sync ||
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

    /// <summary>True for the two sub-service-returning kinds.</summary>
    public static bool IsSubServiceReturn(MethodReturnKind kind) =>
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

    /// <summary>
    /// Name of the auto-generated async sibling interface for <paramref name="interfaceName"/>.
    /// e.g. <c>"IFoo"</c> → <c>"IFooAsync"</c>, <c>"Foo"</c> → <c>"FooAsync"</c>. Falls back
    /// to appending only when the source name does not already end in <c>"Async"</c>.
    /// </summary>
    public static string AsyncSiblingInterfaceName(string interfaceName) =>
        interfaceName.EndsWith("Async", System.StringComparison.Ordinal)
            ? interfaceName
            : interfaceName + "Async";

    /// <summary>
    /// Returns true when the generated async sibling would have a distinct type name.
    /// Services whose own interface name already ends in <c>Async</c> cannot safely get
    /// a sibling because the sibling type would collide with the user-declared service.
    /// </summary>
    public static bool CanGenerateAsyncSiblingInterface(string interfaceName) =>
        !interfaceName.EndsWith("Async", System.StringComparison.Ordinal);

    /// <summary>
    /// Projects a method name onto its async sibling form. Already-Async names are unchanged,
    /// otherwise the suffix is appended.
    /// </summary>
    public static string AsyncSiblingMethodName(string name)
    {
        var unescapedName = IdentifierHelpers.UnescapeIdentifier(name);
        var siblingName = unescapedName.EndsWith("Async", System.StringComparison.Ordinal)
            ? unescapedName
            : unescapedName + "Async";
        return IdentifierHelpers.EscapeIdentifier(siblingName);
    }
}
