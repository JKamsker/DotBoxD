using System.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerWireInvocationEmitter
{
    public static void Append(StringBuilder builder, string resultExpression)
    {
        builder.AppendLine("        byte[]? __response;");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            __response = await Services.WireClient.InvokeServerExtensionAsync(__pluginId, __request, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("        }");
        builder.AppendLine("        catch (global::System.Exception) when (cancellationToken.IsCancellationRequested)");
        builder.AppendLine("        {");
        builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("            throw;");
        builder.AppendLine("        }");
        builder.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
        builder.Append("        return ").Append(resultExpression).AppendLine(";");
    }
}
