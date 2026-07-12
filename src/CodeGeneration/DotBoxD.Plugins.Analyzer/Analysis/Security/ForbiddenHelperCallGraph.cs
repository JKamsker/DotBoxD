using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal sealed class ForbiddenHelperCallGraph
{
    private readonly ConcurrentDictionary<ISymbol, ITypeSymbol> _forbidden = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<ILocalSymbol, ITypeSymbol> _dynamicLocalTypes =
        new(SymbolEqualityComparer.Default);
    private readonly ConcurrentBag<HelperEdge> _helperEdges = [];
    private readonly ConcurrentBag<RootHelperCall> _rootCalls = [];
    private readonly ConcurrentBag<DynamicHelperCall> _dynamicCalls = [];

    public void RecordForbidden(IMethodSymbol method, ITypeSymbol type)
        => _forbidden.TryAdd(Normalize(method), type);

    public void RecordForbiddenInitializer(ISymbol initializer, ITypeSymbol type)
    {
        if (initializer is not (IFieldSymbol or IPropertySymbol) ||
            initializer.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        _forbidden.TryAdd(Normalize(initializer), type);
    }

    public void RecordForbidden(IFieldSymbol field, ITypeSymbol type)
        => _forbidden.TryAdd(Normalize(field), type);

    public void RecordDynamicLocalType(ILocalSymbol local, ITypeSymbol? type)
    {
        if (type is not null && type.TypeKind != TypeKind.Dynamic)
        {
            _dynamicLocalTypes.TryAdd(local, type);
        }
    }

    public void RecordDynamicInvocation(
        ISymbol? caller,
        ILocalSymbol receiver,
        string memberName,
        int argumentCount,
        Location location)
    {
        if (caller is IMethodSymbol or IFieldSymbol or IPropertySymbol)
        {
            _dynamicCalls.Add(new DynamicHelperCall(caller, receiver, memberName, argumentCount, location));
        }
    }

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
        if (!IsReachableSourceMethod(target) ||
            PluginAnalyzer.IsEventKernel(target.ContainingType))
        {
            return;
        }

        var normalizedTarget = Normalize(target);
        if (PluginAnalyzer.IsEventKernel(caller.ContainingType))
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
            !IsStaticSourceMember(target) ||
            PluginAnalyzer.IsEventKernel(target.ContainingType))
        {
            return;
        }

        _helperEdges.Add(new HelperEdge(Normalize(initializer), Normalize(target)));
    }

    public void RecordRootMemberReference(IMethodSymbol caller, ISymbol target, Location location)
    {
        if (!PluginAnalyzer.IsEventKernel(caller.ContainingType) ||
            !IsStaticSourceMember(target) ||
            PluginAnalyzer.IsEventKernel(target.ContainingType))
        {
            return;
        }

        _rootCalls.Add(new RootHelperCall(Normalize(target), location));
    }

    public void ReportDiagnostics(CompilationAnalysisContext context)
    {
        ResolveDynamicCalls();
        if (_forbidden.IsEmpty ||
            _rootCalls.IsEmpty)
        {
            return;
        }

        var tainted = PropagateForbiddenHelpers();
        foreach (var call in _rootCalls)
        {
            if (tainted.TryGetValue(call.Target, out var type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PluginAnalyzer.ForbiddenHostApiRule,
                    call.Location,
                    type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            }
        }
    }

    private Dictionary<ISymbol, ITypeSymbol> PropagateForbiddenHelpers()
    {
        var tainted = new Dictionary<ISymbol, ITypeSymbol>(_forbidden, SymbolEqualityComparer.Default);
        var callersByTarget = new Dictionary<ISymbol, List<ISymbol>>(SymbolEqualityComparer.Default);
        foreach (var edge in _helperEdges)
        {
            if (!callersByTarget.TryGetValue(edge.Target, out var callers))
            {
                callers = [];
                callersByTarget.Add(edge.Target, callers);
            }

            callers.Add(edge.Caller);
        }

        var pending = new Queue<ISymbol>(tainted.Keys);
        while (pending.Count > 0)
        {
            var target = pending.Dequeue();
            if (!tainted.TryGetValue(target, out var type) ||
                !callersByTarget.TryGetValue(target, out var callers))
            {
                continue;
            }

            foreach (var caller in callers)
            {
                if (tainted.ContainsKey(caller))
                {
                    continue;
                }

                tainted.Add(caller, type);
                pending.Enqueue(caller);
            }
        }

        return tainted;
    }

    private void ResolveDynamicCalls()
    {
        foreach (var call in _dynamicCalls)
        {
            if (!_dynamicLocalTypes.TryGetValue(call.Receiver, out var receiverType))
            {
                continue;
            }

            foreach (var target in PluginAnalyzer.DynamicInvocationCandidates(
                         receiverType,
                         call.MemberName,
                         call.ArgumentCount))
            {
                if (call.Caller is IMethodSymbol method)
                {
                    RecordCall(method, target, call.Location);
                }
                else
                {
                    RecordInitializerRootCall(call.Caller, target, call.Location);
                }
            }
        }
    }

    private static bool IsStaticSourceMember(ISymbol symbol)
        => symbol.DeclaringSyntaxReferences.Length != 0 &&
            symbol is IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true };

    private static bool IsReachableSourceMethod(IMethodSymbol method)
        => method.DeclaringSyntaxReferences.Length != 0 ||
            method is
            {
                MethodKind: MethodKind.Constructor,
                IsStatic: false,
                ContainingType.DeclaringSyntaxReferences.Length: > 0
            };

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

    private readonly record struct DynamicHelperCall(
        ISymbol Caller,
        ILocalSymbol Receiver,
        string MemberName,
        int ArgumentCount,
        Location Location);

    private static ISymbol Normalize(IFieldSymbol field)
        => field.OriginalDefinition;
}
