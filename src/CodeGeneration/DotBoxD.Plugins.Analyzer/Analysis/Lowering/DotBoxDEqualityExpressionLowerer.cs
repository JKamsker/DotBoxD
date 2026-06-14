namespace DotBoxD.Plugins.Analyzer;

internal static class DotBoxDEqualityExpressionLowerer
{
    public static DotBoxDExpressionModel Lower(
        DotBoxDExpressionModel left,
        DotBoxDExpressionModel right,
        bool negate,
        bool allocates)
    {
        var symbol = negate
            ? DotBoxDGenerationNames.Operators.NotEqualTo
            : DotBoxDGenerationNames.Operators.EqualTo;
        if (!string.Equals(left.Type, right.Type, StringComparison.Ordinal)) {
            throw new NotSupportedException(
                $"Operator '{symbol}' requires operands with the same supported type.");
        }

        if (!DotBoxDExpressionModelFactory.IsString(left)) {
            var helper = negate
                ? DotBoxDGenerationNames.Helpers.Ne
                : DotBoxDGenerationNames.Helpers.Eq;
            return Bool($"{helper}({left.Source}, {right.Source})", allocates);
        }

        var source = $"{DotBoxDGenerationNames.Helpers.StringEquals}({left.Source}, {right.Source})";
        return Bool(negate ? $"{DotBoxDGenerationNames.Helpers.Not}({source})" : source, allocates);
    }

    private static DotBoxDExpressionModel Bool(string source, bool allocates)
        => new(source, DotBoxDGenerationNames.ManifestTypes.Bool, allocates);
}
