namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

internal sealed partial class RpcKernelValueConversionEmitter
{
    private ResolvedDtoConstructor? TryResolveConstructor(INamedTypeSymbol type, IReadOnlyList<RecordMember> fields)
    {
        ResolvedDtoConstructor? partial = null;
        ResolvedDtoConstructor? rejectedPartial = null;
        foreach (var constructor in type.InstanceConstructors)
        {
            if (!TryMatchConstructor(constructor, fields, out var assigned))
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

            SelectPartialConstructor(fields, resolved, ref partial, ref rejectedPartial);
        }

        return partial ?? rejectedPartial;
    }

    private bool TryMatchConstructor(
        IMethodSymbol constructor,
        IReadOnlyList<RecordMember> fields,
        out bool[] assigned)
    {
        assigned = new bool[fields.Count];
        if (!DotBoxDRpcTypeMapper.IsAccessibleFromGeneratedCode(constructor, _compilation) ||
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

    private void SelectPartialConstructor(
        IReadOnlyList<RecordMember> fields,
        ResolvedDtoConstructor candidate,
        ref ResolvedDtoConstructor? partial,
        ref ResolvedDtoConstructor? rejectedPartial)
    {
        if (DotBoxDRpcTypeMapper.CanReconstructFromAssignedFields(fields, candidate.Assigned, _compilation))
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
}
