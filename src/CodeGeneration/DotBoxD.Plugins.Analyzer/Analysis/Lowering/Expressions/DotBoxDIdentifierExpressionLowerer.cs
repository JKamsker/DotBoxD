using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class DotBoxDIdentifierExpressionLowerer
{
    public static DotBoxDExpressionModel Lower(
        IdentifierNameSyntax identifier,
        DotBoxDExpressionLoweringContext context)
    {
        var name = identifier.Identifier.ValueText;
        if (TryLowerKnownIdentifier(name, context) is { } known)
        {
            return known;
        }

        if (DotBoxDCapturedConstantLocal.TryLower(identifier, context) is { } captured)
        {
            return captured;
        }

        throw new NotSupportedException($"Unsupported plugin identifier '{name}'.");
    }

    public static DotBoxDExpressionModel Lower(
        string name,
        DotBoxDExpressionLoweringContext context)
        => TryLowerKnownIdentifier(name, context) ??
           throw new NotSupportedException($"Unsupported plugin identifier '{name}'.");

    private static DotBoxDExpressionModel? TryLowerKnownIdentifier(
        string name,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.InlinedBindings is { } bindings &&
            bindings.TryGetValue(name, out var bound))
        {
            return bound;
        }

        if (DotBoxDPatternCaptureExpressionLowerer.TryLowerIdentifier(name, context, out var capture))
        {
            return capture;
        }

        if (context.ProjectedElementName is { } projectedName &&
            string.Equals(projectedName, name, StringComparison.Ordinal))
        {
            return context.ProjectedElement!;
        }

        var liveSettings = context.LiveSettings;
        for (var i = 0; i < liveSettings.Count; i++)
        {
            var setting = liveSettings[i];
            if (string.Equals(setting.Name, name, StringComparison.Ordinal))
            {
                return new DotBoxDExpressionModel(
                    $"{DotBoxDGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(name)})",
                    setting.Type,
                    false);
            }
        }

        return null;
    }
}
