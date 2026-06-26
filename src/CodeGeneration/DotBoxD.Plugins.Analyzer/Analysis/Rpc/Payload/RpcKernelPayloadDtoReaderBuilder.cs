namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

internal static class RpcKernelPayloadDtoReaderBuilder
{
    public static string BuildReconstruction(
        INamedTypeSymbol type,
        IReadOnlyList<RecordMember> fields,
        Compilation? compilation = null)
    {
        if (TryResolveConstructor(type, fields, compilation) is { } constructor)
        {
            var construction = "new " + TypeName(type) + "(" +
                string.Join(", ", DtoConstructorArguments(fields, constructor.Symbol)) + ")";
            if (constructor.AssignedCount == fields.Count)
            {
                return "        return " + construction + ";";
            }

            if (!CanReconstructAllUnassignedFields(fields, constructor.Assigned, compilation))
            {
                throw new NotSupportedException(
                    $"Server extension DTO '{type.ToDisplayString()}' constructor '{constructor.Symbol.ToDisplayString()}' " +
                    "does not assign every public field and the remaining fields are not settable.");
            }

            return BuildInitializer("        return " + construction, fields, constructor.Assigned);
        }

        if (CanUseObjectInitializer(type, fields, compilation))
        {
            return BuildInitializer("        return new " + TypeName(type), fields, assigned: null);
        }

        throw new NotSupportedException(
            $"Server extension DTO '{type.ToDisplayString()}' must expose either a constructor matching its " +
            "public fields or a parameterless constructor with settable properties.");
    }

    private static List<string> DtoConstructorArguments(
        IReadOnlyList<RecordMember> fields,
        IMethodSymbol constructor)
    {
        var arguments = new List<string>(constructor.Parameters.Length);
        foreach (var parameter in constructor.Parameters)
        {
            var fieldIndex = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
            arguments.Add(FieldLocal(fieldIndex));
        }

        return arguments;
    }

    private static string BuildInitializer(string construction, IReadOnlyList<RecordMember> fields, bool[]? assigned)
    {
        var initializer = new StringBuilder();
        initializer.Append(construction).AppendLine();
        initializer.AppendLine("        {");
        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned is not null && (assigned[i] || !DotBoxDRpcTypeMapper.IsObjectInitializerWritable(fields[i])))
            {
                continue;
            }

            initializer.Append("            ").Append(Identifier(fields[i].Name)).Append(" = ")
                .Append(FieldLocal(i)).AppendLine(",");
        }

        initializer.Append("        };");
        return initializer.ToString();
    }

    private static ResolvedDtoConstructor? TryResolveConstructor(
        INamedTypeSymbol type,
        IReadOnlyList<RecordMember> fields,
        Compilation? compilation)
    {
        ResolvedDtoConstructor? partial = null;
        foreach (var constructor in type.InstanceConstructors)
        {
            if (!DotBoxDRpcTypeMapper.IsAccessibleFromGeneratedCode(constructor, compilation) ||
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

    private static bool CanReconstructAllUnassignedFields(
        IReadOnlyList<RecordMember> fields,
        bool[] assigned,
        Compilation? compilation)
        => fields.Where((_, index) => !assigned[index])
            .All(field =>
                DotBoxDRpcTypeMapper.IsObjectInitializerWritable(field, compilation) ||
                DotBoxDRpcTypeMapper.IsDerivedFromAssignedFields(field, fields, assigned));

    private static bool CanUseObjectInitializer(
        INamedTypeSymbol type,
        IReadOnlyList<RecordMember> fields,
        Compilation? compilation)
    {
        if (fields.Count == 0 || (!type.IsValueType && !HasAccessibleParameterlessConstructor(type, compilation)))
        {
            return false;
        }

        foreach (var field in fields)
        {
            if (!DotBoxDRpcTypeMapper.IsObjectInitializerWritable(field, compilation))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type, Compilation? compilation)
        => type.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 0 &&
            DotBoxDRpcTypeMapper.IsAccessibleFromGeneratedCode(constructor, compilation));

    private static string FieldLocal(int index)
        => "__field" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Identifier(string name) => "@" + name;

    private static int AssignedCount(bool[] assigned)
        => assigned.Count(static item => item);

    private sealed record ResolvedDtoConstructor(IMethodSymbol Symbol, bool[] Assigned, int AssignedCount);
}
