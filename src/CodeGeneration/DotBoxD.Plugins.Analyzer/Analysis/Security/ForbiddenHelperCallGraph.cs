using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal sealed class ForbiddenHelperCallGraph
{
    private readonly ConcurrentDictionary<ISymbol, string> _forbidden = new(SymbolEqualityComparer.Default);
    private readonly ForbiddenDirectDiagnosticSet _directDiagnostics = new();
    private readonly DynamicHelperCallResolver _dynamicHelperCalls = new();
    private readonly GenericConstructionReachability _genericConstructionReachability = new();
    private readonly ConcurrentBag<HelperEdge> _helperEdges = [];
    private readonly ConcurrentBag<RootHelperCall> _rootCalls = [];

    public void RecordForbidden(IMethodSymbol method, ITypeSymbol type)
        => RecordForbidden(method, DisplayName(type));

    public void RecordForbidden(IMethodSymbol method, string displayName)
        => _forbidden.TryAdd(Normalize(method), displayName);

    public void RecordForbiddenInitializer(ISymbol initializer, ITypeSymbol type)
        => RecordForbiddenInitializer(initializer, DisplayName(type));

    public void RecordForbiddenInitializer(ISymbol initializer, string displayName)
    {
        if (initializer is not (IFieldSymbol or IPropertySymbol) ||
            initializer.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        _forbidden.TryAdd(Normalize(initializer), displayName);
    }

    public void RecordForbidden(IFieldSymbol field, ITypeSymbol type)
        => _forbidden.TryAdd(Normalize(field), DisplayName(type));

    public bool TryRecordDirectDiagnostic(IMethodSymbol method, ITypeSymbol type)
        => _directDiagnostics.TryAdd(Normalize(method), DirectDiagnosticKey(type));

    public bool TryRecordDirectDiagnostic(IMethodSymbol method, string displayName)
        => _directDiagnostics.TryAdd(Normalize(method), displayName);

    public void RecordDynamicLocalType(ILocalSymbol local, ITypeSymbol? type)
        => _dynamicHelperCalls.RecordLocalType(local, type);

    public void RecordDynamicInvocation(
        ISymbol? caller,
        ILocalSymbol receiver,
        string memberName,
        int argumentCount,
        Location location)
        => _dynamicHelperCalls.RecordInvocation(caller, receiver, memberName, argumentCount, location);

    public void RecordDynamicPropertyReference(
        ISymbol? caller,
        ILocalSymbol receiver,
        string memberName,
        bool usesGetter,
        bool usesSetter,
        Location location)
        => _dynamicHelperCalls.RecordPropertyReference(
            caller,
            receiver,
            memberName,
            usesGetter,
            usesSetter,
            location);

    public void RecordDynamicIndexerAccess(
        ISymbol? caller,
        ILocalSymbol receiver,
        int argumentCount,
        bool usesGetter,
        bool usesSetter,
        Location location)
        => _dynamicHelperCalls.RecordIndexerAccess(
            caller,
            receiver,
            argumentCount,
            usesGetter,
            usesSetter,
            location);

    public void RecordDispatchImplementations(IMethodSymbol method)
    {
        if (method.DeclaringSyntaxReferences.Length == 0 ||
            PluginAnalyzer.IsEventKernel(method.ContainingType))
        {
            return;
        }

        if (method.OverriddenMethod is { } overridden)
        {
            _helperEdges.Add(new HelperEdge(Normalize(overridden), Normalize(method)));
        }

        foreach (var @interface in method.ContainingType.AllInterfaces)
        {
            foreach (var member in @interface.GetMembers(method.Name).OfType<IMethodSymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(
                    method.ContainingType.FindImplementationForInterfaceMember(member),
                    method))
                {
                    _helperEdges.Add(new HelperEdge(Normalize(member), Normalize(method)));
                }
            }
        }
    }

    public void RecordCall(IMethodSymbol caller, IMethodSymbol target, Location location)
    {
        if (!ForbiddenGraphAnalysis.IsReachableSourceMethod(target) ||
            PluginAnalyzer.IsEventKernel(target.ContainingType))
        {
            return;
        }

        var normalizedTarget = Normalize(target);
        if (PluginAnalyzer.IsEventKernel(caller.ContainingType) || PluginAnalyzer.IsModuleInitializer(caller))
        {
            _rootCalls.Add(new RootHelperCall(normalizedTarget, location));
            return;
        }

        if (caller.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        _helperEdges.Add(new HelperEdge(Normalize(caller), normalizedTarget));
    }

    public void RecordGenericTypeParameterConstruction(IMethodSymbol method, ITypeParameterSymbol typeParameter)
        => _genericConstructionReachability.RecordTypeParameterConstruction(method, typeParameter);

    public void RecordGenericInvocation(IMethodSymbol caller, IMethodSymbol target, Location location)
        => _genericConstructionReachability.RecordInvocation(caller, target, location);

    public void RecordGenericObjectCreation(
        IMethodSymbol caller,
        IMethodSymbol constructor,
        INamedTypeSymbol constructedType,
        Location location)
        => _genericConstructionReachability.RecordObjectCreation(caller, constructor, constructedType, location);

    public void RecordConstructorInitializers(IMethodSymbol constructor)
    {
        if (constructor.MethodKind != MethodKind.Constructor ||
            constructor.IsStatic ||
            constructor.ContainingType.DeclaringSyntaxReferences.Length == 0 ||
            PluginAnalyzer.IsEventKernel(constructor.ContainingType))
        {
            return;
        }

        var normalizedConstructor = Normalize(constructor);
        foreach (var member in constructor.ContainingType.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false, DeclaringSyntaxReferences.Length: > 0 } field:
                    _helperEdges.Add(new HelperEdge(normalizedConstructor, Normalize(field)));
                    break;
                case IPropertySymbol { IsStatic: false, IsImplicitlyDeclared: false, DeclaringSyntaxReferences.Length: > 0 } property:
                    _helperEdges.Add(new HelperEdge(normalizedConstructor, Normalize(property)));
                    break;
            }
        }
    }

    public void RecordDelegateFieldTarget(IFieldSymbol field, IMethodSymbol target)
    {
        if (field.DeclaringSyntaxReferences.Length == 0 ||
            target.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        _helperEdges.Add(new HelperEdge(Normalize(field), target.OriginalDefinition));
    }

    public void RecordDelegateFieldReference(IMethodSymbol caller, IFieldSymbol field, Location location)
    {
        if (field.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        var normalizedField = Normalize(field);
        if (PluginAnalyzer.IsEventKernel(caller.ContainingType))
        {
            _rootCalls.Add(new RootHelperCall(normalizedField, location));
            return;
        }

        if (caller.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        _helperEdges.Add(new HelperEdge(caller.OriginalDefinition, normalizedField));
    }

    // A source field/property initializer can either be a kernel root or an intermediate helper node. Its
    // ContainingSymbol is the field/property (not a method), so RecordCall never sees it.
    public void RecordInitializerRootCall(ISymbol initializer, IMethodSymbol target, Location location)
    {
        if (target.DeclaringSyntaxReferences.Length == 0 ||
            initializer is not (IFieldSymbol or IPropertySymbol) ||
            PluginAnalyzer.IsEventKernel(target.ContainingType))
        {
            return;
        }

        var normalizedTarget = Normalize(target);
        if (PluginAnalyzer.IsEventKernel(initializer.ContainingType))
        {
            _rootCalls.Add(new RootHelperCall(normalizedTarget, location));
            return;
        }

        if (initializer.DeclaringSyntaxReferences.Length != 0)
        {
            _helperEdges.Add(new HelperEdge(Normalize(initializer), normalizedTarget));
        }
    }

    public void RecordInitializerMemberReference(ISymbol initializer, ISymbol target)
    {
        if (initializer is not (IFieldSymbol or IPropertySymbol) ||
            initializer.DeclaringSyntaxReferences.Length == 0 ||
            !ForbiddenGraphAnalysis.IsStaticSourceMember(target) ||
            PluginAnalyzer.IsEventKernel(target.ContainingType))
        {
            return;
        }

        _helperEdges.Add(new HelperEdge(Normalize(initializer), Normalize(target)));
    }

    public void RecordRootMemberReference(IMethodSymbol caller, ISymbol target, Location location)
    {
        if (!PluginAnalyzer.IsEventKernel(caller.ContainingType) ||
            !ForbiddenGraphAnalysis.IsStaticSourceMember(target) ||
            PluginAnalyzer.IsEventKernel(target.ContainingType))
        {
            return;
        }

        _rootCalls.Add(new RootHelperCall(Normalize(target), location));
    }

    public void ReportDiagnostics(CompilationAnalysisContext context)
    {
        _genericConstructionReachability.Resolve(RecordConstructorInitializers, RecordCall);
        _dynamicHelperCalls.Resolve(RecordCall, RecordInitializerRootCall, RecordDynamicTargetSignature);
        if (_forbidden.IsEmpty ||
            _rootCalls.IsEmpty ||
            !PluginAnalyzer.CompilationContainsEventKernel(context.Compilation))
        {
            return;
        }

        var tainted = ForbiddenGraphAnalysis.Propagate(
            _forbidden,
            _helperEdges.Select(static edge => (edge.Caller, edge.Target)));
        foreach (var call in _rootCalls)
        {
            if (tainted.TryGetValue(call.Target, out var displayName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PluginAnalyzer.ForbiddenHostApiRule,
                    call.Location,
                    displayName));
            }
        }
    }

    private void RecordDynamicTargetSignature(ISymbol caller, IMethodSymbol target, Location location)
    {
        if (ForbiddenGraphAnalysis.FirstForbiddenSignatureType(target) is not { } forbidden)
        {
            return;
        }

        var normalizedCaller = Normalize(caller);
        _forbidden.TryAdd(normalizedCaller, DisplayName(forbidden));
        if (caller is IMethodSymbol method &&
            (PluginAnalyzer.IsEventKernel(method.ContainingType) || PluginAnalyzer.IsModuleInitializer(method)) ||
            caller is IFieldSymbol or IPropertySymbol && PluginAnalyzer.IsEventKernel(caller.ContainingType))
        {
            _rootCalls.Add(new RootHelperCall(normalizedCaller, location));
        }
    }

    private static string DisplayName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private static string DirectDiagnosticKey(ITypeSymbol type)
        => (type is INamedTypeSymbol named ? named.OriginalDefinition : type)
            .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private static ISymbol Normalize(ISymbol symbol)
        => symbol switch
        {
            IMethodSymbol method => method.OriginalDefinition,
            IPropertySymbol property => property.OriginalDefinition,
            IFieldSymbol field => field.OriginalDefinition,
            _ => symbol
        };

    private readonly record struct HelperEdge(ISymbol Caller, ISymbol Target);

    private readonly record struct RootHelperCall(ISymbol Target, Location Location);
}
