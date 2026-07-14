using System.Text;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerReturnWrapperCancellationEmitter
{
    private const string GlobalTask = "global::System.Threading.Tasks.Task";
    private const string GlobalValueTask = "global::System.Threading.Tasks.ValueTask";

    public static bool RequiresAsyncModifier(PluginServerForwardedMethod method)
        => method.ReturnWrapperKind is PluginServerReturnWrapperKind.Task or PluginServerReturnWrapperKind.ValueTask ||
           RequiresAsyncCancellationBlock(method);

    public static bool RequiresAsyncCancellationBlock(PluginServerForwardedMethod method)
        => IsTaskLike(method) && method.Parameters.Any(IsCancellationToken);

    public static bool HasAwaitedResult(PluginServerForwardedMethod method)
        => method.ReturnWrapperName is not null || IsGenericTaskLike(method.ReturnType);

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

    private static bool IsTaskLike(PluginServerForwardedMethod method)
        => method.ReturnWrapperKind is PluginServerReturnWrapperKind.Task or PluginServerReturnWrapperKind.ValueTask ||
           string.Equals(method.ReturnType, GlobalTask, StringComparison.Ordinal) ||
           string.Equals(method.ReturnType, GlobalValueTask, StringComparison.Ordinal) ||
           IsGenericTaskLike(method.ReturnType);

    private static bool IsGenericTaskLike(string type)
        => type.StartsWith(GlobalTask + "<", StringComparison.Ordinal) ||
           type.StartsWith(GlobalValueTask + "<", StringComparison.Ordinal);
}
