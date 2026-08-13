using System.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerSetupEmitter
{
    public static void AppendSetupMembers(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.AppendLine("    private enum RecordedInstallKind");
        builder.AppendLine("    {");
        builder.AppendLine("        Plugin,");
        builder.AppendLine("        Subscription,");
        builder.AppendLine("        ServerExtension");
        builder.AppendLine("    }");
        builder.AppendLine();
        AppendRecordedInstall(builder);
        AppendRecordSetup(builder, model);
        AppendReplaySetup(builder);
        AppendSetupRecorder(builder, model);
        foreach (var control in model.Controls)
        {
            AppendControlAccumulator(builder, control);
        }
    }

    public static void AppendSetupInterfaces(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.AppendLine();
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            string.Empty,
            "Setup-time registration surface. Actions recorded here are replayed when the generated plugin server starts.");
        PluginServerClsComplianceAttributeSource.AppendFalse(builder, model);
        builder.Append(model.Accessibility).Append(" interface ").Append(model.SetupInterfaceName).AppendLine();
        builder.AppendLine("{");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Records a hook kernel package for replay at StartAsync. Hooks are awaited decision logic.");
        builder.Append("    ").Append(model.SetupInterfaceName).AppendLine(" Replace<TService, TKernel>() where TService : class where TKernel : class, TService;");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Setup-time hook registration surface. Recorded hooks plug plugin logic into server decisions and are awaited by the server.");
        builder.Append("    ").Append(model.HookRegistryName).AppendLine(" Hooks { get; }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Setup-time fire-and-forget subscription registration surface. Recorded subscriptions are notifications and the server does not wait for them.");
        builder.Append("    ").Append(model.SubscriptionRegistryName).AppendLine(" Subscriptions { get; }");
        foreach (var control in model.Controls)
        {
            PluginServerXmlDocumentation.Append(builder, "    ", control.Documentation);
            builder.Append("    ").Append(control.AccumulatorInterfaceName).Append(' ')
                .Append(PluginServerIdentifier.Escape(control.Name)).AppendLine(" { get; }");
        }

        builder.AppendLine("}");
        foreach (var control in model.Controls)
        {
            builder.AppendLine();
            PluginServerXmlDocumentation.AppendSummary(
                builder,
                string.Empty,
                "Setup-time server-extension accumulator for the " + control.Name + " domain control.");
            PluginServerClsComplianceAttributeSource.AppendFalse(builder, model);
            builder.Append(model.Accessibility).Append(" interface ").Append(control.AccumulatorInterfaceName).AppendLine();
            builder.AppendLine("{");
            PluginServerXmlDocumentation.AppendSummary(
                builder,
                "    ",
                "Records a server-extension kernel for replay at StartAsync.");
            builder.Append("    ").Append(control.AccumulatorInterfaceName).AppendLine(" Extend<TKernel>() where TKernel : class;");
            PluginServerXmlDocumentation.AppendSummary(
                builder,
                "    ",
                "Records a server-extension kernel for the requested service contract and replays it at StartAsync.");
            builder.Append("    ").Append(control.AccumulatorInterfaceName).AppendLine(" Extend<TService, TKernel>() where TService : class where TKernel : class;");
            builder.AppendLine("}");
        }
    }

    private static void AppendRecordedInstall(StringBuilder builder)
    {
        builder.AppendLine("    private readonly struct RecordedInstall");
        builder.AppendLine("    {");
        builder.AppendLine("        private RecordedInstall(RecordedInstallKind kind, global::DotBoxD.Plugins.PluginPackage package, global::System.Type? registryKey, global::System.Type? secondaryRegistryKey)");
        builder.AppendLine("        {");
        builder.AppendLine("            Kind = kind;");
        builder.AppendLine("            Package = package;");
        builder.AppendLine("            RegistryKey = registryKey;");
        builder.AppendLine("            SecondaryRegistryKey = secondaryRegistryKey;");
        builder.AppendLine("        }");
        builder.AppendLine("        public RecordedInstallKind Kind { get; }");
        builder.AppendLine("        public global::DotBoxD.Plugins.PluginPackage Package { get; }");
        builder.AppendLine("        public global::System.Type? RegistryKey { get; }");
        builder.AppendLine("        // A server extension is also registered under this second key when present, so an extension recorded");
        builder.AppendLine("        // by its service contract (Extend<TService, TKernel>) still resolves by the kernel type the generated");
        builder.AppendLine("        // graft client looks up — and vice versa — instead of throwing \"not registered\".");
        builder.AppendLine("        public global::System.Type? SecondaryRegistryKey { get; }");
        builder.AppendLine("        public static RecordedInstall Plugin(global::DotBoxD.Plugins.PluginPackage package) => new(RecordedInstallKind.Plugin, package, null, null);");
        builder.AppendLine("        public static RecordedInstall Subscription(global::DotBoxD.Plugins.PluginPackage package) => new(RecordedInstallKind.Subscription, package, null, null);");
        builder.AppendLine("        public static RecordedInstall ServerExtension(global::DotBoxD.Plugins.PluginPackage package, global::System.Type registryKey) => new(RecordedInstallKind.ServerExtension, package, registryKey, null);");
        builder.AppendLine("        public static RecordedInstall ServerExtension(global::DotBoxD.Plugins.PluginPackage package, global::System.Type registryKey, global::System.Type secondaryRegistryKey) => new(RecordedInstallKind.ServerExtension, package, registryKey, secondaryRegistryKey);");
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendRecordSetup(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.Append("    private global::System.Collections.Generic.List<RecordedInstall> RecordSetup(global::System.Action<")
            .Append(model.SetupInterfaceName).AppendLine(">? setup)");
        builder.AppendLine("    {");
        builder.AppendLine("        var installs = new global::System.Collections.Generic.List<RecordedInstall>();");
        builder.AppendLine("        if (setup is not null)");
        builder.AppendLine("        {");
        var localHandlersArg = model.EventCallbackType is not null ? "_localHandlers" : "null";
        builder.Append("            setup(new SetupRecorder(installs, ").Append(localHandlersArg).AppendLine("));");
        builder.AppendLine("        }");
        builder.AppendLine("        return installs;");
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendReplaySetup(StringBuilder builder)
    {
        builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask ReplaySetupAsync(global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (_setupReplayed) { return; }");
        builder.AppendLine("        while (_setupReplayIndex < _setupInstalls.Count)");
        builder.AppendLine("        {");
        builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("            var install = _setupInstalls[_setupReplayIndex];");
        builder.AppendLine("            if (install.Kind == RecordedInstallKind.Plugin)");
        builder.AppendLine("            {");
        builder.AppendLine("                _ = await InstallPluginPackageAsync(install.Package, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("                _setupReplayIndex++;");
        builder.AppendLine("                continue;");
        builder.AppendLine("            }");
        builder.AppendLine("            if (install.Kind == RecordedInstallKind.Subscription)");
        builder.AppendLine("            {");
        builder.AppendLine("                _ = await InstallSubscriptionPackageAsync(install.Package, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("                _setupReplayIndex++;");
        builder.AppendLine("                continue;");
        builder.AppendLine("            }");
        builder.AppendLine("            var pluginId = await InstallServerExtensionPackageAsync(install.Package, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("            if (install.RegistryKey is not null) { _serverExtensions[install.RegistryKey] = pluginId; }");
        builder.AppendLine("            if (install.SecondaryRegistryKey is not null) { _serverExtensions[install.SecondaryRegistryKey] = pluginId; }");
        builder.AppendLine("            _setupReplayIndex++;");
        builder.AppendLine("        }");
        builder.AppendLine("        _setupReplayed = true;");
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendSetupRecorder(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.Append("    private sealed class SetupRecorder : ").Append(model.SetupInterfaceName).AppendLine();
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly global::System.Collections.Generic.List<RecordedInstall> _installs;");
        builder.Append("        private readonly ").Append(model.HookRegistryName).AppendLine(" _hooks;");
        builder.Append("        private readonly ").Append(model.SubscriptionRegistryName).AppendLine(" _subscriptions;");
        foreach (var control in model.Controls)
        {
            builder.Append("        private readonly ").Append(control.AccumulatorInterfaceName).Append(' ')
                .Append(control.FieldName).AppendLine(";");
        }

        builder.AppendLine("        public SetupRecorder(");
        builder.AppendLine("            global::System.Collections.Generic.List<RecordedInstall> installs,");
        builder.AppendLine("            global::DotBoxD.Plugins.Runtime.Hooks.RemoteLocalHandlerRegistry? localHandlers)");
        builder.AppendLine("        {");
        builder.AppendLine("            _installs = installs;");
        builder.Append("            _hooks = new ").Append(model.HookRegistryName).AppendLine("(package =>");
        builder.AppendLine("            {");
        builder.AppendLine("                _installs.Add(RecordedInstall.Plugin(package));");
        builder.AppendLine("                return global::System.Threading.Tasks.ValueTask.FromResult(package.CallbackSubscriptionId ?? package.Manifest.PluginId);");
        builder.AppendLine("            }, localHandlers);");
        builder.Append("            _subscriptions = new ").Append(model.SubscriptionRegistryName).AppendLine("(package =>");
        builder.AppendLine("            {");
        builder.AppendLine("                _installs.Add(RecordedInstall.Subscription(package));");
        builder.AppendLine("                return global::System.Threading.Tasks.ValueTask.FromResult(package.CallbackSubscriptionId ?? package.Manifest.PluginId);");
        builder.AppendLine("            }, localHandlers);");
        foreach (var control in model.Controls)
        {
            builder.Append("            ").Append(control.FieldName).Append(" = new ")
                .Append(control.Name).AppendLine("SetupAccumulator(installs);");
        }

        builder.AppendLine("        }");
        builder.Append("        public ").Append(model.SetupInterfaceName).AppendLine(" Replace<TService, TKernel>() where TService : class where TKernel : class, TService");
        builder.AppendLine("        {");
        builder.AppendLine("            _installs.Add(RecordedInstall.Plugin(global::DotBoxD.Plugins.Kernel.KernelPackageRegistry.Resolve<TKernel>()));");
        builder.AppendLine("            return this;");
        builder.AppendLine("        }");
        foreach (var control in model.Controls)
        {
            PluginServerXmlDocumentation.Append(builder, "        ", control.Documentation);
            builder.Append("        public ").Append(control.AccumulatorInterfaceName).Append(' ')
                .Append(PluginServerIdentifier.Escape(control.Name)).Append(" => ")
                .Append(control.FieldName).AppendLine(";");
        }
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "        ",
            "Setup-time hook registration surface. Recorded hooks plug plugin logic into server decisions and are awaited by the server.");
        builder.Append("        public ").Append(model.HookRegistryName).AppendLine(" Hooks => _hooks;");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "        ",
            "Setup-time fire-and-forget subscription registration surface. Recorded subscriptions are notifications and the server does not wait for them.");
        builder.Append("        public ").Append(model.SubscriptionRegistryName).AppendLine(" Subscriptions => _subscriptions;");

        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendControlAccumulator(StringBuilder builder, PluginServerControlProperty control)
    {
        builder.Append("    private sealed class ").Append(control.Name).Append("SetupAccumulator : ")
            .Append(control.AccumulatorInterfaceName).AppendLine();
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly global::System.Collections.Generic.List<RecordedInstall> _installs;");
        builder.Append("        public ").Append(control.Name).AppendLine("SetupAccumulator(global::System.Collections.Generic.List<RecordedInstall> installs) => _installs = installs;");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "        ",
            "Records a server-extension kernel for replay at StartAsync.");
        builder.Append("        public ").Append(control.AccumulatorInterfaceName).AppendLine(" Extend<TKernel>() where TKernel : class");
        builder.AppendLine("        {");
        builder.AppendLine("            Add<TKernel>();");
        builder.AppendLine("            return this;");
        builder.AppendLine("        }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "        ",
            "Records a server-extension kernel for the requested service contract and replays it at StartAsync.");
        builder.Append("        public ").Append(control.AccumulatorInterfaceName).AppendLine(" Extend<TService, TKernel>() where TService : class where TKernel : class");
        builder.AppendLine("        {");
        builder.AppendLine("            Add<TService, TKernel>();");
        builder.AppendLine("            return this;");
        builder.AppendLine("        }");
        builder.AppendLine("        private void Add<TKernel>() where TKernel : class");
        builder.AppendLine("            => _installs.Add(RecordedInstall.ServerExtension(global::DotBoxD.Plugins.Kernel.KernelPackageRegistry.Resolve<TKernel>(), typeof(TKernel)));");
        builder.AppendLine("        private void Add<TService, TKernel>() where TService : class where TKernel : class");
        builder.AppendLine("            => _installs.Add(RecordedInstall.ServerExtension(global::DotBoxD.Plugins.Kernel.KernelPackageRegistry.Resolve<TKernel>(), typeof(TService), typeof(TKernel)));");
        builder.AppendLine("    }");
        builder.AppendLine();
    }
}
