using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Serialization.Json;

internal static class JsonExportNames
{
    private static readonly Dictionary<string, string> UnaryOperators = new(StringComparer.Ordinal)
    {
        ["!"] = "not",
        ["-"] = "-",
    };

    private static readonly Dictionary<string, string> BinaryOperators = new(StringComparer.Ordinal)
    {
        ["+"] = "add",
        ["-"] = "sub",
        ["*"] = "mul",
        ["/"] = "div",
        ["%"] = "rem",
        ["=="] = "eq",
        ["!="] = "ne",
        ["<"] = "lt",
        ["<="] = "lte",
        [">"] = "gt",
        [">="] = "gte",
        ["&&"] = "and",
        ["||"] = "or",
    };

    public static string UnaryOperator(string op)
        => UnaryOperators.TryGetValue(op, out var exported)
            ? exported
            : throw Error("E-JSON-EXPORT", $"unary operator '{op}' cannot be exported");

    public static string BinaryOperator(string op)
        => BinaryOperators.TryGetValue(op, out var exported)
            ? exported
            : throw Error("E-JSON-EXPORT", $"binary operator '{op}' cannot be exported");

    public static SandboxValidationException Error(string code, string message)
        => new([new SandboxDiagnostic(code, message, Span: JsonImport.JsonSpan)]);
}
