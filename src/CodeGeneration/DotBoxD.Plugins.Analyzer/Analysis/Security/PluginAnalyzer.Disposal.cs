using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private const string DisposableInterfaceName = "System.IDisposable";
    private const string AsyncDisposableInterfaceName = "System.IAsyncDisposable";

    private static void AnalyzeUsing(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var operation = (IUsingOperation)context.Operation;
        RecordDisposalReachability(context, helperGraph, operation.Resources, operation.IsAsynchronous);
    }

    private static void AnalyzeUsingDeclaration(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        var operation = (IUsingDeclarationOperation)context.Operation;
        RecordDisposalReachability(context, helperGraph, operation.DeclarationGroup, operation.IsAsynchronous);
    }

    private static void RecordDisposalReachability(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IOperation? resources,
        bool isAsynchronous)
    {
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            return;
        }

        var location = context.Operation.Syntax.GetLocation();
        foreach (var resourceType in ResourceTypes(resources))
        {
            if (TryResolveDisposeMethod(resourceType, isAsynchronous, out var disposeMethod))
            {
                helperGraph.RecordCall(method, disposeMethod, location);
            }
        }
    }

    private static IEnumerable<ITypeSymbol> ResourceTypes(IOperation? operation)
    {
        switch (operation)
        {
            case null:
                yield break;

            case IVariableDeclarationGroupOperation group:
                foreach (var declaration in group.Declarations)
                {
                    foreach (var resourceType in ResourceTypes(declaration))
                    {
                        yield return resourceType;
                    }
                }

                yield break;

            case IVariableDeclarationOperation declaration:
                foreach (var declarator in declaration.Declarators)
                {
                    yield return declarator.Symbol.Type;
                }

                yield break;

            case IConversionOperation conversion:
                foreach (var resourceType in ResourceTypes(conversion.Operand))
                {
                    yield return resourceType;
                }

                yield break;

            default:
                if (operation.Type is { } type)
                {
                    yield return type;
                }

                yield break;
        }
    }

    private static bool TryResolveDisposeMethod(
        ITypeSymbol resourceType,
        bool isAsynchronous,
        out IMethodSymbol disposeMethod)
    {
        disposeMethod = null!;
        if (resourceType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var methodName = isAsynchronous ? "DisposeAsync" : "Dispose";
        var interfaceName = isAsynchronous ? AsyncDisposableInterfaceName : DisposableInterfaceName;
        return TryResolveInterfaceDispose(namedType, interfaceName, methodName, out disposeMethod) ||
            TryResolvePatternDispose(namedType, methodName, out disposeMethod);
    }

    private static bool TryResolveInterfaceDispose(
        INamedTypeSymbol resourceType,
        string interfaceName,
        string methodName,
        out IMethodSymbol disposeMethod)
    {
        disposeMethod = null!;
        foreach (var candidateInterface in resourceType.AllInterfaces)
        {
            if (!string.Equals(candidateInterface.ToDisplayString(), interfaceName, StringComparison.Ordinal))
            {
                continue;
            }

            var interfaceMethod = candidateInterface
                .GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(static method => method.Parameters.Length == 0);
            if (interfaceMethod is null)
            {
                continue;
            }

            disposeMethod = resourceType.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol
                ?? interfaceMethod;
            return true;
        }

        return false;
    }

    private static bool TryResolvePatternDispose(
        INamedTypeSymbol resourceType,
        string methodName,
        out IMethodSymbol disposeMethod)
    {
        for (INamedTypeSymbol? current = resourceType; current is not null; current = current.BaseType)
        {
            disposeMethod = current
                .GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(static method => !method.IsStatic && method.Parameters.Length == 0)!;
            if (disposeMethod is not null)
            {
                return true;
            }
        }

        disposeMethod = null!;
        return false;
    }
}
