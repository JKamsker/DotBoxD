using DotBoxD.Shared.HostBindings;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDHostBindingExpressionLowerer
{
    private static IReadOnlyList<string> HostCapabilityEffects(
        TypedConstant effects,
        bool returnAllocates,
        IMethodSymbol method)
    {
        if (effects.Value is null)
        {
            throw new NotSupportedException(
                $"Host capability on '{method.ToDisplayString()}' must declare explicit effects.");
        }

        try
        {
            var declaredEffects = Convert.ToInt64(effects.Value, System.Globalization.CultureInfo.InvariantCulture);
            return HostBindingMetadataRules.EffectNames(
                    HostBindingMetadataRules.ValidateDeclaredEffects(
                        declaredEffects,
                        returnAllocates,
                        $"Host capability on '{method.ToDisplayString()}'"))
                .ToArray();
        }
        catch (InvalidOperationException ex)
        {
            throw new NotSupportedException(ex.Message, ex);
        }
    }
}
