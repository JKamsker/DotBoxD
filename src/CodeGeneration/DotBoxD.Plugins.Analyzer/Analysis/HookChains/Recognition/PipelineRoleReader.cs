using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

/// <summary>
/// Analyzer-side role model for fluent hook/subscription chains. The roles are internal generator semantics:
/// public pipeline methods expose explicit IR companion parameters instead of carrying a public role attribute.
/// </summary>
internal enum PipelineCallRole
{
    Seed = 0,
    Filter = 1,
    Projection = 2,
    Run = 3,
    RunLocal = 4,
    Register = 5,
    RegisterLocal = 6,
}

/// <summary>
/// Resolves the pipeline role of a fluent call from a marked <c>[PipelineSurface]</c> receiver plus the public
/// method shape. <c>[PipelineSurface]</c> remains the type-level opt-in; per-method role markers no longer exist.
/// </summary>
internal static class PipelineRoleReader
{
    /// <summary>The inferred role of <paramref name="method"/>, or <c>null</c> when it is not a pipeline call.</summary>
    public static PipelineCallRole? RoleOf(IMethodSymbol? method, Compilation compilation)
    {
        if (method is null)
        {
            return null;
        }

        if (IsSeed(method, compilation))
        {
            return PipelineCallRole.Seed;
        }

        if (Transport(method.ContainingType, compilation) is null)
        {
            return null;
        }

        var role = RoleFromName(method.Name);
        if (role is null)
        {
            return null;
        }

        return HasExplicitIrCompanion(method, role.Value, compilation) ? role : null;
    }

    private static PipelineCallRole? RoleFromName(string methodName)
        => methodName switch
        {
            "Where" => PipelineCallRole.Filter,
            "Select" => PipelineCallRole.Projection,
            "Run" => PipelineCallRole.Run,
            "RunLocal" => PipelineCallRole.RunLocal,
            "Register" => PipelineCallRole.Register,
            "RegisterLocal" => PipelineCallRole.RegisterLocal,
            _ => null,
        };

    private static bool HasExplicitIrCompanion(
        IMethodSymbol method,
        PipelineCallRole role,
        Compilation compilation)
    {
        if (!TryGetIrCompanion(method, compilation, out var irParameter))
        {
            return false;
        }

        return IsStageRole(role)
            ? IsIRFunc(irParameter.Type)
            : IsTerminalRole(role) && IsIRKernel(irParameter.Type, compilation);
    }

    private static bool TryGetIrCompanion(
        IMethodSymbol method,
        Compilation compilation,
        out IParameterSymbol irParameter)
    {
        irParameter = null!;
        if (method.Parameters.Length < 2 || method.Parameters[0].Type.TypeKind != TypeKind.Delegate)
        {
            return false;
        }

        irParameter = method.Parameters[1];
        return IsOptionalNull(irParameter) &&
            HasIRBodyOf(irParameter, method.Parameters[0].Name, compilation);
    }

    private static bool IsStageRole(PipelineCallRole role)
        => role is PipelineCallRole.Filter or PipelineCallRole.Projection;

    private static bool IsTerminalRole(PipelineCallRole role)
        => role is PipelineCallRole.Run or PipelineCallRole.RunLocal or
            PipelineCallRole.Register or PipelineCallRole.RegisterLocal;

    private static bool IsOptionalNull(IParameterSymbol parameter)
        => parameter.IsOptional && parameter.HasExplicitDefaultValue && parameter.ExplicitDefaultValue is null;

    private static bool HasIRBodyOf(IParameterSymbol parameter, string sourceParameterName, Compilation compilation)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (IsDotBoxDAttribute(attribute, compilation, DotBoxDGenerationNames.TypeNames.IRBodyOfAttribute) &&
                attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is string parameterName &&
                string.Equals(parameterName, sourceParameterName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIRFunc(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var definition = named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return string.Equals(
                definition,
                DotBoxDGenerationNames.TypeNames.GlobalPrefix + DotBoxDGenerationNames.TypeNames.IRFunc2Original,
                StringComparison.Ordinal) ||
            string.Equals(
                definition,
                DotBoxDGenerationNames.TypeNames.GlobalPrefix + DotBoxDGenerationNames.TypeNames.IRFunc3Original,
                StringComparison.Ordinal);
    }

    private static bool IsIRKernel(ITypeSymbol type, Compilation compilation)
        => compilation.GetTypeByMetadataName(DotBoxDGenerationNames.TypeNames.IRKernel) is { } expected &&
           SymbolEqualityComparer.Default.Equals(type, expected);

    /// <summary>The transport declared by a <c>[PipelineSurface]</c> on <paramref name="type"/> (or a base
    /// type), mapped to <see cref="HookChainReceiverKind"/>; <c>null</c> when the type is not a marked surface.</summary>
    public static HookChainReceiverKind? Transport(INamedTypeSymbol? type, Compilation compilation)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.OriginalDefinition.GetAttributes())
            {
                if (IsDotBoxDAttribute(attribute, compilation, DotBoxDGenerationNames.TypeNames.PipelineSurfaceAttribute) &&
                    attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0].Value is int value)
                {
                    return value switch
                    {
                        0 => HookChainReceiverKind.Local,
                        1 => HookChainReceiverKind.Remote,
                        _ => null,
                    };
                }
            }
        }

        return null;
    }

    private static bool IsDotBoxDAttribute(AttributeData attribute, Compilation compilation, string metadataName)
        => compilation.GetTypeByMetadataName(metadataName) is { } expected &&
           SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected);

    private static bool IsSeed(IMethodSymbol method, Compilation compilation)
        => string.Equals(method.Name, "On", StringComparison.Ordinal) &&
           method.ReturnType is INamedTypeSymbol returnType &&
           Transport(returnType, compilation) is not null;
}
