using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeVariableDeclaration(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            return;
        }

        var declaration = (IVariableDeclarationOperation)context.Operation;
        foreach (var declarator in declaration.Declarators)
        {
            RecordDynamicLocalInitializer(helperGraph, declarator);
            if (!TryGetForbiddenHostApi(declarator.Symbol.Type, out var forbidden))
            {
                continue;
            }

            helperGraph.RecordForbidden(method, forbidden);
            if (!IsEventKernel(method.ContainingType) ||
                !helperGraph.TryRecordDirectDiagnostic(method))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                ForbiddenHostApiRule,
                declarator.Syntax.GetLocation(),
                forbidden.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }
    }

    private static void RecordDynamicLocalInitializer(
        ForbiddenHelperCallGraph helperGraph,
        IVariableDeclaratorOperation declarator)
    {
        if (declarator.Symbol.Type.TypeKind != TypeKind.Dynamic)
        {
            return;
        }

        helperGraph.RecordDynamicLocalType(
            declarator.Symbol,
            DynamicInitializerType(declarator.Initializer?.Value));
    }

    private static ITypeSymbol? DynamicInitializerType(IOperation? initializer)
        => initializer switch
        {
            IConversionOperation conversion => DynamicInitializerType(conversion.Operand),
            { Type.TypeKind: not TypeKind.Dynamic } operation => operation.Type,
            _ => null
        };

    private static bool TryGetForbiddenHostApi(
        ITypeSymbol? type,
        out ITypeSymbol forbidden)
    {
        if (TryGetDirectForbiddenHostApi(type, out forbidden))
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return TryGetForbiddenHostApi(array.ElementType, out forbidden);
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var argument in named.TypeArguments)
            {
                if (TryGetForbiddenHostApi(argument, out forbidden))
                {
                    return true;
                }
            }
        }

        forbidden = null!;
        return false;
    }

    private static bool TryGetForbiddenHostApi(
        ISymbol? symbol,
        out ITypeSymbol forbidden)
    {
        switch (symbol)
        {
            case IMethodSymbol
            {
                Name: "RegisterProvider",
                ContainingType: { } containingType
            } when containingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                    == "System.Text.Encoding":
                forbidden = containingType;
                return true;
            case IMethodSymbol method when TryGetForbiddenX509HostIoMethod(method, out forbidden):
                return true;
            case IMethodSymbol method when TryGetForbiddenNondeterministicMethod(method, out forbidden):
                return true;
            case IMethodSymbol method:
                return TryGetForbiddenHostApi(method.ContainingType, out forbidden);
            case IPropertySymbol property when TryGetForbiddenNondeterministicProperty(property, out forbidden):
                return true;
            case IPropertySymbol property:
                return TryGetForbiddenHostApi(property.ContainingType, out forbidden);
            case ITypeSymbol type:
                return TryGetForbiddenHostApi(type, out forbidden);
            default:
                forbidden = null!;
                return false;
        }
    }

    private static bool TryGetForbiddenX509HostIoMethod(
        IMethodSymbol method,
        out ITypeSymbol forbidden)
    {
        var containingType = method.ContainingType;
        var containingTypeName = containingType?.OriginalDefinition.ToDisplayString(
            SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (IsPathBasedX509CertificateConstructor(method, containingTypeName) ||
            IsX509ChainBuild(method, containingTypeName))
        {
            forbidden = containingType!;
            return true;
        }

        forbidden = null!;
        return false;
    }

    private static bool IsPathBasedX509CertificateConstructor(
        IMethodSymbol method,
        string? containingTypeName)
        => method.MethodKind == MethodKind.Constructor &&
           containingTypeName is
               "System.Security.Cryptography.X509Certificates.X509Certificate" or
               "System.Security.Cryptography.X509Certificates.X509Certificate2" &&
           method.Parameters.Any(static parameter =>
               parameter.Type.SpecialType == SpecialType.System_String);

    private static bool IsX509ChainBuild(
        IMethodSymbol method,
        string? containingTypeName)
        => containingTypeName == "System.Security.Cryptography.X509Certificates.X509Chain" &&
           string.Equals(method.Name, "Build", StringComparison.Ordinal);

    private static bool TryGetForbiddenNondeterministicMethod(
        IMethodSymbol method,
        out ITypeSymbol forbidden)
    {
        var containingType = method.ContainingType;
        var containingTypeName = containingType?.OriginalDefinition.ToDisplayString(
            SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (containingTypeName == "System.Random" &&
            method.MethodKind == MethodKind.Constructor)
        {
            forbidden = containingType!;
            return true;
        }

        if (containingTypeName == "System.Guid" &&
            string.Equals(method.Name, "NewGuid", StringComparison.Ordinal))
        {
            forbidden = containingType!;
            return true;
        }

        forbidden = null!;
        return false;
    }

    private static bool TryGetForbiddenNondeterministicProperty(
        IPropertySymbol property,
        out ITypeSymbol forbidden)
    {
        var containingType = property.ContainingType;
        var containingTypeName = containingType?.OriginalDefinition.ToDisplayString(
            SymbolDisplayFormat.CSharpErrorMessageFormat);
        if ((containingTypeName == "System.DateTime" && property.Name is "Now" or "UtcNow" or "Today") ||
            (containingTypeName == "System.DateTimeOffset" && property.Name is "Now" or "UtcNow") ||
            (containingTypeName == "System.Random" && property.Name == "Shared"))
        {
            forbidden = containingType!;
            return true;
        }

        forbidden = null!;
        return false;
    }

    private static bool TryGetDirectForbiddenHostApi(
        ITypeSymbol? type,
        out ITypeSymbol forbidden)
    {
        var name = type?.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (!string.IsNullOrWhiteSpace(name) &&
            (IsForbiddenExactType(name!) || IsForbiddenNamespace(name!)))
        {
            forbidden = type!;
            return true;
        }

        forbidden = null!;
        return false;
    }

    private static bool IsForbiddenInvocationReceiver(IOperation operation, ITypeSymbol? containingType)
        => IsForbiddenHostApi(containingType) &&
           operation.Parent is IInvocationOperation { TargetMethod.ContainingType: { } targetType } &&
           SymbolEqualityComparer.Default.Equals(
               containingType!.OriginalDefinition,
               targetType.OriginalDefinition);
}
