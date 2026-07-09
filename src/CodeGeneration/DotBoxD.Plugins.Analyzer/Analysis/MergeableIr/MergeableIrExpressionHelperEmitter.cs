using System.Text;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.MergeableIr;

internal static class MergeableIrExpressionHelperEmitter
{
    public static void Emit(StringBuilder builder)
    {
        EmitHelper(
            builder,
            DotBoxDGenerationNames.Helpers.Var,
            Parameter(DotBoxDGenerationNames.CSharpTypes.String, "name"),
            $"new {TypeNames.GlobalVariableExpression}(name, Span)");
        EmitHelper(
            builder,
            DotBoxDGenerationNames.Helpers.Str,
            Parameter(DotBoxDGenerationNames.CSharpTypes.String, "value"),
            $"new {TypeNames.GlobalLiteralExpression}({TypeNames.GlobalSandboxValue}.FromString(value), Span)");
        EmitBindingCallHelper(
            builder,
            DotBoxDGenerationNames.Helpers.Int32ToStr,
            DotBoxDGenerationNames.BindingIds.Int32ToStringInvariant,
            $"{TypeNames.GlobalExpression} value",
            "value");
        EmitBindingCallHelper(
            builder,
            DotBoxDGenerationNames.Helpers.StringLength,
            DotBoxDGenerationNames.BindingIds.StringLength,
            $"{TypeNames.GlobalExpression} value",
            "value");
        EmitBindingCallHelper(
            builder,
            DotBoxDGenerationNames.Helpers.StringSubstring,
            DotBoxDGenerationNames.BindingIds.StringSubstringBudgeted,
            $"{TypeNames.GlobalExpression} value, {TypeNames.GlobalExpression} startIndex, {TypeNames.GlobalExpression} length",
            "value, startIndex, length");
        EmitBindingCallHelper(
            builder,
            DotBoxDGenerationNames.Helpers.ConcatString,
            DotBoxDGenerationNames.BindingIds.StringConcatBudgeted,
            $"{TypeNames.GlobalExpression} left, {TypeNames.GlobalExpression} right",
            "left, right");
        EmitBindingCallHelper(
            builder,
            DotBoxDGenerationNames.Helpers.StringEquals,
            DotBoxDGenerationNames.BindingIds.StringEquals,
            $"{TypeNames.GlobalExpression} left, {TypeNames.GlobalExpression} right",
            "left, right");
        EmitHelper(
            builder,
            DotBoxDGenerationNames.Helpers.I32,
            Parameter(DotBoxDGenerationNames.CSharpTypes.Int, "value"),
            $"new {TypeNames.GlobalLiteralExpression}({TypeNames.GlobalSandboxValue}.FromInt32(value), Span)");
        EmitHelper(
            builder,
            DotBoxDGenerationNames.Helpers.I64,
            Parameter(DotBoxDGenerationNames.CSharpTypes.Long, "value"),
            $"new {TypeNames.GlobalLiteralExpression}({TypeNames.GlobalSandboxValue}.FromInt64(value), Span)");
        EmitHelper(
            builder,
            DotBoxDGenerationNames.Helpers.F64,
            Parameter(DotBoxDGenerationNames.CSharpTypes.Double, "value"),
            $"new {TypeNames.GlobalLiteralExpression}({TypeNames.GlobalSandboxValue}.FromDouble(value), Span)");
        EmitHelper(
            builder,
            DotBoxDGenerationNames.Helpers.Bool,
            Parameter(DotBoxDGenerationNames.CSharpTypes.Bool, "value"),
            $"new {TypeNames.GlobalLiteralExpression}({TypeNames.GlobalSandboxValue}.FromBool(value), Span)");
        EmitUnaryHelper(builder, DotBoxDGenerationNames.Helpers.Not, DotBoxDOperatorNames.LogicalNot);
        EmitUnaryHelper(builder, DotBoxDGenerationNames.Helpers.Neg, DotBoxDOperatorNames.Minus);
        EmitBinaryHelper(builder, DotBoxDGenerationNames.Helpers.Eq, DotBoxDOperatorNames.EqualTo);
        EmitBinaryHelper(builder, DotBoxDGenerationNames.Helpers.Ne, DotBoxDOperatorNames.NotEqualTo);
        EmitBinaryHelper(builder, DotBoxDGenerationNames.Helpers.Ge, DotBoxDOperatorNames.GreaterThanOrEqual);
        EmitBinaryHelper(builder, DotBoxDGenerationNames.Helpers.Gt, DotBoxDOperatorNames.GreaterThan);
        EmitBinaryHelper(builder, DotBoxDGenerationNames.Helpers.Le, DotBoxDOperatorNames.LessThanOrEqual);
        EmitBinaryHelper(builder, DotBoxDGenerationNames.Helpers.Lt, DotBoxDOperatorNames.LessThan);
        EmitBinaryHelper(builder, DotBoxDGenerationNames.Helpers.And, DotBoxDOperatorNames.LogicalAnd);
        EmitBinaryHelper(builder, DotBoxDGenerationNames.Helpers.Or, DotBoxDOperatorNames.LogicalOr);
        EmitBinaryHelper(builder, DotBoxDGenerationNames.Helpers.Add, DotBoxDOperatorNames.Add);
        EmitBinaryHelper(builder, DotBoxDGenerationNames.Helpers.Sub, DotBoxDOperatorNames.Minus);
        EmitBinaryHelper(builder, DotBoxDGenerationNames.Helpers.Mul, DotBoxDOperatorNames.Multiply);
        EmitBinaryHelper(builder, DotBoxDGenerationNames.Helpers.Div, DotBoxDOperatorNames.Divide);
        EmitBinaryHelper(builder, DotBoxDGenerationNames.Helpers.Mod, DotBoxDOperatorNames.Modulo);
    }

    private static void EmitHelper(StringBuilder builder, string name, string parameters, string expression)
        => builder.Append("    private static ").Append(TypeNames.GlobalExpression).Append(' ')
            .Append(name)
            .Append('(')
            .Append(parameters)
            .Append(") => ")
            .Append(expression)
            .AppendLine(";");

    private static void EmitBindingCallHelper(
        StringBuilder builder,
        string helper,
        string bindingId,
        string parameters,
        string arguments)
        => EmitHelper(
            builder,
            helper,
            parameters,
            "new " + TypeNames.GlobalCallExpression + "(" +
            LiteralReader.StringLiteral(bindingId) +
            ", [" + arguments + "], null, Span)");

    private static void EmitUnaryHelper(StringBuilder builder, string name, string op)
        => EmitHelper(
            builder,
            name,
            $"{TypeNames.GlobalExpression} operand",
            $"new {TypeNames.GlobalUnaryExpression}({LiteralReader.StringLiteral(op)}, operand, Span)");

    private static void EmitBinaryHelper(StringBuilder builder, string name, string op)
        => EmitHelper(
            builder,
            name,
            $"{TypeNames.GlobalExpression} left, {TypeNames.GlobalExpression} right",
            $"new {TypeNames.GlobalBinaryExpression}(left, {LiteralReader.StringLiteral(op)}, right, Span)");

    private static string Parameter(string type, string name) => type + " " + name;
}
