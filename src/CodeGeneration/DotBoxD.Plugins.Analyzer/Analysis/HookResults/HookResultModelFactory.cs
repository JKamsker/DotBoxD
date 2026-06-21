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

        if (type.ContainingType is not null)
        {
            // A nested result would be emitted as a phantom top-level type; require a top-level declaration.
            return Invalid(type, declaration, $"hook result '{type.Name}' must be a top-level type");
        }

        if (!type.IsValueType || !type.IsReadOnly)
        {
            // Builders construct via `new() { ... }` and dispatch constrains TResult to a struct, so a reference
            // (record class) result is not supported. Require readonly so generated With<Field> copies preserve
            // value-object semantics instead of exposing mutable result structs.
            return Invalid(type, declaration, $"hook result '{type.Name}' must be a readonly record struct");
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

        var fields = new List<HookResultField>(primary.Parameters.Length);
        var hasSuccess = false;
        var hasReason = false;
        foreach (var parameter in primary.Parameters)
        {
            var isSuccess = string.Equals(parameter.Name, SuccessField, StringComparison.Ordinal)
                && parameter.Type.SpecialType == SpecialType.System_Boolean;
            var isReason = string.Equals(parameter.Name, ReasonField, StringComparison.Ordinal)
                && parameter.Type.SpecialType == SpecialType.System_String
                && parameter.NullableAnnotation == NullableAnnotation.Annotated;
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
            EquatableArray<string>.FromOwned([.. ExistingMemberNames(type)]),
            hasSuccess,
            hasReason,
            diagnostic);
    }

    // A diagnostic-only model: builders are not emitted (HasSuccess/HasReason are false) and the generator
    // reports the DBXK112 message for a malformed [HookResult] shape.
    private static HookResultModel Invalid(INamedTypeSymbol type, TypeDeclarationSyntax declaration, string message)
        => new(
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            type.Name,
            string.Empty,
            EquatableArray<HookResultField>.FromOwned([]),
            EquatableArray<string>.FromOwned([]),
            HasSuccess: false,
            HasReason: false,
            new HookResultDiagnostic(PluginDiagnosticLocation.From(declaration.Identifier.GetLocation()), message));

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

    // Author-declared member names that must not be shadowed by a generated builder. Includes methods,
    // properties, and fields: a positional parameter named e.g. `Ok` synthesizes a property `Ok`, which would
    // collide (CS0102) with a generated `Ok()` method, so all member kinds are collected.
    private static IEnumerable<string> ExistingMemberNames(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (member is (IMethodSymbol or IPropertySymbol or IFieldSymbol) and { IsImplicitlyDeclared: false })
            {
                yield return member.Name;
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
