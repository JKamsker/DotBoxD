using System.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerFacadeInstalledPackageGuardEmitter
{
    public static void Append(StringBuilder builder)
    {
        builder.AppendLine("    private static void RequireInstalledPackageId(global::DotBoxD.Plugins.PluginPackage package, string pluginId)");
        builder.AppendLine("    {");
        builder.AppendLine("        var manifestId = package.Manifest.PluginId;");
        builder.AppendLine("        if (global::System.StringComparer.Ordinal.Equals(pluginId, manifestId))");
        builder.AppendLine("        {");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine("        var callbackId = package.CallbackSubscriptionId;");
        builder.AppendLine("        if (callbackId is not null && global::System.StringComparer.Ordinal.Equals(pluginId, callbackId))");
        builder.AppendLine("        {");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine("        throw new global::System.InvalidOperationException($\"Installed package id '{pluginId}' did not match manifest id '{manifestId}'.\");");
        builder.AppendLine("    }");
        builder.AppendLine("    private void RequireInstalledKernel<TKernel>(string pluginId)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (!_installedPluginIds.Contains(pluginId))");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.InvalidOperationException($\"Kernel '{typeof(TKernel).FullName}' has not been installed.\");");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("    private static global::DotBoxD.Plugins.PluginPackage RequirePluginPackage(object package)");
        builder.AppendLine("        => package as global::DotBoxD.Plugins.PluginPackage ??");
        builder.AppendLine("            throw new global::System.InvalidOperationException(\"Generated InvokeAsync IR did not provide a plugin package.\");");
    }
}
