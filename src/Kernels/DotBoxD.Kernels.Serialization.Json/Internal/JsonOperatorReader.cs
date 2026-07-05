namespace DotBoxD.Kernels.Serialization.Json.Internal;

using System.Text.Json;
using DotBoxD.Kernels.Model;
using static JsonImport;

internal static class JsonOperatorReader
{
    private static readonly Dictionary<string, string> UnaryOperators = new(StringComparer.Ordinal)
    {
        ["not"] = "!",
        ["-"] = "-",
    };

    private static readonly Dictionary<string, string> BinaryOperators = new(StringComparer.Ordinal)
    {
        ["add"] = "+",
        ["sub"] = "-",
        ["mul"] = "*",
        ["div"] = "/",
        ["rem"] = "%",
        ["eq"] = "==",
        ["ne"] = "!=",
        ["lt"] = "<",
        ["lte"] = "<=",
        ["gt"] = ">",
        ["gte"] = ">=",
        ["and"] = "&&",
        ["or"] = "||",
    };

    public static string NormalizeUnary(string op)
        => NormalizeUnary(op, JsonSpan);

    public static string NormalizeUnary(string op, SourceSpan span)
        => UnaryOperators.TryGetValue(op, out var normalized)
            ? normalized
            : throw Error("E-JSON-OP", $"unknown unary op '{op}'", span);

    public static string NormalizeUnary(string op, JsonElement element, JsonSourceMap source)
        => UnaryOperators.TryGetValue(op, out var normalized)
            ? normalized
            : throw Error("E-JSON-OP", $"unknown unary op '{op}'", source.SpanFor(element));

    public static string NormalizeBinary(string op)
        => NormalizeBinary(op, JsonSpan);

    public static string NormalizeBinary(string op, SourceSpan span)
        => BinaryOperators.TryGetValue(op, out var normalized)
            ? normalized
            : throw Error("E-JSON-OP", $"unknown binary op '{op}'", span);

    public static string NormalizeBinary(string op, JsonElement element, JsonSourceMap source)
        => BinaryOperators.TryGetValue(op, out var normalized)
            ? normalized
            : throw Error("E-JSON-OP", $"unknown binary op '{op}'", source.SpanFor(element));
}
