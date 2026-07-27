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
        if (!ForbiddenGraphAnalysis.IsReachableSourceMethod(method) ||
            !TryGetTypeParameterSlot(method, typeParameter, out var slot))
        {
            return;
        }

        _constructions.Add(new GenericConstruction(Normalize(method), slot));
    }

    public void RecordInvocation(IMethodSymbol caller, IMethodSymbol target, Location location)
    {
        var hasMethodTypeArguments =
            target.IsGenericMethod &&
            target.TypeParameters.Length == target.TypeArguments.Length;
        var hasContainingTypeArguments =
            target.ContainingType.IsGenericType &&
            target.ContainingType.TypeParameters.Length == target.ContainingType.TypeArguments.Length;
        if ((!hasMethodTypeArguments && !hasContainingTypeArguments) ||
            target.OriginalDefinition.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        _invocations.Add(new GenericInvocation(
            Normalize(caller),
            Normalize(target),
            hasMethodTypeArguments ? target.TypeArguments : [],
            hasContainingTypeArguments ? target.ContainingType.TypeArguments : [],
            location));
    }

    public void RecordObjectCreation(
        IMethodSymbol caller,
        IMethodSymbol constructor,
        INamedTypeSymbol constructedType,
        Location location)
    {
        if (constructedType.TypeParameters.Length == 0 ||
            constructedType.TypeParameters.Length != constructedType.TypeArguments.Length ||
            constructedType.OriginalDefinition.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        _invocations.Add(new GenericInvocation(
            Normalize(caller),
            Normalize(constructor),
            [],
            constructedType.TypeArguments,
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

        var invocations = _invocations.ToArray();
        var constructions = BuildConstructionOrdinals(_constructions);
        PropagateConstructionOrdinals(constructions, invocations);

        foreach (var invocation in invocations)
        {
            if (!constructions.TryGetValue(invocation.Target, out var ordinals))
            {
                continue;
            }

            foreach (var ordinal in ordinals)
            {
                if (!TryResolveConstructedConstructor(invocation, ordinal, out var constructor))
                {
                    continue;
                }

                recordConstructorInitializers(constructor);
                recordCall(invocation.Caller, constructor, invocation.Location);
            }
        }
    }

    private static Dictionary<IMethodSymbol, HashSet<GenericParameterSlot>> BuildConstructionOrdinals(
        IEnumerable<GenericConstruction> constructions)
    {
        var ordinalsByMethod = new Dictionary<IMethodSymbol, HashSet<GenericParameterSlot>>(SymbolEqualityComparer.Default);
        foreach (var construction in constructions)
        {
            if (!ordinalsByMethod.TryGetValue(construction.Method, out var ordinals))
            {
                ordinals = [];
                ordinalsByMethod.Add(construction.Method, ordinals);
            }

            ordinals.Add(construction.Ordinal);
        }

        return ordinalsByMethod;
    }

    private static void PropagateConstructionOrdinals(
        Dictionary<IMethodSymbol, HashSet<GenericParameterSlot>> constructions,
        GenericInvocation[] invocations)
    {
        // Construction slots only grow. Visit calls to a method when that method gains a slot
        // instead of repeatedly scanning every invocation until a fixed point is reached.
        var invocationsByTarget = IndexInvocationsByTarget(invocations);
        var pending = CreatePendingConstructions(constructions);

        while (pending.Count > 0)
        {
            var construction = pending.Dequeue();
            if (!invocationsByTarget.TryGetValue(construction.Method, out var invocationIndices))
            {
                continue;
            }

            foreach (var invocationIndex in invocationIndices)
            {
                var invocation = invocations[invocationIndex];
                if (!TryGetTypeArgument(invocation, construction.Ordinal, out var typeArgument) ||
                    typeArgument is not ITypeParameterSymbol typeParameter ||
                    !TryGetTypeParameterSlot(invocation.Caller, typeParameter, out var callerOrdinal))
                {
                    continue;
                }

                if (!constructions.TryGetValue(invocation.Caller, out var callerOrdinals))
                {
                    callerOrdinals = [];
                    constructions.Add(invocation.Caller, callerOrdinals);
                }

                if (callerOrdinals.Add(callerOrdinal))
                {
                    pending.Enqueue(new GenericConstruction(invocation.Caller, callerOrdinal));
                }
            }
        }
    }

    private static Queue<GenericConstruction> CreatePendingConstructions(
        Dictionary<IMethodSymbol, HashSet<GenericParameterSlot>> constructions)
    {
        var pending = new Queue<GenericConstruction>();
        foreach (var pair in constructions)
        {
            foreach (var ordinal in pair.Value)
            {
                pending.Enqueue(new GenericConstruction(pair.Key, ordinal));
            }
        }

        return pending;
    }

    private static Dictionary<IMethodSymbol, List<int>> IndexInvocationsByTarget(
        GenericInvocation[] invocations)
    {
        var indicesByTarget = new Dictionary<IMethodSymbol, List<int>>(SymbolEqualityComparer.Default);
        for (var index = 0; index < invocations.Length; index++)
        {
            var target = invocations[index].Target;
            if (!indicesByTarget.TryGetValue(target, out var indices))
            {
                indices = [];
                indicesByTarget.Add(target, indices);
            }

            indices.Add(index);
        }

        return indicesByTarget;
    }

    private static bool TryResolveConstructedConstructor(
        GenericInvocation invocation,
        GenericParameterSlot ordinal,
        out IMethodSymbol constructor)
    {
        if (TryGetTypeArgument(invocation, ordinal, out var typeArgument) &&
            typeArgument is INamedTypeSymbol namedTypeArgument &&
            ParameterlessInstanceConstructor(namedTypeArgument) is { } parameterlessConstructor)
        {
            constructor = parameterlessConstructor;
            return true;
        }

        constructor = null!;
        return false;
    }

    private static bool TryGetTypeParameterOrdinal(
        ImmutableArray<ITypeParameterSymbol> typeParameters,
        ITypeParameterSymbol typeParameter,
        out int ordinal)
    {
        for (var i = 0; i < typeParameters.Length; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(typeParameters[i], typeParameter))
            {
                ordinal = i;
                return true;
            }
        }

        ordinal = -1;
        return false;
    }

    private static bool TryGetTypeParameterSlot(
        IMethodSymbol method,
        ITypeParameterSymbol typeParameter,
        out GenericParameterSlot slot)
    {
        if (TryGetTypeParameterOrdinal(method.TypeParameters, typeParameter, out var methodOrdinal))
        {
            slot = new GenericParameterSlot(GenericParameterOwner.Method, methodOrdinal);
            return true;
        }

        if (TryGetTypeParameterOrdinal(method.ContainingType.TypeParameters, typeParameter, out var typeOrdinal))
        {
            slot = new GenericParameterSlot(GenericParameterOwner.ContainingType, typeOrdinal);
            return true;
        }

        slot = default;
        return false;
    }

    private static bool TryGetTypeArgument(
        GenericInvocation invocation,
        GenericParameterSlot slot,
        out ITypeSymbol typeArgument)
    {
        var arguments = slot.Owner == GenericParameterOwner.Method
            ? invocation.MethodTypeArguments
            : invocation.ContainingTypeArguments;
        if (slot.Ordinal >= 0 && slot.Ordinal < arguments.Length)
        {
            typeArgument = arguments[slot.Ordinal];
            return true;
        }

        typeArgument = null!;
        return false;
    }

    private static IMethodSymbol? ParameterlessInstanceConstructor(INamedTypeSymbol type)
        => type.InstanceConstructors.FirstOrDefault(static constructor =>
            constructor.Parameters.Length == 0);

    private static IMethodSymbol Normalize(IMethodSymbol method)
        => method.OriginalDefinition;

    private readonly record struct GenericConstruction(IMethodSymbol Method, GenericParameterSlot Ordinal);

    private readonly record struct GenericInvocation(
        IMethodSymbol Caller,
        IMethodSymbol Target,
        ImmutableArray<ITypeSymbol> MethodTypeArguments,
        ImmutableArray<ITypeSymbol> ContainingTypeArguments,
        Location Location);

    private readonly record struct GenericParameterSlot(GenericParameterOwner Owner, int Ordinal);

    private enum GenericParameterOwner
    {
        Method,
        ContainingType
    }
}
