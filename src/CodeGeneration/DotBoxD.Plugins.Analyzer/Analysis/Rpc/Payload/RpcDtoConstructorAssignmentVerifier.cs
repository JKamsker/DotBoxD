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
            if (reference.GetSyntax() is not ConstructorDeclarationSyntax declaration)
            {
                continue;
            }

            var model = compilation?.ContainsSyntaxTree(declaration.SyntaxTree) == true
                ? compilation.GetSemanticModel(declaration.SyntaxTree)
                : null;
            if (RpcDtoConstructorMemberAssignmentInspector.BodyPreservesMember(
                    declaration,
                    member,
                    parameter,
                    model))
            {
                return true;
            }
        }

        return RpcDtoPrimaryConstructorMemberAssignmentInspector.PreservesMember(
            constructor,
            member,
            parameter,
            compilation);
    }
}
