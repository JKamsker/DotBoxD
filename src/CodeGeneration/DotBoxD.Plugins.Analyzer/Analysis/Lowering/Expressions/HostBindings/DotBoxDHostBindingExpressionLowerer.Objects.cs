using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDHostBindingExpressionLowerer
{
    private static bool HasHostBindingIgnore(IMethodSymbol method, Compilation compilation)
        => method.GetAttributes().Any(attribute => IsDotBoxDAttribute(
            attribute,
            compilation,
            DotBoxDMetadataNames.HostBindingIgnoreAttribute));

    private static (string BindingId, string? Capability, IReadOnlyList<string> Effects, bool IsAsync)?
        TryHostBindingObject(IMethodSymbol method, Compilation compilation)
    {
        if (!IsEligibleHostBindingObjectMethod(method) ||
            HostBindingObject(method.ContainingType, compilation) is not { } defaults)
        {
            return null;
        }

        var capability = defaults.Capability;
        var effects = defaults.Effects;
        foreach (var attribute in method.GetAttributes())
        {
            if (!IsDotBoxDAttribute(attribute, compilation, DotBoxDMetadataNames.HostBindingAttribute) ||
                attribute.ConstructorArguments.Length != 2 ||
                attribute.ConstructorArguments[0].Value is not string methodCapability ||
                string.IsNullOrWhiteSpace(methodCapability))
            {
                continue;
            }

            capability = methodCapability;
            effects = attribute.ConstructorArguments[1];
            break;
        }

        var returnType = DotBoxDTypeNameReader.UnwrapTaskLike(method.ReturnType);
        return (
            HostBindingObjectRoute(defaults.BindingPrefix, method),
            capability,
            AutoHostBindingSandboxEffects(effects, ReturnAllocates(returnType), method),
            IsTaskLike(method.ReturnType));
    }

    private static (string BindingPrefix, string Capability, TypedConstant Effects)? HostBindingObject(
        INamedTypeSymbol type,
        Compilation compilation)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (!IsDotBoxDAttribute(attribute, compilation, DotBoxDMetadataNames.HostBindingObjectAttribute) ||
                attribute.ConstructorArguments.Length != 3 ||
                attribute.ConstructorArguments[0].Value is not string bindingPrefix ||
                attribute.ConstructorArguments[1].Value is not string capability ||
                string.IsNullOrWhiteSpace(bindingPrefix) ||
                string.IsNullOrWhiteSpace(capability))
            {
                continue;
            }

            return (bindingPrefix, capability, attribute.ConstructorArguments[2]);
        }

        return null;
    }

    private static bool IsEligibleHostBindingObjectMethod(IMethodSymbol method)
        => method.MethodKind == MethodKind.Ordinary &&
           method.DeclaredAccessibility == Accessibility.Public &&
           !method.IsStatic &&
           !method.IsGenericMethod &&
           !method.IsImplicitlyDeclared &&
           !method.IsOverride;

    private static string HostBindingObjectRoute(string bindingPrefix, IMethodSymbol method)
    {
        var route = new StringBuilder(bindingPrefix)
            .Append('.')
            .Append(BindingIdentifierSegment(method.Name));
        foreach (var parameter in method.Parameters)
        {
            route.Append('.').Append(HostBindingObjectParameterType(parameter.Type));
        }

        return route.ToString();
    }

    private static string HostBindingObjectParameterType(ITypeSymbol type)
        => type.SpecialType switch
        {
            SpecialType.System_Boolean => "bool",
            SpecialType.System_Int32 => "i32",
            SpecialType.System_Int64 => "i64",
            SpecialType.System_Double or SpecialType.System_Single => "f64",
            SpecialType.System_String => "string",
            _ => BindingIdentifierSegment(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
        };

    private static string BindingIdentifierSegment(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
            {
                result.Append(character);
                continue;
            }

            result.Append('_').Append(((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
        }

        return result.ToString();
    }
}
