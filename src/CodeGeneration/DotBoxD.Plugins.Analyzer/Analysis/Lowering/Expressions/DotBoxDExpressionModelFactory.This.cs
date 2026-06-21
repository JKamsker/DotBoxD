using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDExpressionModelFactory
{
    private static DotBoxDExpressionModel LowerThisMemberAccess(
        MemberAccessExpressionSyntax member,
        string memberName,
        DotBoxDExpressionLoweringContext context)
    {
        var liveSettings = context.LiveSettings;
        for (var i = 0; i < liveSettings.Count; i++)
        {
            var setting = liveSettings[i];
            if (string.Equals(setting.Name, memberName, StringComparison.Ordinal))
            {
                return new DotBoxDExpressionModel(
                    $"{DotBoxDGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(memberName)})",
                    setting.Type,
                    false);
            }
        }

        return Unsupported(member);
    }
}
