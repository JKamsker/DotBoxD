using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

internal static partial class HookResultModelFactory
{
    private static bool TryGetTarget(
        GeneratorAttributeSyntaxContext context,
        out INamedTypeSymbol type,
        out TypeDeclarationSyntax declaration)
    {
        if (context.TargetSymbol is INamedTypeSymbol targetType &&
            context.TargetNode is TypeDeclarationSyntax targetDeclaration)
        {
            type = targetType;
            declaration = targetDeclaration;
            return true;
        }

        type = null!;
        declaration = null!;
        return false;
    }

    private static HookResultModel? TryGetInvalidDeclarationResult(
        INamedTypeSymbol type,
        TypeDeclarationSyntax declaration,
        Compilation compilation)
    {
        if (type.TypeParameters.Length > 0)
        {
            return Invalid(type, declaration, $"hook result '{type.Name}' must not be generic");
        }

        if (declaration.Modifiers.Any(SyntaxKind.FileKeyword))
        {
            return Invalid(
                type,
                declaration,
                $"hook result '{type.Name}' cannot be file-local because generated builders must attach to the same public result type",
                useUnsupportedShapeRule: true);
        }

        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return NonPartialResult(type, declaration, compilation);
        }

        if (type.ContainingType is not null)
        {
            // A nested result would be emitted as a phantom top-level type; require a top-level declaration.
            return Invalid(type, declaration, $"hook result '{type.Name}' must be a top-level type");
        }

        return ValidValueTypeShape(type, declaration);
    }

    private static HookResultModel? NonPartialResult(
        INamedTypeSymbol type,
        TypeDeclarationSyntax declaration,
        Compilation compilation)
        // A non-partial [HookResult] can't have IHookResult or the Ok()/Reject() builders generated for it.
        // If it doesn't already implement IHookResult, a later .Register/.RegisterLocal install (constrained
        // `where TResult : struct, IHookResult`) fails with a cryptic CS0315; surface DBXK112 so the missing
        // contract is explicit. A type that implements IHookResult by hand is valid and left alone.
        => IsValueTypeImplementingHookResult(type, compilation)
            ? null
            : Invalid(
                type,
                declaration,
                $"hook result '{type.Name}' must be declared 'partial' so the generator can add IHookResult and "
                + "the Ok()/Reject() builders, or it must implement IHookResult and declare those builders manually");

    private static HookResultModel? ValidValueTypeShape(INamedTypeSymbol type, TypeDeclarationSyntax declaration)
    {
        if (type.IsValueType && type.IsReadOnly)
        {
            return null;
        }

        // Builders construct via `new() { ... }` and dispatch constrains TResult to a struct, so a reference
        // (record class) result is not supported. Require readonly so generated With<Field> copies preserve
        // value-object semantics instead of exposing mutable result structs.
        return Invalid(type, declaration, $"hook result '{type.Name}' must be a readonly record struct");
    }

    private static HookResultModel CreateModel(
        INamedTypeSymbol type,
        TypeDeclarationSyntax declaration,
        IMethodSymbol primary)
    {
        var fields = new List<HookResultField>(primary.Parameters.Length);
        var controls = CollectFields(primary, fields);
        var diagnostic = controls.HasSuccess && controls.HasReason
            ? null
            : new HookResultDiagnostic(
                PluginDiagnosticLocation.From(declaration.Identifier.GetLocation()),
                $"hook result '{type.Name}' must declare a 'bool Success' and a 'string? Reason' field");
        return new HookResultModel(
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            type.Name,
            DeclarationKeywords(type),
            EquatableArray<HookResultField>.FromOwned([.. fields]),
            EquatableArray<HookResultExistingMember>.FromOwned([.. ExistingMembers(type)]),
            controls.HasSuccess,
            controls.HasReason,
            diagnostic);
    }

    private static HookResultControls CollectFields(IMethodSymbol primary, List<HookResultField> fields)
    {
        var controls = new HookResultControls(false, false);
        foreach (var parameter in primary.Parameters)
        {
            var isSuccess = IsSuccessParameter(parameter);
            var isReason = IsReasonParameter(parameter);
            controls = new HookResultControls(controls.HasSuccess || isSuccess, controls.HasReason || isReason);
            fields.Add(new HookResultField(
                parameter.Name,
                parameter.Type.ToDisplayString(FieldTypeFormat),
                ParameterName(parameter.Name),
                IsControlParameter(parameter)));
        }

        return controls;
    }

    private static bool IsSuccessParameter(IParameterSymbol parameter)
        => string.Equals(parameter.Name, SuccessField, StringComparison.Ordinal) &&
           parameter.Type.SpecialType == SpecialType.System_Boolean;

    private static bool IsReasonParameter(IParameterSymbol parameter)
        => string.Equals(parameter.Name, ReasonField, StringComparison.Ordinal) &&
           parameter.Type.SpecialType == SpecialType.System_String &&
           parameter.NullableAnnotation == NullableAnnotation.Annotated;

    private static bool IsControlParameter(IParameterSymbol parameter)
        => string.Equals(parameter.Name, SuccessField, StringComparison.Ordinal) ||
           string.Equals(parameter.Name, ReasonField, StringComparison.Ordinal);

    private readonly record struct HookResultControls(bool HasSuccess, bool HasReason);
}
