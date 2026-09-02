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
            if (SyntaxPreservesMember(reference.GetSyntax(), member, parameter, compilation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SyntaxPreservesMember(
        SyntaxNode syntax,
        RecordMember member,
        IParameterSymbol parameter,
        Compilation? compilation)
    {
        var model = compilation?.ContainsSyntaxTree(syntax.SyntaxTree) == true
            ? compilation.GetSemanticModel(syntax.SyntaxTree)
            : null;

        return syntax switch
        {
            ConstructorDeclarationSyntax declaration =>
                ConstructorDeclarationPreservesMember(declaration, member, parameter, model, compilation),
            TypeDeclarationSyntax typeDeclaration =>
                PrimaryConstructorPreservesMember(typeDeclaration, member, parameter, model, compilation),
            _ => false
        };
    }

    private static bool ConstructorDeclarationPreservesMember(
        ConstructorDeclarationSyntax declaration,
        RecordMember member,
        IParameterSymbol parameter,
        SemanticModel? model,
        Compilation? compilation)
        => RpcDtoConstructorMemberAssignmentInspector.BodyPreservesMember(
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
               compilation);

    private static bool PrimaryConstructorPreservesMember(
        TypeDeclarationSyntax typeDeclaration,
        RecordMember member,
        IParameterSymbol parameter,
        SemanticModel? model,
        Compilation? compilation)
    {
        var baseTypes = typeDeclaration.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>();
        if (baseTypes is null)
        {
            return false;
        }

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
        if (arguments is null || invocation is null)
        {
            return false;
        }

        var baseConstructor = model?.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        return baseConstructor is not null &&
            ForwardedParameterPreservesMember(
                arguments,
                baseConstructor,
                member,
                parameter,
                model,
                compilation);
    }

    private static bool ForwardedParameterPreservesMember(
        ArgumentListSyntax arguments,
        IMethodSymbol baseConstructor,
        RecordMember member,
        IParameterSymbol parameter,
        SemanticModel? model,
        Compilation? compilation)
    {
        for (var index = 0; index < arguments.Arguments.Count; index++)
        {
            var argument = arguments.Arguments[index];
            if (!IsForwardedParameter(argument, parameter, model) ||
                !TryGetTargetParameter(baseConstructor, argument, index, out var targetParameter))
            {
                continue;
            }

            if (ConstructorPreservesMember(baseConstructor, member, targetParameter, compilation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsForwardedParameter(
        ArgumentSyntax argument,
        IParameterSymbol parameter,
        SemanticModel? model)
        => model?.GetSymbolInfo(argument.Expression).Symbol is IParameterSymbol source &&
           SymbolEqualityComparer.Default.Equals(source, parameter);

    private static bool TryGetTargetParameter(
        IMethodSymbol constructor,
        ArgumentSyntax argument,
        int positionalIndex,
        out IParameterSymbol parameter)
    {
        var index = argument.NameColon is null
            ? positionalIndex
            : ParameterIndex(constructor, argument.NameColon.Name.Identifier.ValueText);
        if ((uint)index >= (uint)constructor.Parameters.Length)
        {
            parameter = null!;
            return false;
        }

        parameter = constructor.Parameters[index];
        return true;
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
