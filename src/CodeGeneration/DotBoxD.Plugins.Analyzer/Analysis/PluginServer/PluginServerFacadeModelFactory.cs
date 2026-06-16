using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerFacadeModelFactory
{
    private const string ServiceControlType = "DotBoxD.Abstractions.IServiceControl";
    private const string ExtensibleControlType = "DotBoxD.Abstractions.IExtensibleControl";

    public static PluginServerFacadeResult? Create(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol type ||
            context.TargetNode is not ClassDeclarationSyntax declaration)
        {
            return null;
        }

        try
        {
            var model = CreateModel(type, context.SemanticModel.Compilation, cancellationToken);
            return new PluginServerFacadeResult(PluginServerFacadeEmitter.Emit(model), null);
        }
        catch (NotSupportedException ex)
        {
            return new PluginServerFacadeResult(null, PluginKernelDiagnostic.Create(declaration.Identifier, ex.Message));
        }
    }

    private static PluginServerFacadeModel CreateModel(
        INamedTypeSymbol type,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var worldType = ResolveWorldType(type)
            ?? throw new NotSupportedException(
                $"Generated plugin server '{type.Name}' must directly implement one [DotBoxDService] world interface.");
        var controlServiceType = ResolveControlService(compilation, worldType)
            ?? throw new NotSupportedException(
                $"Generated plugin server '{type.Name}' could not resolve the IGamePluginControlService control-plane contract.");
        var controls = ResolveControls(worldType, cancellationToken);
        var ns = type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString();
        var controlNs = controlServiceType.ContainingNamespace.ToDisplayString();
        return new PluginServerFacadeModel(
            ns,
            AccessibilityText(type.DeclaredAccessibility),
            type.Name,
            TypeName(worldType),
            TypeName(controlServiceType),
            "global::" + controlNs + ".LiveSettingUpdate",
            new EquatableArray<PluginServerForwardedMethod>(ResolveMethods(worldType, cancellationToken)),
            new EquatableArray<PluginServerControlProperty>(controls));
    }

    private static INamedTypeSymbol? ResolveWorldType(INamedTypeSymbol type)
    {
        foreach (var candidate in type.Interfaces)
        {
            if (HasAttribute(candidate, DotBoxDGenerationNames.Metadata.DotBoxDServiceAttribute))
            {
                return candidate;
            }
        }

        return null;
    }

    private static INamedTypeSymbol? ResolveControlService(
        Compilation compilation,
        INamedTypeSymbol worldType)
    {
        var worldNamespace = worldType.ContainingNamespace.ToDisplayString();
        return compilation.GetTypeByMetadataName(worldNamespace + ".Ipc.IGamePluginControlService");
    }

    private static PluginServerControlProperty[] ResolveControls(
        INamedTypeSymbol worldType,
        CancellationToken cancellationToken)
    {
        var controls = new List<PluginServerControlProperty>();
        foreach (var member in worldType.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IPropertySymbol
                {
                    IsStatic: false,
                    GetMethod: not null,
                    SetMethod: null,
                    Type: INamedTypeSymbol propertyType
                } property ||
                !HasAttribute(propertyType, DotBoxDGenerationNames.Metadata.DotBoxDServiceAttribute))
            {
                continue;
            }

            controls.Add(new PluginServerControlProperty(
                property.Name,
                TypeName(propertyType),
                property.Name + "PluginControl",
                new EquatableArray<PluginServerForwardedMethod>(ResolveMethods(propertyType, cancellationToken))));
        }

        return controls.ToArray();
    }

    private static PluginServerForwardedMethod[] ResolveMethods(
        INamedTypeSymbol controlType,
        CancellationToken cancellationToken)
    {
        var methods = new List<PluginServerForwardedMethod>();
        foreach (var member in controlType.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary,
                    IsStatic: false,
                    IsGenericMethod: false
                } method &&
                !IsControlPlaneMember(method.ContainingType))
            {
                methods.Add(new PluginServerForwardedMethod(
                    method.Name,
                    TypeName(method.ReturnType),
                    new EquatableArray<PluginServerParameter>(ResolveParameters(method))));
            }
        }

        return methods.ToArray();
    }

    private static PluginServerParameter[] ResolveParameters(IMethodSymbol method)
    {
        var parameters = new PluginServerParameter[method.Parameters.Length];
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            parameters[i] = new PluginServerParameter(parameter.Name, TypeName(parameter.Type));
        }

        return parameters;
    }

    private static bool HasAttribute(INamedTypeSymbol type, string metadataName)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsControlPlaneMember(INamedTypeSymbol type)
    {
        var name = type.ToDisplayString();
        return string.Equals(name, ServiceControlType, StringComparison.Ordinal) ||
               string.Equals(name, ExtensibleControlType, StringComparison.Ordinal);
    }

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string AccessibilityText(Accessibility accessibility)
        => accessibility switch
        {
            Microsoft.CodeAnalysis.Accessibility.Public => "public",
            Microsoft.CodeAnalysis.Accessibility.Internal => "internal",
            _ => "internal"
        };
}
