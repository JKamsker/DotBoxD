using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void RegisterCollectionExpressionSpreadSyntaxAnalysis(
        CompilationStartAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        context.RegisterSyntaxNodeAction(
            c => AnalyzeSpreadElement(c, helperGraph),
            SyntaxKind.SpreadElement);
    }

    private static void AnalyzeSpreadElement(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        var spreadSyntax = (SpreadElementSyntax)context.Node;
        if (context.SemanticModel.GetOperation(spreadSyntax, context.CancellationToken) is not ISpreadOperation spread ||
            spread.Operand.Type is not { } collectionType ||
            TryResolveGetEnumerator(collectionType, out _))
        {
            return;
        }

        if (!TryResolveExtensionGetEnumerator(
                context.SemanticModel,
                spreadSyntax,
                collectionType,
                out var getEnumerator))
        {
            return;
        }

        var location = spreadSyntax.GetLocation();
        var containingSymbol = context.SemanticModel.GetEnclosingSymbol(
            spreadSyntax.SpanStart,
            context.CancellationToken);
        foreach (var member in CollectionSpreadEnumerationMembers(getEnumerator))
        {
            if (containingSymbol is IMethodSymbol method)
            {
                helperGraph.RecordCall(method, member, location);
            }
            else if (containingSymbol is IFieldSymbol or IPropertySymbol)
            {
                helperGraph.RecordInitializerRootCall(containingSymbol, member, location);
            }
        }
    }

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

        foreach (var member in CollectionSpreadEnumerationMembers(getEnumerator))
        {
            yield return member;
        }
    }

    private static IEnumerable<IMethodSymbol> CollectionSpreadEnumerationMembers(IMethodSymbol getEnumerator)
    {
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

    private static bool TryResolveExtensionGetEnumerator(
        SemanticModel semanticModel,
        SpreadElementSyntax spread,
        ITypeSymbol collectionType,
        out IMethodSymbol getEnumerator)
    {
        getEnumerator = null!;
        if (collectionType is not INamespaceOrTypeSymbol lookupContainer)
        {
            return false;
        }

        foreach (var symbol in semanticModel.LookupSymbols(
                spread.SpanStart,
                lookupContainer,
                name: WellKnownMemberNames.GetEnumeratorMethodName,
                includeReducedExtensionMethods: true)
            .OfType<IMethodSymbol>())
        {
            var reduced = ReducedExtensionMethod(symbol, collectionType);
            if (reduced is { Parameters.Length: 0, ReturnsVoid: false })
            {
                getEnumerator = reduced;
                return true;
            }
        }

        return false;
    }

    private static IMethodSymbol? ReducedExtensionMethod(IMethodSymbol method, ITypeSymbol receiverType)
    {
        if (method.ReducedFrom is not null)
        {
            return method;
        }

        return method.IsExtensionMethod ? method.ReduceExtensionMethod(receiverType) : null;
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
