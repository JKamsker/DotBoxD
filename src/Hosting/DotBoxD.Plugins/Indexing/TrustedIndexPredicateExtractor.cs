using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Indexing;

internal static class TrustedIndexPredicateExtractor
{
    public static IReadOnlyList<IndexedPredicate> Extract<TEvent>(
        PluginPackage package,
        IReadOnlyList<Parameter> eventParameters)
        => Extract(package, eventParameters, typeof(TEvent));

    public static IReadOnlyList<IndexedPredicate> Extract(
        PluginPackage package,
        IReadOnlyList<Parameter> eventParameters)
        => Extract(package, eventParameters, eventType: null);

    private static IReadOnlyList<IndexedPredicate> Extract(
        PluginPackage package,
        IReadOnlyList<Parameter> eventParameters,
        Type? eventType)
    {
        if (package.Module.Functions.FirstOrDefault(f =>
                string.Equals(f.Id, package.Entrypoints.ShouldHandle, StringComparison.Ordinal)) is not { } shouldHandle)
        {
            return [];
        }

        var eventPaths = EventParameterPaths(eventParameters);
        if (eventPaths.Count == 0)
        {
            return [];
        }

        var predicates = new List<IndexedPredicate>();
        CollectNecessaryPredicates(shouldHandle.Body, eventPaths, eventType, predicates);
        return predicates;
    }

    private static Dictionary<string, string> EventParameterPaths(IReadOnlyList<Parameter> eventParameters)
    {
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in eventParameters)
        {
            if (parameter.Name.StartsWith("e_", StringComparison.Ordinal) && parameter.Name.Length > 2)
            {
                paths[parameter.Name] = parameter.Name[2..];
            }
        }

        return paths;
    }

    private static void CollectNecessaryPredicates(
        Expression expression,
        IReadOnlyDictionary<string, string> eventPaths,
        Type? eventType,
        List<IndexedPredicate> predicates)
    {
        if (expression is BinaryExpression { Operator: "&&" } conjunction)
        {
            CollectNecessaryPredicates(conjunction.Left, eventPaths, eventType, predicates);
            CollectNecessaryPredicates(conjunction.Right, eventPaths, eventType, predicates);
            return;
        }

        if (TryPredicate(expression, eventPaths, eventType, out var predicate))
        {
            predicates.Add(predicate);
        }
    }

    private static void CollectNecessaryPredicates(
        IReadOnlyList<Statement> statements,
        IReadOnlyDictionary<string, string> eventPaths,
        Type? eventType,
        List<IndexedPredicate> predicates)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case AssignmentStatement assignment when !eventPaths.ContainsKey(assignment.Name):
                    continue;
                case ReturnStatement returned:
                    CollectNecessaryPredicates(returned.Value, eventPaths, eventType, predicates);
                    return;
                case IfStatement branch when AlwaysReturnsFalse(branch.Else):
                    CollectNecessaryPredicates(branch.Condition, eventPaths, eventType, predicates);
                    CollectNecessaryPredicates(branch.Then, eventPaths, eventType, predicates);
                    return;
                default:
                    return;
            }
        }
    }

    private static bool AlwaysReturnsFalse(IReadOnlyList<Statement> statements)
        => statements.Count == 1 &&
           statements[0] is ReturnStatement
           {
               Value: LiteralExpression { Value: BoolValue { Value: false } }
           };

    private static bool TryPredicate(
        Expression expression,
        IReadOnlyDictionary<string, string> eventPaths,
        Type? eventType,
        out IndexedPredicate predicate)
    {
        predicate = null!;
        if (expression is not BinaryExpression binary ||
            Operator(binary.Operator) is not { } op)
        {
            return TryCallPredicate(expression, eventPaths, eventType, out predicate);
        }

        if (TryPathLiteral(binary.Left, binary.Right, eventPaths, eventType, op, out predicate))
        {
            return true;
        }

        return TryPathLiteral(binary.Right, binary.Left, eventPaths, eventType, Flip(op), out predicate);
    }

    private static bool TryCallPredicate(
        Expression expression,
        IReadOnlyDictionary<string, string> eventPaths,
        Type? eventType,
        out IndexedPredicate predicate)
    {
        predicate = null!;
        if (expression is UnaryExpression { Operator: "!", Operand: { } operand })
        {
            return TryStringEquals(operand, eventPaths, eventType, IndexPredicateOperator.NotEquals, out predicate);
        }

        return TryStringEquals(expression, eventPaths, eventType, IndexPredicateOperator.Equals, out predicate);
    }

    private static bool TryStringEquals(
        Expression expression,
        IReadOnlyDictionary<string, string> eventPaths,
        Type? eventType,
        IndexPredicateOperator op,
        out IndexedPredicate predicate)
    {
        predicate = null!;
        if (expression is not CallExpression
            {
                Name: "string.equals",
                Arguments.Count: 2
            } call)
        {
            return false;
        }

        if (TryPathLiteral(call.Arguments[0], call.Arguments[1], eventPaths, eventType, op, out predicate))
        {
            return true;
        }

        return TryPathLiteral(call.Arguments[1], call.Arguments[0], eventPaths, eventType, op, out predicate);
    }

    private static bool TryPathLiteral(
        Expression pathExpression,
        Expression literal,
        IReadOnlyDictionary<string, string> eventPaths,
        Type? eventType,
        IndexPredicateOperator op,
        out IndexedPredicate predicate)
    {
        predicate = null!;
        if (!TryEventPath(pathExpression, eventPaths, eventType, out var path) ||
            literal is not LiteralExpression literalExpression ||
            !TryLiteralValue(literalExpression.Value, out var value, out var valueType))
        {
            return false;
        }

        predicate = new IndexedPredicate(path, op, value, valueType);
        return true;
    }

    private static bool TryEventPath(
        Expression expression,
        IReadOnlyDictionary<string, string> eventPaths,
        Type? eventType,
        out string path)
    {
        var indexes = new List<int>();
        var current = expression;
        while (current is CallExpression
            {
                Name: "record.get",
                Arguments.Count: 2
            } call)
        {
            if (!TryI32Literal(call.Arguments[1], out var index))
            {
                path = string.Empty;
                return false;
            }

            indexes.Add(index);
            current = call.Arguments[0];
        }

        if (current is not VariableExpression variable ||
            !eventPaths.TryGetValue(variable.Name, out var root))
        {
            path = string.Empty;
            return false;
        }

        if (indexes.Count == 0)
        {
            path = root;
            return true;
        }

        indexes.Reverse();
        path = string.Empty;
        return eventType is not null &&
               EventIndexPathResolver.TryResolveDottedPath(eventType, root, indexes, out path);
    }

    private static bool TryI32Literal(Expression expression, out int value)
    {
        if (expression is LiteralExpression { Value: I32Value i32 })
        {
            value = i32.Value;
            return true;
        }

        value = 0;
        return false;
    }

    private static IndexPredicateOperator? Operator(string op)
        => op switch
        {
            "==" => IndexPredicateOperator.Equals,
            "!=" => IndexPredicateOperator.NotEquals,
            ">" => IndexPredicateOperator.GreaterThan,
            ">=" => IndexPredicateOperator.GreaterThanOrEqual,
            "<" => IndexPredicateOperator.LessThan,
            "<=" => IndexPredicateOperator.LessThanOrEqual,
            _ => null
        };

    private static IndexPredicateOperator Flip(IndexPredicateOperator op)
        => op switch
        {
            IndexPredicateOperator.GreaterThan => IndexPredicateOperator.LessThan,
            IndexPredicateOperator.GreaterThanOrEqual => IndexPredicateOperator.LessThanOrEqual,
            IndexPredicateOperator.LessThan => IndexPredicateOperator.GreaterThan,
            IndexPredicateOperator.LessThanOrEqual => IndexPredicateOperator.GreaterThanOrEqual,
            _ => op
        };

    private static bool TryLiteralValue(SandboxValue literal, out object value, out string valueType)
    {
        switch (literal)
        {
            case BoolValue boolValue:
                value = boolValue.Value;
                valueType = "bool";
                return true;
            case I32Value i32Value:
                value = i32Value.Value;
                valueType = "int";
                return true;
            case I64Value i64Value:
                value = i64Value.Value;
                valueType = "long";
                return true;
            case F64Value f64Value:
                value = f64Value.Value;
                valueType = "double";
                return true;
            case StringValue stringValue:
                value = stringValue.Value;
                valueType = "string";
                return true;
            default:
                value = "";
                valueType = "";
                return false;
        }
    }
}
