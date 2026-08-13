using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

internal static class FunctionFrameTypeResolver
{
    private static readonly HashSet<string> BoolBinaryOperators = new(StringComparer.Ordinal)
    {
        "&&", "||", "==", "!=", "<", "<=", ">", ">="
    };

    public static SandboxType?[] Resolve(
        SandboxFunction function,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, int> slots)
    {
        var candidates = new Dictionary<string, SandboxType?>(StringComparer.Ordinal);
        foreach (var parameter in function.Parameters)
        {
            Observe(candidates, parameter.Name, parameter.Type);
        }

        Scan(function.Body, function, functionAnalysis, bindings, candidates);
        var result = new SandboxType?[slots.Count];
        foreach (var pair in slots)
        {
            candidates.TryGetValue(pair.Key, out result[pair.Value]);
        }

        return result;
    }

    private static void Scan(
        IReadOnlyList<Statement> statements,
        SandboxFunction function,
        IReadOnlyDictionary<string, FunctionAnalysis> analyses,
        IBindingCatalog bindings,
        Dictionary<string, SandboxType?> candidates)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case AssignmentStatement assignment:
                    Observe(candidates, assignment.Name, Infer(assignment.Value, function, analyses, bindings, candidates));
                    break;
                case IfStatement branch:
                    Scan(branch.Then, function, analyses, bindings, candidates);
                    Scan(branch.Else, function, analyses, bindings, candidates);
                    break;
                case ForRangeStatement range:
                    Observe(candidates, range.LocalName, SandboxType.I32);
                    Scan(range.Body, function, analyses, bindings, candidates);
                    break;
                case WhileStatement loop:
                    Scan(loop.Body, function, analyses, bindings, candidates);
                    break;
            }
        }
    }

    private static void Observe(Dictionary<string, SandboxType?> candidates, string name, SandboxType? type)
    {
        if (!candidates.TryGetValue(name, out var existing))
        {
            candidates[name] = type;
            return;
        }

        candidates[name] = existing?.Equals(type) == true ? existing : null;
    }

    private static SandboxType? Infer(
        Expression expression,
        SandboxFunction function,
        IReadOnlyDictionary<string, FunctionAnalysis> analyses,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, SandboxType?> candidates)
        => expression switch
        {
            LiteralExpression literal => literal.Value.Type,
            VariableExpression variable => VariableType(variable.Name, function, candidates),
            UnaryExpression { Operator: "!" } => SandboxType.Bool,
            UnaryExpression unary => Infer(unary.Operand, function, analyses, bindings, candidates),
            BinaryExpression binary when BoolBinaryOperators.Contains(binary.Operator) => SandboxType.Bool,
            BinaryExpression binary => Infer(binary.Left, function, analyses, bindings, candidates),
            CallExpression call => CallType(call, analyses, bindings),
            _ => null
        };

    private static SandboxType? VariableType(
        string name,
        SandboxFunction function,
        IReadOnlyDictionary<string, SandboxType?> candidates)
    {
        if (candidates.TryGetValue(name, out var type) && type is not null)
        {
            return type;
        }

        foreach (var parameter in function.Parameters)
        {
            if (string.Equals(parameter.Name, name, StringComparison.Ordinal))
            {
                return parameter.Type;
            }
        }

        return null;
    }

    private static SandboxType? CallType(
        CallExpression call,
        IReadOnlyDictionary<string, FunctionAnalysis> analyses,
        IBindingCatalog bindings)
        => call.Name switch
        {
            "list.empty" => SandboxType.List(call.GenericType ?? SandboxType.Unit),
            "list.count" => SandboxType.I32,
            "map.empty" => call.GenericType,
            "numeric.toI64" => SandboxType.I64,
            "numeric.toF64" => SandboxType.F64,
            _ => analyses.TryGetValue(call.Name, out var analysis)
                ? analysis.ReturnType
                : bindings.TryGet(call.Name, out var binding) ? binding.ReturnType : null
        };
}
