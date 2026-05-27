using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ShaRPC.SourceGenerator;

/// <summary>
/// Incremental source generator for ShaRPC client proxies and server dispatchers.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ShaRpcGenerator : IIncrementalGenerator
{
    private const string ShaRpcServiceAttributeName = "ShaRPC.Core.Attributes.ShaRpcServiceAttribute";
    private const string ShaRpcMethodAttributeName = "ShaRPC.Core.Attributes.ShaRpcMethodAttribute";
    private const string CancellationTokenFullName = "System.Threading.CancellationToken";
    private const string SystemThreadingTasks = "System.Threading.Tasks";

    /// <summary>Display format that emits fully-qualified <c>global::</c> type names so
    /// generated code never depends on the user's <c>using</c> set.</summary>
    private static readonly SymbolDisplayFormat s_qualifiedFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    private static readonly DiagnosticDescriptor s_generatorErrorRule = new(
        id: "SHARPC001",
        title: "ShaRPC source generator error",
        messageFormat: "ShaRPC failed to generate for '{0}': {1}",
        category: "ShaRPC.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_unsupportedMethodRule = new(
        id: "SHARPC002",
        title: "Unsupported ShaRPC method shape",
        messageFormat: "ShaRPC cannot generate code for '{0}.{1}': {2}",
        category: "ShaRPC.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_unsupportedServiceRule = new(
        id: "SHARPC003",
        title: "Unsupported ShaRPC service shape",
        messageFormat: "ShaRPC cannot generate code for service '{0}': {1}",
        category: "ShaRPC.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_asyncSiblingCollisionRule = new(
        id: "SHARPC004",
        title: "ShaRPC async sibling method name collides",
        messageFormat: "ShaRPC cannot project '{0}.{1}' onto its async sibling: {2}",
        category: "ShaRPC.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal readonly record struct ServiceResult(
        ServiceModel? Model,
        GeneratorError? Error,
        EquatableArray<MethodDiagnostic> MethodDiagnostics,
        ServiceDiagnostic? ServiceDiagnostic);

    internal readonly record struct GeneratorError(string Where, string Message);

    internal readonly record struct DiagnosticLocation(
        string FilePath,
        int Start,
        int Length,
        int StartLine,
        int StartCharacter,
        int EndLine,
        int EndCharacter)
    {
        public static DiagnosticLocation FromLocation(Location? location)
        {
            if (location is null || !location.IsInSource)
            {
                return default;
            }

            var lineSpan = location.GetLineSpan();
            return new DiagnosticLocation(
                lineSpan.Path,
                location.SourceSpan.Start,
                location.SourceSpan.Length,
                lineSpan.StartLinePosition.Line,
                lineSpan.StartLinePosition.Character,
                lineSpan.EndLinePosition.Line,
                lineSpan.EndLinePosition.Character);
        }

        public Location ToLocation()
        {
            if (Length <= 0)
            {
                return Location.None;
            }

            return Location.Create(
                FilePath,
                new TextSpan(Start, Length),
                new LinePositionSpan(
                    new LinePosition(StartLine, StartCharacter),
                    new LinePosition(EndLine, EndCharacter)));
        }
    }

    /// <summary>Diagnostic about one method (SHARPC002) — emitted while still producing the rest of the service.</summary>
    internal readonly record struct MethodDiagnostic(
        string InterfaceName,
        string MethodName,
        string Reason,
        DiagnosticLocation Location = default);

    /// <summary>Diagnostic about the service as a whole (SHARPC003) — service is skipped entirely.</summary>
    internal readonly record struct ServiceDiagnostic(
        string InterfaceName,
        string Reason,
        DiagnosticLocation Location = default);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ShaRpcServiceAttributeName,
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, ct) => GetServiceResult(ctx, ct))
            .WithTrackingName("ServiceResults");

        var errors = results
            .Where(static r => r.Error is not null)
            .Select(static (r, _) => r.Error!.Value)
            .WithTrackingName("ServiceErrors");

        context.RegisterSourceOutput(errors, static (spc, error) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_generatorErrorRule,
                Location.None,
                error.Where,
                error.Message)));

        // SHARPC002 — per-method diagnostics for shapes ShaRPC cannot generate (ref/in/out).
        var methodDiagnostics = results
            .SelectMany(static (r, _) => r.MethodDiagnostics.Array)
            .WithTrackingName("MethodDiagnostics");

        context.RegisterSourceOutput(methodDiagnostics, static (spc, d) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_unsupportedMethodRule,
                d.Location.ToLocation(),
                d.InterfaceName,
                d.MethodName,
                d.Reason)));

        // SHARPC003 — service-level diagnostics (generic / nested service interfaces).
        var serviceDiagnostics = results
            .Where(static r => r.ServiceDiagnostic is not null)
            .Select(static (r, _) => r.ServiceDiagnostic!.Value)
            .WithTrackingName("ServiceDiagnostics");

        context.RegisterSourceOutput(serviceDiagnostics, static (spc, d) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_unsupportedServiceRule,
                d.Location.ToLocation(),
                d.InterfaceName,
                d.Reason)));

        var models = results
            .Where(static r => r.Model is not null)
            .Select(static (r, _) => r.Model!)
            .WithTrackingName("Services");

        // Bundle each model with its async-sibling projection + collision diagnostics so
        // every downstream step (per-service source output, SHARPC004 diagnostics) flows
        // through one value-equatable record.
        var bundles = models
            .Select(static (m, ct) =>
            {
                if (!NamingHelpers.CanGenerateAsyncSiblingInterface(m.InterfaceName))
                {
                    return ServiceBundle.Empty(m);
                }

                var (siblings, collisions) = ComputeAsyncSiblingMethods(m, ct);
                return new ServiceBundle(m, siblings, collisions);
            })
            .WithTrackingName("ServiceBundles");

        // SHARPC004 — async-sibling naming collision warnings.
        var siblingCollisions = bundles
            .SelectMany(static (b, _) => b.SiblingCollisions.Array)
            .WithTrackingName("SiblingCollisions");

        context.RegisterSourceOutput(siblingCollisions, static (spc, d) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_asyncSiblingCollisionRule,
                d.Location.ToLocation(),
                d.InterfaceName,
                d.MethodName,
                d.Reason)));

        context.RegisterSourceOutput(bundles, static (spc, bundle) =>
        {
            try
            {
                var hintPrefix = HintNamePrefix(bundle.Model);
                var ct = spc.CancellationToken;
                var proxySource = ProxyGenerator.Generate(bundle.Model, bundle.SiblingMethods, ct);
                spc.AddSource(
                    $"{hintPrefix}.ShaRpcProxy.g.cs",
                    SourceText.From(proxySource, Encoding.UTF8));

                var dispatcherSource = DispatcherGenerator.Generate(bundle.Model, ct);
                spc.AddSource(
                    $"{hintPrefix}.ShaRpcDispatcher.g.cs",
                    SourceText.From(dispatcherSource, Encoding.UTF8));

                if (!bundle.SiblingMethods.IsEmpty)
                {
                    var asyncSource = AsyncInterfaceGenerator.Generate(bundle.Model, bundle.SiblingMethods, ct);
                    spc.AddSource(
                        $"{hintPrefix}.ShaRpcAsync.g.cs",
                        SourceText.From(asyncSource, Encoding.UTF8));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    s_generatorErrorRule,
                    Location.None,
                    bundle.Model.InterfaceName,
                    ex.ToString()));
            }
        });

        var allServices = models
            .Collect()
            .Select(static (arr, _) => arr
                .OrderBy(static service => service.Namespace, StringComparer.Ordinal)
                .ThenBy(static service => service.InterfaceName, StringComparer.Ordinal)
                .ThenBy(static service => service.ServiceName, StringComparer.Ordinal)
                .ToEquatableArray())
            .WithTrackingName("AllServices");

        context.RegisterSourceOutput(allServices, static (spc, services) =>
        {
            if (services.IsEmpty)
            {
                return;
            }

            try
            {
                var extensionsSource = GenerateExtensions(services, spc.CancellationToken);
                spc.AddSource(
                    "ShaRpcExtensions.g.cs",
                    SourceText.From(extensionsSource, Encoding.UTF8));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    s_generatorErrorRule,
                    Location.None,
                    "ShaRpcExtensions",
                    ex.ToString()));
            }
        });
    }

    /// <summary>
    /// Builds the hint-name prefix for the per-service generated files. Includes the
    /// namespace so two services that share a simple interface name (e.g. <c>A.IFoo</c>
    /// and <c>B.IFoo</c>) don't collide on <see cref="SourceProductionContext.AddSource"/>.
    /// Dots are replaced with underscores because hint names are typically used as file
    /// names by IDEs and some surfaces don't tolerate multiple dots in a filename stem.
    /// </summary>
    internal static string HintNamePrefix(ServiceModel model)
    {
        if (string.IsNullOrEmpty(model.Namespace))
        {
            return model.InterfaceName;
        }
        return NamespaceIdentifierPrefix(model.Namespace) + "_" + model.InterfaceName;
    }

    private static string NamespaceIdentifierPrefix(string namespaceName)
    {
        var flattened = namespaceName.Replace('.', '_');
        if (namespaceName.IndexOf('_') < 0)
        {
            return flattened;
        }

        return flattened + "__" + StableHash(namespaceName);
    }

    private static string StableHash(string value)
    {
        unchecked
        {
            ulong hash = 14695981039346656037;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 1099511628211;
            }

            return hash.ToString("x16");
        }
    }

    private static ServiceResult GetServiceResult(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        try
        {
            return BuildServiceResult(context, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var name = context.TargetSymbol?.ToDisplayString() ?? "<unknown>";
            return new ServiceResult(
                Model: null,
                Error: new GeneratorError(name, ex.ToString()),
                MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
                ServiceDiagnostic: null);
        }
    }

    private static ServiceResult BuildServiceResult(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol interfaceSymbol)
        {
            return default;
        }

        var displayName = interfaceSymbol.ToDisplayString();
        var serviceLocation = GetSymbolLocation(interfaceSymbol);

        // SHARPC003 — reject generic service interfaces. The generated proxy/dispatcher
        // would need matching type parameters; supporting this fully is non-trivial and
        // out of scope today, so we surface a clear diagnostic instead of emitting broken
        // output silently.
        if (interfaceSymbol.IsGenericType)
        {
            return new ServiceResult(
                Model: null,
                Error: null,
                MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
                ServiceDiagnostic: new ServiceDiagnostic(
                    displayName,
                    "generic service interfaces are not supported; declare a non-generic interface and forward to a generic helper if needed",
                    serviceLocation));
        }

        // SHARPC003 — reject nested service interfaces (declared inside another type). The
        // generated proxy/dispatcher class would need to live inside that containing type
        // (which would have to be partial), which is a deeper refactor than today's scope.
        if (interfaceSymbol.ContainingType is not null)
        {
            return new ServiceResult(
                Model: null,
                Error: null,
                MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
                ServiceDiagnostic: new ServiceDiagnostic(
                    displayName,
                    "nested service interfaces are not supported; declare the interface at namespace scope",
                    serviceLocation));
        }

        if (interfaceSymbol.DeclaredAccessibility != Accessibility.Public)
        {
            return new ServiceResult(
                Model: null,
                Error: null,
                MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
                ServiceDiagnostic: new ServiceDiagnostic(
                    displayName,
                    "service interfaces must be public because generated proxy, dispatcher, and extension APIs are public",
                    serviceLocation));
        }

        var unsupportedMemberReason = GetUnsupportedMemberReason(interfaceSymbol);
        if (unsupportedMemberReason is not null)
        {
            return new ServiceResult(
                Model: null,
                Error: null,
                MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
                ServiceDiagnostic: new ServiceDiagnostic(displayName, unsupportedMemberReason, serviceLocation));
        }

        ct.ThrowIfCancellationRequested();

        // Pick the service attribute (already guaranteed to be present by ForAttributeWithMetadataName).
        string? customName = null;
        foreach (var attr in context.Attributes)
        {
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "Name" && namedArg.Value.Value is string s)
                {
                    customName = s;
                }
            }
        }

        var ns = interfaceSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : interfaceSymbol.ContainingNamespace.ToDisplayString();
        var interfaceName = interfaceSymbol.Name;
        var serviceName = customName ?? interfaceName;
        var cancellationTokenSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(CancellationTokenFullName);

        var methods = new List<MethodModel>();
        var methodLocations = new List<DiagnosticLocation>();
        var methodDiagnostics = new List<MethodDiagnostic>();
        var seenSignatures = new HashSet<string>(StringComparer.Ordinal);

        // Walk the declaring interface AND every base interface, so methods inherited from
        // `IBar` in `interface IFoo : IBar` are also emitted on the FooProxy. Without this
        // step the generated proxy would fail to implement IFoo (CS0535).
        IEnumerable<IMethodSymbol> EnumerateMethods()
        {
            foreach (var member in interfaceSymbol.GetMembers())
            {
                if (member is IMethodSymbol m && m.MethodKind == MethodKind.Ordinary)
                {
                    yield return m;
                }
            }
            foreach (var baseInterface in interfaceSymbol.AllInterfaces)
            {
                foreach (var member in baseInterface.GetMembers())
                {
                    if (member is IMethodSymbol m && m.MethodKind == MethodKind.Ordinary)
                    {
                        yield return m;
                    }
                }
            }
        }

        foreach (var methodSymbol in EnumerateMethods())
        {
            ct.ThrowIfCancellationRequested();

            // De-duplicate in case a derived interface re-declares (via `new`) a base method
            // with the same signature shape.
            var sigKey = methodSymbol.Name + "`" + methodSymbol.Arity + "(" +
                string.Join(",", methodSymbol.Parameters.Select(p => p.RefKind + ":" + p.Type.ToDisplayString())) + ")";
            if (!seenSignatures.Add(sigKey))
            {
                continue;
            }

            string? customMethodName = null;
            foreach (var attr in methodSymbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == ShaRpcMethodAttributeName)
                {
                    foreach (var namedArg in attr.NamedArguments)
                    {
                        if (namedArg.Key == "Name" && namedArg.Value.Value is string s)
                        {
                            customMethodName = s;
                        }
                    }
                }
            }

            var returnType = methodSymbol.ReturnType;
            var returnKind = ClassifyReturnType(returnType, out var unwrappedReturnType, out var subService);
            var typeParameterList = GetTypeParameterList(methodSymbol);
            var constraintClauses = GetConstraintClauses(methodSymbol);
            string? unsupportedReason = GetUnsupportedServiceReturnReason(returnType);
            var methodLocation = GetSymbolLocation(methodSymbol);

            var parameters = new List<ParameterModel>();
            var hasCancellationToken = false;
            var cancellationTokenCount = 0;
            if (methodSymbol.IsGenericMethod)
            {
                unsupportedReason ??= "generic service methods are not supported; expose a non-generic RPC method instead";
            }

            foreach (var param in methodSymbol.Parameters)
            {
                var isCancellationToken = cancellationTokenSymbol is not null &&
                    SymbolEqualityComparer.Default.Equals(param.Type, cancellationTokenSymbol);

                if (isCancellationToken)
                {
                    cancellationTokenCount++;
                    hasCancellationToken = true;
                }

                if (param.RefKind != RefKind.None)
                {
                    unsupportedReason ??=
                        $"parameter '{param.Name}' uses an unsupported pass-by-reference kind '{param.RefKind.ToString().ToLowerInvariant()}'";
                }

                parameters.Add(new ParameterModel(
                    EscapeIdentifier(param.Name),
                    param.Type.ToDisplayString(s_qualifiedFormat),
                    RefKindKeyword(param.RefKind),
                    isCancellationToken,
                    param.HasExplicitDefaultValue));
            }

            if (cancellationTokenCount > 1)
            {
                unsupportedReason ??= "multiple CancellationToken parameters are not supported";
            }

            if (unsupportedReason is not null)
            {
                methodDiagnostics.Add(new MethodDiagnostic(
                    displayName,
                    methodSymbol.Name,
                    unsupportedReason,
                    methodLocation));
            }

            methods.Add(new MethodModel(
                Name: EscapeIdentifier(methodSymbol.Name),
                RpcName: EscapeStringLiteral(customMethodName ?? methodSymbol.Name),
                ReturnKind: returnKind,
                UnwrappedReturnType: unwrappedReturnType,
                HasCancellationToken: hasCancellationToken,
                Parameters: parameters.ToEquatableArray(),
                TypeParameterList: typeParameterList,
                ConstraintClauses: constraintClauses,
                UnsupportedReason: unsupportedReason,
                SubService: subService));
            methodLocations.Add(methodLocation);
        }

        MarkDuplicateWireNames(displayName, methods, methodLocations, methodDiagnostics);

        static string RefKindKeyword(RefKind kind) => kind switch
        {
            RefKind.Ref => "ref ",
            RefKind.In => "in ",
            RefKind.Out => "out ",
            _ => string.Empty,
        };

        return new ServiceResult(
            Model: new ServiceModel(
                Namespace: ns,
                InterfaceName: interfaceName,
                ServiceName: EscapeStringLiteral(serviceName),
                Methods: methods.ToEquatableArray()),
            Error: null,
            MethodDiagnostics: methodDiagnostics.ToEquatableArray(),
            ServiceDiagnostic: null);
    }

    private static DiagnosticLocation GetSymbolLocation(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource)
            {
                return DiagnosticLocation.FromLocation(location);
            }
        }

        return default;
    }

    private static string? GetUnsupportedMemberReason(INamedTypeSymbol interfaceSymbol)
    {
        foreach (var member in EnumerateInterfaceMembers(interfaceSymbol))
        {
            if (member is IPropertySymbol property)
            {
                return $"interface property '{property.Name}' is not supported; ShaRPC services may declare methods only";
            }

            if (member is IEventSymbol eventSymbol)
            {
                return $"interface event '{eventSymbol.Name}' is not supported; ShaRPC services may declare methods only";
            }

            if (member is IMethodSymbol method)
            {
                if (method.MethodKind == MethodKind.Ordinary && method.IsStatic)
                {
                    return $"static interface method '{method.Name}' is not supported; ShaRPC services may declare instance methods only";
                }

                if (method.MethodKind is not MethodKind.Ordinary and not MethodKind.PropertyGet
                    and not MethodKind.PropertySet and not MethodKind.EventAdd and not MethodKind.EventRemove)
                {
                    return $"interface member '{method.Name}' has unsupported method kind '{method.MethodKind}'";
                }
            }
        }

        return null;
    }

    private static IEnumerable<ISymbol> EnumerateInterfaceMembers(INamedTypeSymbol interfaceSymbol)
    {
        foreach (var member in interfaceSymbol.GetMembers())
        {
            yield return member;
        }

        foreach (var baseInterface in interfaceSymbol.AllInterfaces)
        {
            foreach (var member in baseInterface.GetMembers())
            {
                yield return member;
            }
        }
    }

    private static string GetTypeParameterList(IMethodSymbol method)
    {
        if (!method.IsGenericMethod)
        {
            return string.Empty;
        }

        return "<" + string.Join(", ", method.TypeParameters.Select(p => p.Name)) + ">";
    }

    private static string GetConstraintClauses(IMethodSymbol method)
    {
        if (!method.IsGenericMethod)
        {
            return string.Empty;
        }

        var clauses = new List<string>();
        foreach (var typeParameter in method.TypeParameters)
        {
            var constraints = new List<string>();
            if (typeParameter.HasReferenceTypeConstraint)
            {
                constraints.Add("class");
            }
            else if (typeParameter.HasUnmanagedTypeConstraint)
            {
                constraints.Add("unmanaged");
            }
            else if (typeParameter.HasValueTypeConstraint)
            {
                constraints.Add("struct");
            }
            else if (typeParameter.HasNotNullConstraint)
            {
                constraints.Add("notnull");
            }

            foreach (var constraintType in typeParameter.ConstraintTypes)
            {
                constraints.Add(constraintType.ToDisplayString(s_qualifiedFormat));
            }

            if (typeParameter.HasConstructorConstraint)
            {
                constraints.Add("new()");
            }

            if (constraints.Count > 0)
            {
                clauses.Add($" where {typeParameter.Name} : {string.Join(", ", constraints)}");
            }
        }

        return string.Concat(clauses);
    }

    private static string? GetUnsupportedServiceReturnReason(ITypeSymbol returnType)
    {
        if (IsShaRpcServiceInterface(returnType))
        {
            return "synchronous sub-service returns are not supported; return Task<TService> or ValueTask<TService>";
        }

        if (!TryGetAsyncPayloadType(returnType, out var payloadType) ||
            !IsShaRpcServiceInterface(payloadType))
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

    private static bool TryGetAsyncPayloadType(ITypeSymbol type, out ITypeSymbol payloadType)
    {
        payloadType = null!;
        if (type is not INamedTypeSymbol named || !named.IsGenericType)
        {
            return false;
        }

        var nsName = named.ContainingNamespace?.ToDisplayString();
        if (nsName != SystemThreadingTasks ||
            (named.Name != "Task" && named.Name != "ValueTask"))
        {
            return false;
        }

        payloadType = named.TypeArguments[0];
        return true;
    }

    private static bool IsShaRpcServiceInterface(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || named.TypeKind != TypeKind.Interface)
        {
            return false;
        }

        foreach (var attr in named.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == ShaRpcServiceAttributeName)
            {
                return true;
            }
        }

        return false;
    }

    private static void MarkDuplicateWireNames(
        string interfaceName,
        List<MethodModel> methods,
        List<DiagnosticLocation> methodLocations,
        List<MethodDiagnostic> methodDiagnostics)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            if (method.UnsupportedReason is not null)
            {
                continue;
            }

            counts.TryGetValue(method.RpcName, out var count);
            counts[method.RpcName] = count + 1;
        }

        for (var i = 0; i < methods.Count; i++)
        {
            var method = methods[i];
            if (method.UnsupportedReason is not null ||
                !counts.TryGetValue(method.RpcName, out var count) ||
                count < 2)
            {
                continue;
            }

            var reason =
                $"wire method name '{method.RpcName}' is used by multiple service methods; give each overload a distinct [ShaRpcMethod(Name = ...)] value";
            methods[i] = method with { UnsupportedReason = reason };
            methodDiagnostics.Add(new MethodDiagnostic(interfaceName, method.Name, reason, methodLocations[i]));
        }
    }

    /// <summary>
    /// Classifies a method return type into one of the <see cref="MethodReturnKind"/>
    /// variants and computes the unwrapped payload type (for generic Task/ValueTask, this
    /// is the type argument; for sync returns, it's the return type itself).
    /// </summary>
    private static MethodReturnKind ClassifyReturnType(
        ITypeSymbol returnType,
        out string? unwrappedReturnType,
        out SubServiceInfo? subService)
    {
        subService = null;

        if (returnType is INamedTypeSymbol named && named.IsGenericType)
        {
            var nsName = named.ContainingNamespace?.ToDisplayString();
            if (nsName == SystemThreadingTasks)
            {
                if (named.Name == "Task")
                {
                    var arg = named.TypeArguments[0];
                    unwrappedReturnType = arg.ToDisplayString(s_qualifiedFormat);
                    if (TryGetSubServiceInfo(arg, out var sub))
                    {
                        subService = sub;
                        return MethodReturnKind.TaskOfSubService;
                    }
                    return MethodReturnKind.TaskOf;
                }
                if (named.Name == "ValueTask")
                {
                    var arg = named.TypeArguments[0];
                    unwrappedReturnType = arg.ToDisplayString(s_qualifiedFormat);
                    if (TryGetSubServiceInfo(arg, out var sub))
                    {
                        subService = sub;
                        return MethodReturnKind.ValueTaskOfSubService;
                    }
                    return MethodReturnKind.ValueTaskOf;
                }
            }
        }

        var rNs = returnType.ContainingNamespace?.ToDisplayString();
        if (rNs == SystemThreadingTasks)
        {
            if (returnType.Name == "Task")
            {
                unwrappedReturnType = null;
                return MethodReturnKind.Task;
            }
            if (returnType.Name == "ValueTask")
            {
                unwrappedReturnType = null;
                return MethodReturnKind.ValueTask;
            }
        }

        if (returnType.SpecialType == SpecialType.System_Void)
        {
            unwrappedReturnType = null;
            return MethodReturnKind.Void;
        }

        unwrappedReturnType = returnType.ToDisplayString(s_qualifiedFormat);
        return MethodReturnKind.Sync;
    }

    /// <summary>
    /// Returns true if <paramref name="type"/> is itself decorated with
    /// <c>[ShaRpcService]</c>, in which case methods that return it should marshal a
    /// <c>ServiceHandle</c> rather than attempting to serialize the live object.
    /// </summary>
    private static bool TryGetSubServiceInfo(ITypeSymbol type, out SubServiceInfo info)
    {
        info = null!;
        if (type is not INamedTypeSymbol named || named.TypeKind != TypeKind.Interface)
        {
            return false;
        }

        if (named.IsGenericType || named.ContainingType is not null)
        {
            return false;
        }

        AttributeData? serviceAttr = null;
        foreach (var attr in named.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == ShaRpcServiceAttributeName)
            {
                serviceAttr = attr;
                break;
            }
        }
        if (serviceAttr is null) return false;

        string serviceName = named.Name;
        foreach (var arg in serviceAttr.NamedArguments)
        {
            if (arg.Key == "Name" && arg.Value.Value is string customName)
            {
                serviceName = customName;
            }
        }

        info = new SubServiceInfo(
            QualifiedInterfaceName: named.ToDisplayString(s_qualifiedFormat),
            ServiceName: EscapeStringLiteral(serviceName));
        return true;
    }

    /// <summary>
    /// Prefixes a C# identifier with <c>@</c> when it would otherwise collide with a
    /// reserved keyword (e.g. a parameter named <c>class</c> or <c>default</c>).
    /// </summary>
    private static string EscapeIdentifier(string name)
    {
        var kind = Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name);
        if (kind != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None)
        {
            return "@" + name;
        }
        return name;
    }

    /// <summary>
    /// Projects each method on the service onto its async-sibling shape, and reports any
    /// collisions on the sibling-interface naming. The first return value is the set of
    /// sibling rows; the second is the list of collision diagnostics to surface as
    /// SHARPC004 warnings.
    /// </summary>
    internal static (EquatableArray<AsyncSiblingMethod> Siblings, EquatableArray<MethodDiagnostic> Collisions)
        ComputeAsyncSiblingMethods(ServiceModel service, CancellationToken ct = default)
    {
        var candidates = new List<AsyncSiblingMethod>();
        var collisions = new List<MethodDiagnostic>();

        foreach (var m in service.Methods.Array)
        {
            ct.ThrowIfCancellationRequested();
            // Unsupported (ref/in/out) methods don't get exposed on the sibling — they
            // already have a SHARPC002 diagnostic and a throwing stub on the proxy.
            if (m.UnsupportedReason is not null)
            {
                continue;
            }

            string siblingName = NamingHelpers.IsAsync(m.ReturnKind)
                ? m.Name
                : NamingHelpers.AsyncSiblingMethodName(m.Name);
            var siblingParameters = BuildAsyncSiblingParameters(m);

            // The sibling return kind:
            // - sync void → Task
            // - sync T    → Task<T>
            // - already-async → unchanged (so awaiting is straight-through)
            var siblingReturnKind = m.ReturnKind switch
            {
                MethodReturnKind.Void => MethodReturnKind.Task,
                MethodReturnKind.Sync => MethodReturnKind.TaskOf,
                _ => m.ReturnKind,
            };

            // True iff the proxy would need an extra method to satisfy the sibling
            // interface. A second method is unnecessary when name AND full signature
            // already match (same name on both, already async, already has CT parameter).
            var siblingNameMatches = siblingName == m.Name;
            var signatureMatches = ParametersEqual(m.Parameters, siblingParameters);
            var requiresExtra = !(siblingNameMatches && signatureMatches && NamingHelpers.IsAsync(m.ReturnKind));

            candidates.Add(new AsyncSiblingMethod(siblingName, m, siblingReturnKind, siblingParameters, requiresExtra));
        }

        var groups = new Dictionary<string, List<AsyncSiblingMethod>>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var key = AsyncSiblingSignatureKey(candidate);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new List<AsyncSiblingMethod>();
                groups[key] = group;
            }
            group.Add(candidate);
        }

        var rows = new List<AsyncSiblingMethod>();
        var handledKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var key = AsyncSiblingSignatureKey(candidate);
            if (!handledKeys.Add(key))
            {
                continue;
            }

            var group = groups[key];
            if (group.Count == 1)
            {
                rows.Add(candidate);
                continue;
            }

            var keeper = group.FirstOrDefault(static row => !row.RequiresExtraProxyMethod);
            if (keeper is not null)
            {
                rows.Add(keeper);
            }

            foreach (var row in group)
            {
                if (ReferenceEquals(row, keeper))
                {
                    continue;
                }

                var other = group.First(candidateRow => !ReferenceEquals(candidateRow, row));
                collisions.Add(new MethodDiagnostic(
                    service.InterfaceName,
                    row.Source.Name,
                    $"the async-sibling projection '{row.Name}' would collide with '{other.Source.Name}'. Rename one of the methods or drop the trailing 'Async' on the sync method."));
            }
        }

        return (rows.ToEquatableArray(), collisions.ToEquatableArray());
    }

    private static string AsyncSiblingSignatureKey(AsyncSiblingMethod method) =>
        method.Name + "(" +
        string.Join(",", method.Parameters.Array.Select(p => p.RefKindKeyword + p.Type)) + ")";

    private static EquatableArray<ParameterModel> BuildAsyncSiblingParameters(MethodModel method)
    {
        if (NamingHelpers.IsAsync(method.ReturnKind) && method.HasCancellationToken)
        {
            return method.Parameters;
        }

        var parameters = new List<ParameterModel>();
        foreach (var parameter in method.Parameters.Array)
        {
            if (!parameter.IsCancellationToken)
            {
                parameters.Add(parameter);
            }
        }

        parameters.Add(new ParameterModel(
            UniqueParameterName(method.Parameters, "ct"),
            "global::System.Threading.CancellationToken",
            IsCancellationToken: true,
            HasDefaultValue: true));

        return parameters.ToEquatableArray();
    }

    private static string UniqueParameterName(EquatableArray<ParameterModel> parameters, string baseName)
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in parameters.Array)
        {
            usedNames.Add(parameter.Name);
        }

        var candidate = baseName;
        var suffix = 1;
        while (usedNames.Contains(candidate))
        {
            candidate = baseName + suffix;
            suffix++;
        }

        return candidate;
    }

    private static bool ParametersEqual(
        EquatableArray<ParameterModel> left,
        EquatableArray<ParameterModel> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Escapes a value that will appear inside a regular C# string literal in generated
    /// source. Handles backslash, double-quote, and the common control characters that
    /// would otherwise terminate or corrupt the literal. The values come from
    /// user-supplied attribute arguments (<c>[ShaRpcService(Name = "...")]</c>) and
    /// from generator-internal diagnostic messages, so neither can be trusted to be
    /// free of these characters.
    /// </summary>
    internal static string EscapeStringLiteral(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static string GenerateExtensions(EquatableArray<ServiceModel> services, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        // Pre-compute the set of short-name collisions across all services so the extension
        // method names can be disambiguated by namespace where needed. Without this, two
        // services like A.IFoo and B.IFoo would both produce `CreateFooProxy` and the
        // generated extensions file would not compile (CS0111).
        var shortNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in services)
        {
            ct.ThrowIfCancellationRequested();
            var sn = NamingHelpers.StripInterfacePrefix(s.InterfaceName);
            shortNameCounts.TryGetValue(sn, out var count);
            shortNameCounts[sn] = count + 1;
        }

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace ShaRPC.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Extension methods for registering generated ShaRPC services.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class ShaRpcGeneratedExtensions");
        sb.AppendLine("    {");

        foreach (var service in services)
        {
            ct.ThrowIfCancellationRequested();
            var serviceName = NamingHelpers.StripInterfacePrefix(service.InterfaceName);
            // Disambiguate the extension method suffix when multiple services share the
            // same short name. The proxy/dispatcher CLASS names (defined in their own
            // namespaces) don't collide, but extension-method names do.
            var extensionSuffix = shortNameCounts[serviceName] > 1
                ? (string.IsNullOrEmpty(service.Namespace) ? serviceName : NamespaceIdentifierPrefix(service.Namespace) + "_" + serviceName)
                : serviceName;
            var proxyName = serviceName + "Proxy";
            var dispatcherName = serviceName + "Dispatcher";
            var fullInterfaceName = "global::" + (string.IsNullOrEmpty(service.Namespace)
                ? service.InterfaceName
                : $"{service.Namespace}.{service.InterfaceName}");
            var fullProxyName = "global::" + (string.IsNullOrEmpty(service.Namespace)
                ? proxyName
                : $"{service.Namespace}.{proxyName}");
            var fullDispatcherName = "global::" + (string.IsNullOrEmpty(service.Namespace)
                ? dispatcherName
                : $"{service.Namespace}.{dispatcherName}");

            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine($"        /// Creates a proxy for {service.InterfaceName}.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public static {fullInterfaceName} Create{extensionSuffix}Proxy(this global::ShaRPC.Core.Client.IShaRpcClient client)");
            sb.AppendLine($"            => new {fullProxyName}(client);");

            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine($"        /// Registers {service.InterfaceName} with the server.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public static global::ShaRPC.Core.Server.ShaRpcServerBuilder Add{extensionSuffix}(this global::ShaRPC.Core.Server.ShaRpcServerBuilder builder, {fullInterfaceName} implementation)");
            sb.AppendLine($"            => builder.AddDispatcher(new {fullDispatcherName}(implementation));");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
