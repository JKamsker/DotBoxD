using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcDtoConstructorAssignmentVerifier
{
    public static bool TryAssignConstructorParameter(
        IMethodSymbol constructor,
        IReadOnlyList<RecordMember> fields,
        bool[] assigned,
        IParameterSymbol parameter,
        Compilation? compilation)
        => TryAssignConstructorParameter(constructor, fields, assigned, parameter, compilation, out _);

    public static bool TryAssignConstructorParameter(
        IMethodSymbol constructor,
        IReadOnlyList<RecordMember> fields,
        bool[] assigned,
        IParameterSymbol parameter,
        Compilation? compilation,
        out int fieldIndex)
    {
        fieldIndex = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
        if (fieldIndex < 0)
        {
            return parameter.HasExplicitDefaultValue;
        }

        if (parameter.RefKind != RefKind.None)
        {
            assigned[fieldIndex] = true;
            return true;
        }

        if (assigned[fieldIndex] ||
            !ConstructorPreservesMember(constructor, fields[fieldIndex], parameter, compilation))
        {
            return false;
        }

        assigned[fieldIndex] = true;
        return true;
    }

    public static bool ConstructorPreservesMember(
        IMethodSymbol constructor,
        RecordMember member,
        IParameterSymbol parameter,
        Compilation? compilation)
    {
        if (DotBoxDRpcTypeMapper.IsObjectInitializerWritable(member, compilation) ||
            constructor.IsImplicitlyDeclared)
        {
            return true;
        }

        if (constructor.DeclaringSyntaxReferences.Length == 0)
        {
            return true;
        }

        foreach (var reference in constructor.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax();
            var model = compilation?.ContainsSyntaxTree(syntax.SyntaxTree) == true
                ? compilation.GetSemanticModel(syntax.SyntaxTree)
                : null;
            if (syntax is ConstructorDeclarationSyntax declaration &&
                (RpcDtoConstructorMemberAssignmentInspector.BodyPreservesMember(
                    declaration,
                    member,
                    parameter,
                    model) ||
                 BaseConstructorPreservesMember(
                    declaration.Initializer?.ArgumentList,
                    declaration.Initializer,
                    member,
                    parameter,
                    model,
                    compilation)))
            {
                return true;
            }

            if (syntax is TypeDeclarationSyntax typeDeclaration &&
                typeDeclaration.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>() is { } baseTypes)
            {
                foreach (var baseType in baseTypes)
                {
                    if (BaseConstructorPreservesMember(
                            baseType.ArgumentList,
                            baseType,
                            member,
                            parameter,
                            model,
                            compilation))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool BaseConstructorPreservesMember(
        ArgumentListSyntax? arguments,
        SyntaxNode? invocation,
        RecordMember member,
        IParameterSymbol parameter,
        SemanticModel? model,
        Compilation? compilation)
    {
        if (arguments is null ||
            invocation is null ||
            model?.GetSymbolInfo(invocation).Symbol is not IMethodSymbol baseConstructor)
        {
            return false;
        }

        for (var index = 0; index < arguments.Arguments.Count; index++)
        {
            var argument = arguments.Arguments[index];
            if (model.GetSymbolInfo(argument.Expression).Symbol is not IParameterSymbol source ||
                !SymbolEqualityComparer.Default.Equals(source, parameter))
            {
                continue;
            }

            var targetIndex = argument.NameColon is null
                ? index
                : ParameterIndex(baseConstructor, argument.NameColon.Name.Identifier.ValueText);
            if (targetIndex >= 0 &&
                targetIndex < baseConstructor.Parameters.Length &&
                ConstructorPreservesMember(
                    baseConstructor,
                    member,
                    baseConstructor.Parameters[targetIndex],
                    compilation))
            {
                return true;
            }
        }

        return false;
    }

    private static int ParameterIndex(IMethodSymbol constructor, string name)
    {
        for (var index = 0; index < constructor.Parameters.Length; index++)
        {
            if (string.Equals(constructor.Parameters[index].Name, name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
