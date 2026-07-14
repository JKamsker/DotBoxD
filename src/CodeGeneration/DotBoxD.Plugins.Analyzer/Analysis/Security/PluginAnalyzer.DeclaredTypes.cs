using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (field.IsImplicitlyDeclared)
        {
            return;
        }

        ReportForbiddenDeclaredType(context, field.ContainingType, field.Type, field.Locations.FirstOrDefault());
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        ReportForbiddenDeclaredMethodSignature(context, method);
        if (HasAttribute(method, DotBoxDMetadataNames.NativeOnlyAttribute))
        {
            ValidateLocalMember(context, method, method);
        }
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var method = (IMethodSymbol)context.Symbol;
        helperGraph.RecordDispatchImplementations(method);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        ReportForbiddenDeclaredPropertyType(context, property);
        if (HasAttribute(property, DotBoxDMetadataNames.NativeOnlyAttribute))
        {
            ValidateLocalMember(context, property, property);
        }

        if (!HasAttribute(property, DotBoxDMetadataNames.LiveSettingAttribute))
        {
            return;
        }

        if (!IsAllowedLiveSettingType(property.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                LiveSettingTypeRule,
                property.Locations.FirstOrDefault(),
                property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    private static void ReportForbiddenDeclaredPropertyType(SymbolAnalysisContext context, IPropertySymbol property)
        => ReportForbiddenDeclaredType(context, property.ContainingType, property.Type, property.Locations.FirstOrDefault());

    private static void ReportForbiddenDeclaredMethodSignature(SymbolAnalysisContext context, IMethodSymbol method)
    {
        if (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet)
        {
            return;
        }

        if (!IsEventKernel(method.ContainingType))
        {
            return;
        }

        if (ReportForbiddenDeclaredType(context, method.ReturnType, method.Locations.FirstOrDefault()))
        {
            return;
        }

        foreach (var parameter in method.Parameters)
        {
            if (ReportForbiddenDeclaredType(context, parameter.Type, parameter.Locations.FirstOrDefault()))
            {
                return;
            }
        }
    }

    private static void ReportForbiddenReferencedMethodSignature(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method)
    {
        if (!IsReferencedFromEventKernel(context, method.ContainingType))
        {
            return;
        }

        if (context.ContainingSymbol is not IMethodSymbol caller)
        {
            return;
        }

        var location = context.Operation.Syntax.GetLocation();
        if (ReportForbiddenType(context, helperGraph, caller, method.ReturnType, location))
        {
            return;
        }

        foreach (var parameter in method.Parameters)
        {
            if (ReportForbiddenType(context, helperGraph, caller, parameter.Type, location))
            {
                return;
            }
        }
    }

    private static bool ReportForbiddenReferencedType(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        INamedTypeSymbol containingType,
        ITypeSymbol type)
    {
        if (!IsReferencedFromEventKernel(context, containingType))
        {
            return false;
        }

        return context.ContainingSymbol is IMethodSymbol caller &&
               ReportForbiddenType(context, helperGraph, caller, type, context.Operation.Syntax.GetLocation());
    }

    private static bool IsReferencedFromEventKernel(OperationAnalysisContext context, INamedTypeSymbol containingType)
    {
        var eventKernelType = context.ContainingSymbol?.ContainingType;
        return IsEventKernel(eventKernelType) &&
               !SymbolEqualityComparer.Default.Equals(eventKernelType, containingType);
    }

    private static void ReportForbiddenDeclaredType(
        SymbolAnalysisContext context,
        INamedTypeSymbol? containingType,
        ITypeSymbol type,
        Location? location)
    {
        if (IsEventKernel(containingType))
        {
            ReportForbiddenDeclaredType(context, type, location);
        }
    }

    private static bool ReportForbiddenDeclaredType(SymbolAnalysisContext context, ITypeSymbol type, Location? location)
        => ReportForbiddenType(context.ReportDiagnostic, type, location);

    private static bool ReportForbiddenType(OperationAnalysisContext context, ITypeSymbol type, Location? location)
        => ReportForbiddenType(context.ReportDiagnostic, type, location);

    private static bool ReportForbiddenType(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol caller,
        ITypeSymbol type,
        Location? location)
    {
        if (FirstForbiddenHostApi(type) is not { } forbiddenType)
        {
            return false;
        }

        if (helperGraph.TryRecordDirectDiagnostic(caller))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ForbiddenHostApiRule,
                location,
                forbiddenType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }

        return true;
    }

    private static bool ReportForbiddenType(
        Action<Diagnostic> reportDiagnostic,
        ITypeSymbol type,
        Location? location)
    {
        if (FirstForbiddenHostApi(type) is not { } forbiddenType)
        {
            return false;
        }

        reportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            location,
            forbiddenType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        return true;
    }

    private static ITypeSymbol? FirstForbiddenHostApi(ITypeSymbol? type)
    {
        if (type is null)
        {
            return null;
        }

        if (IsForbiddenHostApi(type))
        {
            return type;
        }

        if (type is IArrayTypeSymbol arrayType)
        {
            return FirstForbiddenHostApi(arrayType.ElementType);
        }

        if (type is IPointerTypeSymbol pointerType)
        {
            return FirstForbiddenHostApi(pointerType.PointedAtType);
        }

        if (type is INamedTypeSymbol namedType)
        {
            foreach (var argument in namedType.TypeArguments)
            {
                if (FirstForbiddenHostApi(argument) is { } forbiddenArgument)
                {
                    return forbiddenArgument;
                }
            }
        }

        return null;
    }
}
