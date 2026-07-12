using System.Text;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerReturnWrapperCancellationEmitter
{
    public static bool RequiresAsyncReturnWrapperBlock(PluginServerForwardedMethod method)
        => method.ReturnWrapperName is not null &&
           method.ReturnWrapperKind is PluginServerReturnWrapperKind.Task or PluginServerReturnWrapperKind.ValueTask &&
           method.Parameters.Any(IsCancellationToken);

    public static void AppendCancellationChecks(
        StringBuilder builder,
        PluginServerForwardedMethod method,
        string indent)
    {
        foreach (var parameter in method.Parameters.Where(IsCancellationToken))
        {
            builder.Append(indent).Append('@').Append(parameter.Name).AppendLine(".ThrowIfCancellationRequested();");
        }
    }

    public static string UniqueLocalName(string preferred, PluginServerForwardedMethod method)
    {
        var used = new HashSet<string>(method.Parameters.Select(static p => p.Name), StringComparer.Ordinal);
        if (used.Add(preferred))
        {
            return preferred;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = preferred + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool IsCancellationToken(PluginServerParameter parameter)
        => string.Equals(
            parameter.Type,
            DotBoxDGenerationNames.TypeNames.GlobalCancellationToken,
            StringComparison.Ordinal);
}
