using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

/// <summary>
/// Reads a <c>[HookResult]</c>-annotated positional record into a <see cref="HookResultModel"/> for the
/// builder generator. Supports a top-level readonly partial positional record struct that declares a
/// <c>bool Success</c> and a <c>string? Reason</c> field; anything else either yields no model (non-partial —
/// the builders simply are not generated) or a DBXK112 diagnostic.
/// </summary>
internal static partial class HookResultModelFactory
{
    private const string SuccessField = "Success";
    private const string ReasonField = "Reason";

    private static readonly SymbolDisplayFormat FieldTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static HookResultModel? Create(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetTarget(context, out var type, out var declaration))
        {
            return null;
        }

        if (TryGetInvalidDeclarationResult(type, declaration, context.SemanticModel.Compilation) is { } invalid)
        {
            return invalid;
        }

        if (ShouldSkipGeneration(type, declaration, context.SemanticModel.Compilation))
        {
            return null;
        }

        if (declaration is not RecordDeclarationSyntax { ParameterList: { } parameters })
        {
            return Invalid(type, declaration, $"hook result '{type.Name}' must be a positional record struct");
        }

        var primary = PrimaryConstructor(type, parameters);
        if (primary is null)
        {
            return Invalid(type, declaration, $"hook result '{type.Name}' must be a positional record struct");
        }

        return CreateModel(type, declaration, primary);
    }

    private static bool ShouldSkipGeneration(
        INamedTypeSymbol type,
        TypeDeclarationSyntax declaration,
        Compilation compilation)
        => !declaration.Modifiers.Any(SyntaxKind.PartialKeyword) &&
           IsValueTypeImplementingHookResult(type, compilation);

    private static HookResultModel Invalid(INamedTypeSymbol type, TypeDeclarationSyntax declaration, string message, bool useUnsupportedShapeRule = false)
        => new(
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            type.Name,
            string.Empty,
            EquatableArray<HookResultField>.FromOwned([]),
            EquatableArray<HookResultExistingMember>.FromOwned([]),
            HasSuccess: false,
            HasReason: false,
            new HookResultDiagnostic(
                PluginDiagnosticLocation.From(declaration.Identifier.GetLocation()),
                message,
                useUnsupportedShapeRule));

    internal static bool CanSatisfyHookResult(
        INamedTypeSymbol type,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (type.TypeParameters.Length > 0)
        {
            return false;
        }

        return IsValueTypeImplementingHookResult(type, compilation) ||
            IsValidGeneratedHookResult(type, cancellationToken);
    }

    private static bool IsValueTypeImplementingHookResult(INamedTypeSymbol type, Compilation compilation)
    {
        if (!type.IsValueType)
        {
            return false;
        }

        var hookResult = compilation.GetTypeByMetadataName("DotBoxD.Abstractions.IHookResult");
        if (hookResult is null)
        {
            return false;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(@interface, hookResult))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidGeneratedHookResult(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        if (type is not { IsValueType: true, IsReadOnly: true, IsRecord: true, ContainingType: null } ||
            type.TypeParameters.Length > 0 ||
            !HasHookResultAttribute(type))
        {
            return false;
        }

        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not RecordDeclarationSyntax
                {
                    ParameterList: { } parameters
                } declaration ||
                declaration.Modifiers.Any(SyntaxKind.FileKeyword) ||
                !declaration.Modifiers.Any(SyntaxKind.PartialKeyword) ||
                PrimaryConstructor(type, parameters) is not { } primary)
            {
                continue;
            }

            if (HasControlConstructorParameters(primary))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasHookResultAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.HookResultAttribute,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasControlConstructorParameters(IMethodSymbol primary)
    {
        var hasSuccess = false;
        var hasReason = false;
        foreach (var parameter in primary.Parameters)
        {
            hasSuccess |= string.Equals(parameter.Name, SuccessField, StringComparison.Ordinal)
                && parameter.Type.SpecialType == SpecialType.System_Boolean;
            hasReason |= string.Equals(parameter.Name, ReasonField, StringComparison.Ordinal)
                && parameter.Type.SpecialType == SpecialType.System_String
                && parameter.NullableAnnotation == NullableAnnotation.Annotated;
        }

        return hasSuccess && hasReason;
    }

    // The positional record's primary constructor: the instance constructor that is neither the implicit
    // parameterless struct constructor nor the synthesized single-parameter copy constructor.
    private static IMethodSymbol? PrimaryConstructor(
        INamedTypeSymbol type,
        ParameterListSyntax parameters)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.Parameters.Length != parameters.Parameters.Count)
            {
                continue;
            }

            var matches = true;
            for (var i = 0; i < parameters.Parameters.Count; i++)
            {
                if (!string.Equals(
                        constructor.Parameters[i].Name,
                        parameters.Parameters[i].Identifier.ValueText,
                        StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return constructor;
            }
        }

        return null;
    }

    private static string DeclarationKeywords(INamedTypeSymbol type)
    {
        var accessibilityPrefix = type.DeclaredAccessibility switch
        {
            Accessibility.Public => "public ",
            Accessibility.Internal => "internal ",
            _ => string.Empty
        };
        var readOnlyPrefix = type.IsValueType && type.IsReadOnly ? "readonly " : string.Empty;
        var structSuffix = type.IsValueType ? " struct" : string.Empty;
        return accessibilityPrefix + readOnlyPrefix + "partial record" + structSuffix;
    }

    private static string ParameterName(string fieldName)
    {
        var camel = char.ToLowerInvariant(fieldName[0]) + fieldName.Substring(1);
        return SyntaxFacts.GetKeywordKind(camel) == SyntaxKind.None &&
               SyntaxFacts.GetContextualKeywordKind(camel) == SyntaxKind.None
            ? camel
            : "@" + camel;
    }
}
