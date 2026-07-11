using System.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerBuilderEmitter
{
    public static void Append(StringBuilder builder, PluginServerFacadeModel model)
    {
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            string.Empty,
            "Builder for the generated plugin server. Use Setup to record installs without I/O, then Build to create the runtime facade.");
        builder.Append(model.Accessibility).Append(" sealed class ").Append(model.ClassName).AppendLine("Builder");
        builder.AppendLine("{");
        AppendFieldsAndConstructors(builder, model);
        if (model.EmitPipeBuilder)
        {
            AppendPipeFactories(builder, model);
        }

        AppendConnectionFactory(builder, model);
        AppendSetupAndBuild(builder, model);
        builder.AppendLine("}");
    }

    private static void AppendFieldsAndConstructors(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.AppendLine("    private readonly global::System.Func<global::System.Action<global::DotBoxD.Services.Peer.RpcPeer>?, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask<global::DotBoxD.Services.Peer.RpcPeerSession>>? _connectionFactory;");
        if (model.EmitPipeBuilder)
        {
            builder.AppendLine("    private readonly global::DotBoxD.Pushdown.Services.PluginDebugBridge? _debugBridge;");
        }

        builder.Append("    private readonly ").Append(model.ControlServiceType).AppendLine("? _control;");
        builder.Append("    private readonly ").Append(model.WorldType).AppendLine("? _world;");
        builder.Append("    private global::System.Action<").Append(model.SetupInterfaceName).AppendLine(">? _setup;");
        builder.AppendLine("    private " + model.ClassName + "Builder(global::System.Func<global::System.Action<global::DotBoxD.Services.Peer.RpcPeer>?, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask<global::DotBoxD.Services.Peer.RpcPeerSession>> connectionFactory) => _connectionFactory = connectionFactory;");
        if (model.EmitPipeBuilder)
        {
            builder.AppendLine("    private " + model.ClassName + "Builder(global::System.Func<global::System.Action<global::DotBoxD.Services.Peer.RpcPeer>?, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask<global::DotBoxD.Services.Peer.RpcPeerSession>> connectionFactory, global::DotBoxD.Pushdown.Services.PluginDebugBridge debugBridge) { _connectionFactory = connectionFactory; _debugBridge = debugBridge; }");
        }

        builder.AppendLine("    private " + model.ClassName + "Builder(" + model.ControlServiceType + " control, " + model.WorldType + "? world) { _control = control; _world = world; }");
    }

    private static void AppendPipeFactories(StringBuilder builder, PluginServerFacadeModel model)
    {
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Creates a builder that connects to a running game server by named pipe when StartAsync is called.");
        builder.AppendLine("    public static " + model.ClassName + "Builder FromPipeName(string pipeName)");
        builder.AppendLine("        => FromPipeName(pipeName, options: null);");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Creates a builder that connects with explicit peer options, such as an infinite timeout for a long-lived host session.");
        builder.AppendLine("    public static " + model.ClassName + "Builder FromPipeName(string pipeName, global::DotBoxD.Services.Peer.RpcPeerOptions? options)");
        builder.AppendLine("        => FromPipeName(pipeName, global::DotBoxD.Pushdown.Services.NamedPipeTransportOptions.Default, options);");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Creates a builder that connects with explicit named-pipe transport and peer options.");
        builder.AppendLine("    public static " + model.ClassName + "Builder FromPipeName(string pipeName, global::DotBoxD.Pushdown.Services.NamedPipeTransportOptions transportOptions, global::DotBoxD.Services.Peer.RpcPeerOptions? options = null)");
        builder.AppendLine("        => new((configurePeer, ct) => new global::System.Threading.Tasks.ValueTask<global::DotBoxD.Services.Peer.RpcPeerSession>(global::DotBoxD.Pushdown.Services.RpcMessagePackIpc.ConnectNamedPipeAsync(pipeName, configurePeer, namedPipeOptions: transportOptions, options: options, cancellationToken: ct)));");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Creates a pipe builder that exposes source maps through an explicitly started local debug bridge before setup packages are installed.");
        builder.AppendLine("    public static " + model.ClassName + "Builder FromPipeNameWithKernelDebugging(string pipeName, global::DotBoxD.Pushdown.Services.PluginDebugBridge debugBridge)");
        builder.AppendLine("        => FromPipeNameWithKernelDebugging(pipeName, debugBridge, options: null);");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Creates a debugging pipe builder with explicit peer options, such as an infinite timeout for a long-lived host session.");
        builder.AppendLine("    public static " + model.ClassName + "Builder FromPipeNameWithKernelDebugging(string pipeName, global::DotBoxD.Pushdown.Services.PluginDebugBridge debugBridge, global::DotBoxD.Services.Peer.RpcPeerOptions? options)");
        builder.AppendLine("        => FromPipeNameWithKernelDebugging(pipeName, debugBridge, global::DotBoxD.Pushdown.Services.NamedPipeTransportOptions.Default, options);");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Creates a debugging pipe builder with explicit named-pipe transport and peer options.");
        builder.AppendLine("    public static " + model.ClassName + "Builder FromPipeNameWithKernelDebugging(string pipeName, global::DotBoxD.Pushdown.Services.PluginDebugBridge debugBridge, global::DotBoxD.Pushdown.Services.NamedPipeTransportOptions transportOptions, global::DotBoxD.Services.Peer.RpcPeerOptions? options = null)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(debugBridge);");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(transportOptions);");
        builder.AppendLine("        return new((configurePeer, ct) => new global::System.Threading.Tasks.ValueTask<global::DotBoxD.Services.Peer.RpcPeerSession>(global::DotBoxD.Pushdown.Services.RpcMessagePackIpc.ConnectNamedPipeAsync(pipeName, peer => { global::DotBoxD.Pushdown.Services.PluginDebugRpcPeerExtensions.ProvidePluginDebugEvents(peer, debugBridge); configurePeer?.Invoke(peer); }, namedPipeOptions: transportOptions, options: options, cancellationToken: ct)), debugBridge);");
        builder.AppendLine("    }");
    }

    private static void AppendConnectionFactory(StringBuilder builder, PluginServerFacadeModel model)
    {
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Creates a builder over an already connected control-plane service and optional world proxy.");
        builder.AppendLine("    public static " + model.ClassName + "Builder FromConnection(" + model.ControlServiceType + " control, " + model.WorldType + "? world = null)");
        builder.AppendLine("        => new(control, world);");
    }

    private static void AppendSetupAndBuild(StringBuilder builder, PluginServerFacadeModel model)
    {
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Records setup actions such as hooks, fire-and-forget subscriptions, replacements, and server extensions. Build remains synchronous; StartAsync replays the recorded installs.");
        builder.AppendLine("    public " + model.ClassName + "Builder Setup(global::System.Action<" + model.SetupInterfaceName + "> configure)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(configure);");
        builder.AppendLine("        _setup += configure;");
        builder.AppendLine("        return this;");
        builder.AppendLine("    }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Builds the generated plugin server facade. For pipe-based builders, call StartAsync before using runtime APIs.");
        builder.AppendLine("    public " + model.ServerInterfaceName + " Build()");
        builder.AppendLine("        => _connectionFactory is not null ? new " + PluginServerIdentifier.Escape(model.ClassName) + "(_connectionFactory, _setup" + (model.EmitPipeBuilder ? ", _debugBridge" : string.Empty) + ") : new " + PluginServerIdentifier.Escape(model.ClassName) + "(_control!, _world, _setup);");
    }
}
