using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeSpread(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var spread = (ISpreadOperation)context.Operation;
        var location = context.Operation.Syntax.GetLocation();
        foreach (var member in CollectionSpreadEnumerationMembers(spread.Operand.Type))
        {
            if (context.ContainingSymbol is IMethodSymbol method)
            {
                helperGraph.RecordCall(method, member, location);
            }
            else if (context.ContainingSymbol is IFieldSymbol or IPropertySymbol)
            {
                helperGraph.RecordInitializerRootCall(context.ContainingSymbol, member, location);
            }
        }
    }

    private static IEnumerable<IMethodSymbol> CollectionSpreadEnumerationMembers(ITypeSymbol? collectionType)
    {
        if (!TryResolveGetEnumerator(collectionType, out var getEnumerator))
        {
            yield break;
        }

        yield return getEnumerator;

        var enumeratorType = getEnumerator.ReturnType;
        if (TryResolveMoveNext(enumeratorType, out var moveNext))
        {
            yield return moveNext;
        }

        if (TryResolveCurrentGetter(enumeratorType, out var currentGetter))
        {
            yield return currentGetter;
        }

        if (TryResolveDisposeMethod(enumeratorType, isAsynchronous: false, out var disposeMethod))
        {
            yield return disposeMethod;
        }
    }

    private static bool TryResolveGetEnumerator(
        ITypeSymbol? collectionType,
        out IMethodSymbol getEnumerator)
    {
        getEnumerator = null!;
        if (collectionType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        getEnumerator = SourceInstanceMethods(namedType, WellKnownMemberNames.GetEnumeratorMethodName)
            .FirstOrDefault(static method => method.Parameters.Length == 0 &&
                method.ReturnsVoid == false)!;
        if (getEnumerator is not null)
        {
            return true;
        }

        return TryResolveEnumerableInterfaceGetEnumerator(namedType, out getEnumerator);
    }

    private static bool TryResolveMoveNext(ITypeSymbol? enumeratorType, out IMethodSymbol moveNext)
    {
        moveNext = null!;
        if (enumeratorType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        moveNext = SourceInstanceMethods(namedType, WellKnownMemberNames.MoveNextMethodName)
            .FirstOrDefault(static method => method.Parameters.Length == 0 &&
                method.ReturnType.SpecialType == SpecialType.System_Boolean)!;
        return moveNext is not null;
    }

    private static bool TryResolveCurrentGetter(ITypeSymbol? enumeratorType, out IMethodSymbol getter)
    {
        getter = null!;
        if (enumeratorType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var property = SourceInstanceProperties(namedType, WellKnownMemberNames.CurrentPropertyName)
            .FirstOrDefault(static property => property.GetMethod is not null);
        if (property?.GetMethod is null)
        {
            return false;
        }

        getter = property.GetMethod;
        return true;
    }

    private static bool TryResolveEnumerableInterfaceGetEnumerator(
        INamedTypeSymbol collectionType,
        out IMethodSymbol getEnumerator)
    {
        getEnumerator = null!;
        foreach (var enumerable in collectionType.AllInterfaces)
        {
            if (enumerable.SpecialType is not SpecialType.System_Collections_Generic_IEnumerable_T &&
                !string.Equals(enumerable.ToDisplayString(), "System.Collections.IEnumerable", StringComparison.Ordinal))
            {
                continue;
            }

            var interfaceMethod = enumerable
                .GetMembers(WellKnownMemberNames.GetEnumeratorMethodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(static method => method.Parameters.Length == 0);
            if (interfaceMethod is null)
            {
                continue;
            }

            getEnumerator = collectionType.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol
                ?? interfaceMethod;
            return true;
        }

        return false;
    }

    private static IEnumerable<IMethodSymbol> SourceInstanceMethods(INamedTypeSymbol type, string name)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers(name).OfType<IMethodSymbol>())
            {
                if (!method.IsStatic && method.DeclaringSyntaxReferences.Length != 0)
                {
                    yield return method;
                }
            }
        }
    }

    private static IEnumerable<IPropertySymbol> SourceInstanceProperties(INamedTypeSymbol type, string name)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers(name).OfType<IPropertySymbol>())
            {
                if (!property.IsStatic && property.DeclaringSyntaxReferences.Length != 0)
                {
                    yield return property;
                }
            }
        }
    }
}
