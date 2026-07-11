using System.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerFacadeSurfaceEmitter
{
    public static void AppendInstallSurface(StringBuilder builder, PluginServerFacadeModel model)
        => PluginServerFacadeInstallSurfaceEmitter.Append(builder, model);

    public static void AppendProperties(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.AppendLine();
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Returns this generated server as its complete service facade.");
        builder.Append("    public ").Append(model.ServerInterfaceName).AppendLine(" Services => RequireFacade();");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Registry for server extension clients installed through setup, Extend, or EnsureAnonymousKernelAsync.");
        builder.AppendLine("    public global::DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions => RequireFacade();");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Remote hook registration surface. Hooks plug plugin logic into server decisions and are awaited by the server when matching events are published.");
        builder.Append("    public ").Append(model.HookRegistryName).AppendLine(" Hooks => RequireStarted(_hooks);");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Remote fire-and-forget subscription registration surface. Subscriptions are notifications: the server calls matching handlers when an event is published but does not wait for them.");
        builder.Append("    public ").Append(model.SubscriptionRegistryName).AppendLine(" Subscriptions => RequireStarted(_subscriptions);");
        foreach (var control in model.Controls)
        {
            PluginServerXmlDocumentation.Append(builder, "    ", control.Documentation);
            PluginServerFlowAttributeSource.Append(builder, "    ", control.Attributes);
            builder.Append("    public ").Append(control.Type).Append(' ')
                .Append(PluginServerIdentifier.Escape(control.Name))
                .Append(" => RequireStarted(").Append(control.FieldName).AppendLine(");");
        }

        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Wire client used by generated server extension clients to invoke installed server-side extension kernels.");
        builder.AppendLine("    public global::DotBoxD.Abstractions.IServerExtensionWireClient WireClient => RequireFacade();");
    }

    public static void AppendServerInterface(StringBuilder builder, PluginServerFacadeModel model)
    {
        PluginServerXmlDocumentation.Append(builder, string.Empty, model.WorldDocumentation);
        PluginServerClsComplianceAttributeSource.AppendFalse(builder, model);
        builder.Append(model.Accessibility).Append(" interface ").Append(model.ServerInterfaceName)
            .Append(" : ").Append(model.WorldType)
            .Append(", global::DotBoxD.Abstractions.IPluginServer<").Append(model.WorldType)
            .Append(">, global::DotBoxD.Abstractions.IServerExtensionClientRegistry, ")
            .AppendLine("global::System.IDisposable, global::System.IAsyncDisposable");
        builder.AppendLine("{");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Returns this generated server as its complete service facade.");
        builder.Append("    ").Append(model.ServerInterfaceName).AppendLine(" Services { get; }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Registry for server extension clients installed through setup, Extend, or EnsureAnonymousKernelAsync.");
        builder.AppendLine("    global::DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Remote hook registration surface. Hooks plug plugin logic into server decisions and are awaited by the server when matching events are published.");
        builder.Append("    ").Append(model.HookRegistryName).AppendLine(" Hooks { get; }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Remote fire-and-forget subscription registration surface. Subscriptions are notifications: the server calls matching handlers when an event is published but does not wait for them.");
        builder.Append("    ").Append(model.SubscriptionRegistryName).AppendLine(" Subscriptions { get; }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Wire client used by generated server extension clients to invoke installed server-side extension kernels.");
        builder.AppendLine("    global::DotBoxD.Abstractions.IServerExtensionWireClient WireClient { get; }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Creates a live-settings handle for an installed kernel so the plugin can batch strongly typed setting updates.");
        builder.AppendLine("    global::DotBoxD.Abstractions.ILiveSettingsHandle<TKernel> Get<TKernel>() where TKernel : class, new();");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Installs the package produced by the factory once, evicts failed attempts, and returns the installed plugin id.");
        builder.AppendLine("    global::System.Threading.Tasks.Task<string> EnsureAnonymousKernelAsync(string pluginId, global::System.Func<global::DotBoxD.Plugins.PluginPackage> factory, global::System.Threading.CancellationToken cancellationToken = default);");
        builder.AppendLine("}");
    }
}
