using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal sealed class GenericConstructionReachability
{
    private readonly ConcurrentBag<GenericConstruction> _constructions = [];
    private readonly ConcurrentBag<GenericInvocation> _invocations = [];

    public void RecordTypeParameterConstruction(IMethodSymbol method, ITypeParameterSymbol typeParameter)
    {
        if (method.DeclaringSyntaxReferences.Length == 0 ||
            !typeParameter.HasConstructorConstraint ||
            !TryGetTypeParameterOrdinal(method, typeParameter, out var ordinal))
        {
            return;
        }

        _constructions.Add(new GenericConstruction(Normalize(method), ordinal));
    }

    public void RecordInvocation(IMethodSymbol caller, IMethodSymbol target, Location location)
    {
        if (!target.IsGenericMethod ||
            target.TypeParameters.Length != target.TypeArguments.Length ||
            target.OriginalDefinition.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        _invocations.Add(new GenericInvocation(
            Normalize(caller),
            Normalize(target),
            target.TypeArguments,
            location));
    }

    public void Resolve(
        Action<IMethodSymbol> recordConstructorInitializers,
        Action<IMethodSymbol, IMethodSymbol, Location> recordCall)
    {
        if (_constructions.IsEmpty || _invocations.IsEmpty)
        {
            return;
        }

        var constructions = new Dictionary<IMethodSymbol, HashSet<int>>(SymbolEqualityComparer.Default);
        foreach (var construction in _constructions)
        {
            if (!constructions.TryGetValue(construction.Method, out var ordinals))
            {
                ordinals = [];
                constructions.Add(construction.Method, ordinals);
            }

            ordinals.Add(construction.Ordinal);
        }

        foreach (var invocation in _invocations)
        {
            if (!constructions.TryGetValue(invocation.Target, out var ordinals))
            {
                continue;
            }

            foreach (var ordinal in ordinals)
            {
                if (ordinal >= invocation.TypeArguments.Length ||
                    invocation.TypeArguments[ordinal] is not INamedTypeSymbol typeArgument ||
                    ParameterlessInstanceConstructor(typeArgument) is not { } constructor)
                {
                    continue;
                }

                recordConstructorInitializers(constructor);
                recordCall(invocation.Caller, constructor, invocation.Location);
            }
        }
    }

    private static bool TryGetTypeParameterOrdinal(
        IMethodSymbol method,
        ITypeParameterSymbol typeParameter,
        out int ordinal)
    {
        for (var i = 0; i < method.TypeParameters.Length; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(method.TypeParameters[i], typeParameter))
            {
                ordinal = i;
                return true;
            }
        }

        ordinal = -1;
        return false;
    }

    private static IMethodSymbol? ParameterlessInstanceConstructor(INamedTypeSymbol type)
        => type.InstanceConstructors.FirstOrDefault(static constructor =>
            constructor.Parameters.Length == 0 &&
            !constructor.IsStatic);

    private static IMethodSymbol Normalize(IMethodSymbol method)
        => method.OriginalDefinition;

    private readonly record struct GenericConstruction(IMethodSymbol Method, int Ordinal);

    private readonly record struct GenericInvocation(
        IMethodSymbol Caller,
        IMethodSymbol Target,
        ImmutableArray<ITypeSymbol> TypeArguments,
        Location Location);
}
