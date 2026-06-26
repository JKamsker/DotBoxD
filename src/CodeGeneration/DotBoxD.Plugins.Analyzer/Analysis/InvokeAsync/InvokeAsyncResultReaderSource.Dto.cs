using System.Text;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncResultReaderSource
{
    private string BuildDtoReconstruction(INamedTypeSymbol type, IReadOnlyList<RecordMember> fields)
    {
        if (TryResolveConstructor(type, fields) is { } constructor)
        {
            var construction = "new " + TypeName(type) + "(" +
                string.Join(", ", DtoConstructorArguments(fields, constructor.Symbol)) + ")";
            if (constructor.AssignedCount == fields.Count)
            {
                return "            return " + construction + ";";
            }

            if (!CanReconstructAllUnassignedFields(fields, constructor.Assigned))
            {
                throw new NotSupportedException(
                    $"InvokeAsync DTO '{type.ToDisplayString()}' constructor '{constructor.Symbol.ToDisplayString()}' " +
                    "does not assign every public field and the remaining fields are not settable.");
            }

            return BuildDtoInitializer("            return " + construction, fields, constructor.Assigned);
        }

        if (CanUseObjectInitializer(type, fields))
        {
            return BuildDtoInitializer("            return new " + TypeName(type), fields, assigned: null);
        }

        throw new NotSupportedException(
            $"InvokeAsync DTO '{type.ToDisplayString()}' must expose either a constructor matching its " +
            "public fields or a parameterless constructor with settable properties.");
    }

    private string BuildDtoInitializer(string construction, IReadOnlyList<RecordMember> fields, bool[]? assigned)
    {
        var initializer = new StringBuilder();
        initializer.Append(construction).AppendLine();
        initializer.AppendLine("            {");
        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned is not null && (assigned[i] || !DotBoxDRpcTypeMapper.IsObjectInitializerWritable(fields[i])))
            {
                continue;
            }

            initializer.Append("                ").Append(Identifier(fields[i].Name)).Append(" = ")
                .Append(ReadExpression(fields[i].Type, "value.GetItem(" + i + ")")).AppendLine(",");
        }

        initializer.Append("            };");
        return initializer.ToString();
    }

    private List<string> DtoConstructorArguments(IReadOnlyList<RecordMember> fields, IMethodSymbol constructor)
    {
        var arguments = new List<string>(constructor.Parameters.Length);
        foreach (var parameter in constructor.Parameters)
        {
            var fieldIndex = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
            arguments.Add(ReadExpression(fields[fieldIndex].Type, "value.GetItem(" + fieldIndex + ")"));
        }

        return arguments;
    }

    private ResolvedDtoConstructor? TryResolveConstructor(INamedTypeSymbol type, IReadOnlyList<RecordMember> fields)
    {
        ResolvedDtoConstructor? partial = null;
        foreach (var constructor in type.InstanceConstructors)
        {
            if (!DotBoxDRpcTypeMapper.IsAccessibleFromGeneratedCode(constructor, _compilation) ||
                constructor.Parameters.Length > fields.Count ||
                constructor.Parameters.Length == 0)
            {
                continue;
            }

            var matched = true;
            var assigned = new bool[fields.Count];
            foreach (var parameter in constructor.Parameters)
            {
                var fieldIndex = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
                if (fieldIndex < 0 || assigned[fieldIndex])
                {
                    matched = false;
                    break;
                }

                assigned[fieldIndex] = true;
            }

            if (matched)
            {
                var assignedCount = AssignedCount(assigned);
                var resolved = new ResolvedDtoConstructor(constructor, assigned, assignedCount);
                if (assignedCount == fields.Count)
                {
                    return resolved;
                }

                if (partial is null || assignedCount > partial.AssignedCount)
                {
                    partial = resolved;
                }
            }
        }

        return partial;
    }

    private bool CanUseObjectInitializer(INamedTypeSymbol type, IReadOnlyList<RecordMember> fields)
    {
        if (fields.Count == 0 || (!type.IsValueType && !HasAccessibleParameterlessConstructor(type)))
        {
            return false;
        }

        foreach (var field in fields)
        {
            if (!DotBoxDRpcTypeMapper.IsObjectInitializerWritable(field, _compilation))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type)
        => type.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 0 &&
            DotBoxDRpcTypeMapper.IsAccessibleFromGeneratedCode(constructor, _compilation));

    private bool CanReconstructAllUnassignedFields(IReadOnlyList<RecordMember> fields, bool[] assigned)
        => fields.Where((_, index) => !assigned[index])
            .All(field =>
                DotBoxDRpcTypeMapper.IsObjectInitializerWritable(field, _compilation) ||
                DotBoxDRpcTypeMapper.IsDerivedFromAssignedFields(field, fields, assigned));

    private static string Identifier(string name)
        => "@" + name;

    private static int AssignedCount(bool[] assigned)
        => assigned.Count(static item => item);

    private sealed record ResolvedDtoConstructor(IMethodSymbol Symbol, bool[] Assigned, int AssignedCount);
}
