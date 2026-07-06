using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Model;

internal static class ModuleSerializationGuard
{
    public static void ThrowIfMalformed(SandboxModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var diagnostics = new List<SandboxDiagnostic>();
        CheckCapabilityRequests(module.CapabilityRequests, diagnostics);
        CheckMetadata(module.Metadata, diagnostics);
        CheckFunctions(module.Functions, diagnostics);

        if (diagnostics.Count > 0)
        {
            throw new SandboxValidationException(diagnostics);
        }
    }

    private static void CheckCapabilityRequests(
        IReadOnlyList<CapabilityRequest> requests,
        List<SandboxDiagnostic> diagnostics)
    {
        for (var i = 0; i < requests.Count; i++)
        {
            if (requests[i] is null)
            {
                AddNull(diagnostics, $"capabilityRequests entry at index {i}");
            }
        }
    }

    private static void CheckMetadata(
        IReadOnlyDictionary<string, string> metadata,
        List<SandboxDiagnostic> diagnostics)
    {
        foreach (var item in metadata)
        {
            if (item.Key is null)
            {
                AddNull(diagnostics, "metadata key");
            }

            if (item.Value is null)
            {
                AddNull(diagnostics, $"metadata value for key '{item.Key ?? "<null>"}'");
            }
        }
    }

    private static void CheckFunctions(
        IReadOnlyList<SandboxFunction> functions,
        List<SandboxDiagnostic> diagnostics)
    {
        for (var i = 0; i < functions.Count; i++)
        {
            var function = functions[i];
            if (function is null)
            {
                AddNull(diagnostics, $"functions entry at index {i}");
                continue;
            }

            CheckFunction(function, diagnostics);
        }
    }

    private static void CheckFunction(
        SandboxFunction function,
        List<SandboxDiagnostic> diagnostics)
    {
        CheckType(function.ReturnType, $"function '{function.Id}' return type", diagnostics);
        CheckParameters(function, diagnostics);
        CheckStatements(function.Body, $"function '{function.Id}' body", diagnostics);
    }

    private static void CheckParameters(
        SandboxFunction function,
        List<SandboxDiagnostic> diagnostics)
    {
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var parameter = function.Parameters[i];
            if (parameter is null)
            {
                AddNull(diagnostics, $"function '{function.Id}' parameters entry at index {i}");
                continue;
            }

            CheckType(parameter.Type, $"parameter '{parameter.Name}' type", diagnostics);
        }
    }

    private static void CheckStatements(
        IReadOnlyList<Statement> statements,
        string path,
        List<SandboxDiagnostic> diagnostics)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            var statement = statements[i];
            if (statement is null)
            {
                AddNull(diagnostics, $"{path} statement at index {i}");
                continue;
            }

            CheckStatement(statement, $"{path} statement at index {i}", diagnostics);
        }
    }

    private static void CheckStatement(
        Statement statement,
        string path,
        List<SandboxDiagnostic> diagnostics)
    {
        switch (statement)
        {
            case AssignmentStatement assignment:
                CheckExpression(assignment.Value, $"{path} value", diagnostics);
                break;
            case ReturnStatement ret:
                CheckExpression(ret.Value, $"{path} return value", diagnostics);
                break;
            case ExpressionStatement expression:
                CheckExpression(expression.Value, $"{path} expression value", diagnostics);
                break;
            case IfStatement branch:
                CheckExpression(branch.Condition, $"{path} condition", diagnostics);
                CheckStatements(branch.Then, $"{path} then body", diagnostics);
                CheckStatements(branch.Else, $"{path} else body", diagnostics);
                break;
            case WhileStatement loop:
                CheckExpression(loop.Condition, $"{path} condition", diagnostics);
                CheckStatements(loop.Body, $"{path} body", diagnostics);
                break;
            case ForRangeStatement range:
                CheckExpression(range.Start, $"{path} start", diagnostics);
                CheckExpression(range.End, $"{path} end", diagnostics);
                CheckStatements(range.Body, $"{path} body", diagnostics);
                break;
        }
    }

    private static void CheckExpression(
        Expression? expression,
        string path,
        List<SandboxDiagnostic> diagnostics)
    {
        switch (expression)
        {
            case null:
                AddNull(diagnostics, path);
                break;
            case LiteralExpression literal:
                CheckValue(literal.Value, $"{path} literal", diagnostics);
                break;
            case UnaryExpression unary:
                CheckExpression(unary.Operand, $"{path} operand", diagnostics);
                break;
            case BinaryExpression binary:
                CheckExpression(binary.Left, $"{path} left operand", diagnostics);
                CheckExpression(binary.Right, $"{path} right operand", diagnostics);
                break;
            case CallExpression call:
                if (call.GenericType is not null)
                {
                    CheckType(call.GenericType, $"{path} generic type", diagnostics);
                }

                CheckExpressions(call.Arguments, $"call '{call.Name}' arguments", diagnostics);
                break;
        }
    }

    private static void CheckExpressions(
        IReadOnlyList<Expression> expressions,
        string path,
        List<SandboxDiagnostic> diagnostics)
    {
        for (var i = 0; i < expressions.Count; i++)
        {
            CheckExpression(expressions[i], $"{path} entry at index {i}", diagnostics);
        }
    }

    private static void CheckValue(
        SandboxValue? value,
        string path,
        List<SandboxDiagnostic> diagnostics)
    {
        switch (value)
        {
            case null:
                AddNull(diagnostics, path);
                break;
            case ListValue list:
                CheckType(list.ItemType, $"{path} item type", diagnostics);
                CheckValues(list.Values, $"{path} values", diagnostics);
                break;
            case MapValue map:
                CheckType(map.KeyType, $"{path} key type", diagnostics);
                CheckType(map.ValueType, $"{path} value type", diagnostics);
                foreach (var entry in map.Values)
                {
                    CheckValue(entry.Key, $"{path} map key", diagnostics);
                    CheckValue(entry.Value, $"{path} map value", diagnostics);
                }

                break;
            case RecordValue record:
                CheckValues(record.Fields, $"{path} fields", diagnostics);
                break;
        }
    }

    private static void CheckValues(
        IReadOnlyList<SandboxValue> values,
        string path,
        List<SandboxDiagnostic> diagnostics)
    {
        for (var i = 0; i < values.Count; i++)
        {
            CheckValue(values[i], $"{path} entry at index {i}", diagnostics);
        }
    }

    private static void CheckType(
        SandboxType? type,
        string path,
        List<SandboxDiagnostic> diagnostics)
    {
        if (type is null)
        {
            AddNull(diagnostics, path);
            return;
        }

        for (var i = 0; i < type.Arguments.Count; i++)
        {
            CheckType(type.Arguments[i], $"{path} argument at index {i}", diagnostics);
        }
    }

    private static void AddNull(List<SandboxDiagnostic> diagnostics, string path)
        => diagnostics.Add(new SandboxDiagnostic("E-IR-NULL", $"{path} must not be null"));
}
