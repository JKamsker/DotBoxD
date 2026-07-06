using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcDtoConstructorResolver
{
    public static ResolvedDtoConstructor? TryResolve(
        INamedTypeSymbol type,
        IReadOnlyList<RecordMember> fields,
        Compilation? compilation)
    {
        ResolvedDtoConstructor? partial = null;
        ResolvedDtoConstructor? rejectedPartial = null;
        foreach (var constructor in type.InstanceConstructors)
        {
            if (!TryMatchConstructor(constructor, fields, compilation, out var assigned))
            {
                continue;
            }

            RpcDtoFieldMatcher.ValidateNoRefLikeParameters(
                constructor,
                $"Server extension DTO '{type.ToDisplayString()}'");

            var assignedCount = AssignedCount(assigned);
            var resolved = new ResolvedDtoConstructor(constructor, assigned, assignedCount);
            if (assignedCount == fields.Count)
            {
                return resolved;
            }

            SelectPartialConstructor(fields, compilation, resolved, ref partial, ref rejectedPartial);
        }

        return partial ?? rejectedPartial;
    }

    public static void ThrowIfRequiredReadOnlyMembersNeedSetsRequiredMembers(
        INamedTypeSymbol type,
        IReadOnlyList<RecordMember> fields,
        IMethodSymbol constructor,
        Compilation? compilation)
    {
        if (HasSetsRequiredMembers(constructor))
        {
            return;
        }

        foreach (var field in fields)
        {
            if (IsRequiredMember(field) &&
                !DotBoxDRpcTypeMapper.IsObjectInitializerWritable(field, compilation))
            {
                throw new NotSupportedException(
                    $"Server extension DTO '{type.ToDisplayString()}' required field '{field.Name}' is read-only; " +
                    "mark the constructor with SetsRequiredMembers or make the member settable.");
            }
        }
    }

    private static bool TryMatchConstructor(
        IMethodSymbol constructor,
        IReadOnlyList<RecordMember> fields,
        Compilation? compilation,
        out bool[] assigned)
    {
        assigned = new bool[fields.Count];
        if (!DotBoxDRpcTypeMapper.IsAccessibleFromGeneratedCode(constructor, compilation) ||
            constructor.Parameters.Length == 0)
        {
            return false;
        }

        foreach (var parameter in constructor.Parameters)
        {
            if (!TryAssignConstructorParameter(fields, assigned, parameter))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAssignConstructorParameter(
        IReadOnlyList<RecordMember> fields,
        bool[] assigned,
        IParameterSymbol parameter)
    {
        var fieldIndex = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
        if (fieldIndex < 0)
        {
            return parameter.HasExplicitDefaultValue;
        }

        if (assigned[fieldIndex])
        {
            return false;
        }

        assigned[fieldIndex] = true;
        return true;
    }

    private static void SelectPartialConstructor(
        IReadOnlyList<RecordMember> fields,
        Compilation? compilation,
        ResolvedDtoConstructor candidate,
        ref ResolvedDtoConstructor? partial,
        ref ResolvedDtoConstructor? rejectedPartial)
    {
        if (DotBoxDRpcTypeMapper.CanReconstructFromAssignedFields(fields, candidate.Assigned, compilation))
        {
            SelectBetterConstructor(candidate, ref partial);
            return;
        }

        SelectBetterConstructor(candidate, ref rejectedPartial);
    }

    private static void SelectBetterConstructor(
        ResolvedDtoConstructor candidate,
        ref ResolvedDtoConstructor? selected)
    {
        if (selected is null || candidate.AssignedCount > selected.AssignedCount)
        {
            selected = candidate;
        }
    }

    private static int AssignedCount(bool[] assigned)
        => assigned.Count(static item => item);

    private static bool IsRequiredMember(RecordMember field)
        => field.Symbol switch
        {
            IPropertySymbol property => property.IsRequired,
            IFieldSymbol fieldSymbol => fieldSymbol.IsRequired,
            _ => false,
        };

    private static bool HasSetsRequiredMembers(IMethodSymbol constructor)
        => constructor.GetAttributes().Any(attribute => string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute",
            StringComparison.Ordinal));
}

internal sealed record ResolvedDtoConstructor(IMethodSymbol Symbol, bool[] Assigned, int AssignedCount);
