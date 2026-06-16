using System.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerWrapperEmitter
{
    public static void AppendControlWrapper(
        StringBuilder builder,
        PluginServerFacadeModel model,
        PluginServerControlProperty control)
    {
        builder.AppendLine("    public sealed class " + control.WrapperName + " : " + PluginServerFacadeEmitter.ClientInterfaceRef(model, control) + ", global::DotBoxD.Abstractions.IServerExtensionClientAccessor");
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly " + model.ClassName + " _owner;");
        builder.AppendLine("        private readonly " + control.Type + " _inner;");
        builder.AppendLine("        public " + control.WrapperName + "(" + model.ClassName + " owner, " + control.Type + " inner) { _owner = owner; _inner = inner; }");
        AppendAccessorSurface(builder, "        ");
        foreach (var method in control.Methods)
        {
            AppendMethod(builder, method, "        ");
        }

        foreach (var wrapper in control.ServiceWrappers)
        {
            AppendServiceWrapper(builder, model, wrapper);
        }

        builder.AppendLine("    }");
    }

    private static void AppendServiceWrapper(
        StringBuilder builder,
        PluginServerFacadeModel model,
        PluginServerServiceWrapper wrapper)
    {
        builder.AppendLine("        private sealed class " + wrapper.WrapperName + " : " + wrapper.Type + ", global::DotBoxD.Abstractions.IServerExtensionClientAccessor");
        builder.AppendLine("        {");
        builder.AppendLine("            private readonly " + model.ClassName + " _owner;");
        builder.AppendLine("            private readonly " + wrapper.Type + " _inner;");
        builder.AppendLine("            public " + wrapper.WrapperName + "(" + model.ClassName + " owner, " + wrapper.Type + " inner) { _owner = owner; _inner = inner; }");
        AppendAccessorSurface(builder, "            ");
        foreach (var property in wrapper.Properties)
        {
            builder.Append("            public ").Append(property.Type).Append(' ').Append(property.Name)
                .Append(" => _inner.").Append(property.Name).AppendLine(";");
        }

        foreach (var method in wrapper.Methods)
        {
            AppendMethod(builder, method, "            ");
        }

        builder.AppendLine("        }");
    }

    private static void AppendAccessorSurface(StringBuilder builder, string indent)
    {
        builder.Append(indent).AppendLine("public global::DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions => _owner;");
        builder.Append(indent).AppendLine("public global::System.Threading.Tasks.ValueTask<string> Extend<TService, TKernel>() where TService : class where TKernel : class => _owner.Extend<TService, TKernel>();");
        builder.Append(indent).AppendLine("public global::System.Threading.Tasks.ValueTask<string> Extend<TKernel>() where TKernel : class => _owner.Extend<TKernel>();");
    }

    private static void AppendMethod(StringBuilder builder, PluginServerForwardedMethod method, string indent)
    {
        builder.Append(indent).Append("public ").Append(method.ReturnType).Append(' ').Append(method.Name)
            .Append('(').Append(ParameterList(method)).Append(") => ");
        if (method.ReturnWrapperName is null)
        {
            builder.Append("_inner.").Append(method.Name).Append('(').Append(ArgumentList(method)).AppendLine(");");
            return;
        }

        builder.Append("new ").Append(method.ReturnWrapperName).Append("(_owner, _inner.")
            .Append(method.Name).Append('(').Append(ArgumentList(method)).AppendLine("));");
    }

    private static string ParameterList(PluginServerForwardedMethod method)
        => string.Join(", ", method.Parameters.Select(static p => p.Type + " @" + p.Name));

    private static string ArgumentList(PluginServerForwardedMethod method)
        => string.Join(", ", method.Parameters.Select(static p => "@" + p.Name));
}
