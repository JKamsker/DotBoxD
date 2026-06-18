namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

/// <summary>
/// DTO marshalling for <see cref="RpcKernelValueConversionEmitter"/>: a DTO is written as a positional
/// <c>Record</c> of its public instance properties (declaration order) and read back through a constructor
/// whose parameters match those fields by name and type. All field expressions are computed before the
/// owning helper method is appended, so nested list/DTO helpers never interleave with the body being built.
/// </summary>
internal sealed partial class RpcKernelValueConversionEmitter
{
    private string EnsureDtoWriter(INamedTypeSymbol type)
    {
        var key = TypeKey(type);
        if (_writers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName("Write");
        _writers[key] = method;
        var fieldExpressions = DtoWriteExpressions(type);
        _helpers.Append("    private static global::DotBoxD.Plugins.KernelRpcValue ").Append(method)
            .Append('(').Append(TypeName(type)).AppendLine(" value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        return global::DotBoxD.Plugins.KernelRpcValue.Record(new global::DotBoxD.Plugins.KernelRpcValue[]");
        _helpers.AppendLine("        {");
        foreach (var fieldExpression in fieldExpressions)
        {
            _helpers.Append("            ").Append(fieldExpression).AppendLine(",");
        }

        _helpers.AppendLine("        });");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private string EnsureDtoReader(INamedTypeSymbol type)
    {
        var key = TypeKey(type);
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName("Read");
        _readers[key] = method;
        var fields = DotBoxDRpcTypeMapper.RecordFields(type);
        var constructor = ResolveConstructor(type, fields);
        var constructorArguments = DtoConstructorArguments(fields, constructor);
        _helpers.Append("    private static ").Append(TypeName(type)).Append(' ').Append(method)
            .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.Record);");
        _helpers.AppendLine("        var __fields = value.Items;");
        _helpers.Append("        if (__fields.Length != ").Append(fields.Count).AppendLine(")");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension record field count did not match the generated DTO shape.\");");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        _helpers.Append("        return new ").Append(TypeName(type)).Append('(');
        _helpers.Append(string.Join(", ", constructorArguments));
        _helpers.AppendLine(");");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private List<string> DtoWriteExpressions(INamedTypeSymbol type)
    {
        var fields = DotBoxDRpcTypeMapper.RecordFields(type);
        var expressions = new List<string>(fields.Count);
        foreach (var field in fields)
        {
            expressions.Add(WriteExpression(field.Type, "value." + Identifier(field.Name)));
        }

        return expressions;
    }

    private List<string> DtoConstructorArguments(
        IReadOnlyList<IPropertySymbol> fields,
        IMethodSymbol constructor)
    {
        var arguments = new List<string>(constructor.Parameters.Length);
        foreach (var parameter in constructor.Parameters)
        {
            var fieldIndex = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
            arguments.Add(ReadExpression(fields[fieldIndex].Type, "__fields[" + fieldIndex + "]"));
        }

        return arguments;
    }

    private static IMethodSymbol ResolveConstructor(INamedTypeSymbol type, IReadOnlyList<IPropertySymbol> fields)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility is not (
                    Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal) ||
                constructor.Parameters.Length != fields.Count ||
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
                return constructor;
            }
        }

        throw new NotSupportedException(
            $"Server extension DTO '{type.ToDisplayString()}' must expose a constructor matching its public fields.");
    }
}
