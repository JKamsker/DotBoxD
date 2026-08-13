using System.Text;

namespace DotBoxD.DebugAdapter;

internal static class DapBreakpointExpressionTranslator
{
    public static string TranslateCondition(
        string expression,
        IReadOnlyList<DapSourceVariableBinding> bindings)
        => DapSourceVariableProjector.Translate(expression, bindings);

    public static string TranslateLogMessage(
        string template,
        IReadOnlyList<DapSourceVariableBinding> bindings)
    {
        var translated = new StringBuilder(template.Length);
        var position = 0;
        while (position < template.Length)
        {
            var open = template.IndexOf('{', position);
            if (open < 0)
            {
                translated.Append(template, position, template.Length - position);
                break;
            }

            translated.Append(template, position, open - position + 1);
            var close = template.IndexOf('}', open + 1);
            if (close < 0)
            {
                translated.Append(template, open + 1, template.Length - open - 1);
                break;
            }

            var expression = template[(open + 1)..close];
            translated.Append(DapSourceVariableProjector.Translate(expression, bindings));
            translated.Append('}');
            position = close + 1;
        }

        return translated.ToString();
    }
}
