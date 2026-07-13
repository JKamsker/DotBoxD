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
        AnalyzeMethodAttributeTypes(helperGraph, method);
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
        IMethodSymbol method)
    {
        if (!IsReferencedFromEventKernel(context, method.ContainingType))
        {
            return;
        }

        var location = context.Operation.Syntax.GetLocation();
        if (ReportForbiddenType(context, method.ReturnType, location))
        {
            return;
        }

        foreach (var parameter in method.Parameters)
        {
            if (ReportForbiddenType(context, parameter.Type, location))
            {
                return;
            }
        }
    }

    private static bool ReportForbiddenReferencedType(
        OperationAnalysisContext context,
        INamedTypeSymbol containingType,
        ITypeSymbol type)
    {
        if (!IsReferencedFromEventKernel(context, containingType))
        {
            return false;
        }

        return ReportForbiddenType(context, type, context.Operation.Syntax.GetLocation());
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

    private static void AnalyzeMethodAttributeTypes(
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method)
    {
        if (method.IsImplicitlyDeclared)
        {
            return;
        }

        foreach (var attribute in MethodAndParameterAttributes(method))
        {
            if (FirstForbiddenAttributeType(attribute) is not { } forbiddenType)
            {
                continue;
            }

            helperGraph.RecordForbidden(method, forbiddenType);
        }
    }

    private static IEnumerable<AttributeData> MethodAndParameterAttributes(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            yield return attribute;
        }

        foreach (var attribute in method.GetReturnTypeAttributes())
        {
            yield return attribute;
        }

        foreach (var parameter in method.Parameters)
        {
            foreach (var attribute in parameter.GetAttributes())
            {
                yield return attribute;
            }
        }
    }

    private static ITypeSymbol? FirstForbiddenAttributeType(AttributeData attribute)
    {
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (FirstForbiddenAttributeType(argument) is { } forbiddenType)
            {
                return forbiddenType;
            }
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (FirstForbiddenAttributeType(argument.Value) is { } forbiddenType)
            {
                return forbiddenType;
            }
        }

        return null;
    }

    private static ITypeSymbol? FirstForbiddenAttributeType(TypedConstant constant)
    {
        if (constant.Kind == TypedConstantKind.Type && constant.Value is ITypeSymbol type)
        {
            return FirstForbiddenHostApi(type);
        }

        if (constant.Kind == TypedConstantKind.Array)
        {
            foreach (var value in constant.Values)
            {
                if (FirstForbiddenAttributeType(value) is { } forbiddenType)
                {
                    return forbiddenType;
                }
            }
        }

        return null;
    }

    private static Location? AttributeLocation(AttributeData attribute)
        => attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
}
