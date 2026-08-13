using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainProjectionSlotResolver
{
    public static string? Final(IReadOnlyList<HookChainStage> stages)
    {
        for (var index = stages.Count - 1; index >= 0; index--)
        {
            if (stages[index].IsSelect)
            {
                return HookChainStageLowerer.SelectTemp(stages[index].Lambda);
            }
        }

        return null;
    }

    public static string? FinalSource(
        IReadOnlyList<HookChainStage> stages,
        EquatableArray<EventPropertyModel> eventProperties)
    {
        HookChainStage? selected = null;
        foreach (var stage in stages)
        {
            if (!stage.IsSelect)
            {
                continue;
            }

            if (selected is not null)
            {
                return null;
            }

            selected = stage;
        }

        if (selected?.Lambda.ExpressionBody is not MemberAccessExpressionSyntax member ||
            ElementParameter(selected.Value.Lambda) is not { } parameterName ||
            member.Expression is not IdentifierNameSyntax receiver ||
            !string.Equals(receiver.Identifier.ValueText, parameterName, StringComparison.Ordinal))
        {
            return null;
        }

        var propertyName = member.Name.Identifier.ValueText;
        return eventProperties.Any(property => string.Equals(property.Name, propertyName, StringComparison.Ordinal))
            ? DotBoxDExpressionModelFactory.EventVariable(propertyName)
            : null;
    }

    private static string? ElementParameter(LambdaExpressionSyntax lambda)
        => lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized =>
                parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
            _ => null
        };
}
