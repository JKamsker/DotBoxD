using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

/// <summary>
/// Reads a <c>[HookResult]</c>-annotated positional record into a <see cref="HookResultModel"/> for the
/// builder generator. Supports a partial positional record/record-struct that declares a <c>bool Success</c>
/// and a <c>string? Reason</c> field; anything else either yields no model (non-positional / non-partial — the
/// builders simply are not generated) or a DBXK112 diagnostic (positional but missing the control fields).
/// </summary>
internal static class HookResultModelFactory
{
    private const string SuccessField = "Success";
    private const string ReasonField = "Reason";

    private static readonly SymbolDisplayFormat FieldTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static HookResultModel? Create(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol type ||
            context.TargetNode is not TypeDeclarationSyntax declaration ||
            !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return null;
        }

        var primary = PrimaryConstructor(type);
        if (primary is null)
        {
            // A non-positional record has no constructor-shaped field list to build setters from; the builder
            // surface only targets the positional records the plan describes, so generate nothing.
            return null;
        }

        var fields = new List<HookResultField>(primary.Parameters.Length);
        var hasSuccess = false;
        var hasReason = false;
        foreach (var parameter in primary.Parameters)
        {
            var isSuccess = string.Equals(parameter.Name, SuccessField, StringComparison.Ordinal)
                && parameter.Type.SpecialType == SpecialType.System_Boolean;
            var isReason = string.Equals(parameter.Name, ReasonField, StringComparison.Ordinal)
                && parameter.Type.SpecialType == SpecialType.System_String;
            hasSuccess |= isSuccess;
            hasReason |= isReason;

            var isControl = string.Equals(parameter.Name, SuccessField, StringComparison.Ordinal)
                || string.Equals(parameter.Name, ReasonField, StringComparison.Ordinal);
            fields.Add(new HookResultField(
                parameter.Name,
                parameter.Type.ToDisplayString(FieldTypeFormat),
                ParameterName(parameter.Name),
                isControl));
        }

        var diagnostic = hasSuccess && hasReason
            ? null
            : new HookResultDiagnostic(
                PluginDiagnosticLocation.From(declaration.Identifier.GetLocation()),
                $"hook result '{type.Name}' must declare a 'bool Success' and a 'string? Reason' field");

        return new HookResultModel(
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            type.Name,
            DeclarationKeywords(type),
            EquatableArray<HookResultField>.FromOwned([.. fields]),
            EquatableArray<string>.FromOwned([.. ExistingMethodNames(type)]),
            hasSuccess,
            hasReason,
            diagnostic);
    }

    // The positional record's primary constructor: the instance constructor that is neither the implicit
    // parameterless struct constructor nor the synthesized single-parameter copy constructor.
    private static IMethodSymbol? PrimaryConstructor(INamedTypeSymbol type)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.Parameters.Length == 0)
            {
                continue;
            }

            if (constructor.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, type))
            {
                continue;
            }

            return constructor;
        }

        return null;
    }

    private static IEnumerable<string> ExistingMethodNames(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (member is IMethodSymbol { IsImplicitlyDeclared: false } method)
            {
                yield return method.Name;
            }
        }
    }

    private static string DeclarationKeywords(INamedTypeSymbol type)
    {
        var readOnlyPrefix = type.IsValueType && type.IsReadOnly ? "readonly " : string.Empty;
        var structSuffix = type.IsValueType ? " struct" : string.Empty;
        return readOnlyPrefix + "partial record" + structSuffix;
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
