using System.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerWrapperEmitter
{
    public static void AppendControlWrapper(
        StringBuilder builder,
        PluginServerFacadeModel model,
        PluginServerControlProperty control)
    {
        var fields = ResolveBackingFields(control.Properties, control.Methods);
        PluginServerXmlDocumentation.Append(builder, "    ", control.Documentation);
        PluginServerClsComplianceAttributeSource.AppendFalse(builder, model, "    ");
        builder.AppendLine("    public sealed class " + control.WrapperName + " : " + control.Type + ", global::DotBoxD.Abstractions.IServerExtensionClientAccessor");
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly " + PluginServerIdentifier.Escape(model.ClassName) + " " + fields.Owner + ";");
        builder.AppendLine("        private readonly " + control.Type + " " + fields.Inner + ";");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "        ",
            "Creates a generated wrapper around the remote domain control and the owning plugin server.");
        builder.AppendLine("        public " + control.WrapperName + "(" + PluginServerIdentifier.Escape(model.ClassName) + " owner, " + control.Type + " inner) { " + fields.Owner + " = owner; " + fields.Inner + " = inner; }");
        AppendAccessorSurface(builder, "        ", fields.Owner);
        foreach (var property in control.Properties)
        {
            AppendProperty(builder, property, "        ", fields.Owner, fields.Inner);
        }

        foreach (var method in control.Methods)
        {
            AppendMethod(builder, method, "        ", fields.Owner, fields.Inner);
        }

        foreach (var wrapper in control.ServiceWrappers)
        {
            AppendServiceWrapper(builder, model, wrapper, "        ");
        }

        builder.AppendLine("    }");
    }

    public static void AppendServiceWrapper(
        StringBuilder builder,
        PluginServerFacadeModel model,
        PluginServerServiceWrapper wrapper,
        string indent)
    {
        var fields = ResolveBackingFields(wrapper.Properties, wrapper.Methods);
        var memberIndent = indent + "    ";
        PluginServerXmlDocumentation.Append(builder, indent, wrapper.Documentation);
        builder.Append(indent).Append("private sealed class ").Append(wrapper.WrapperName)
            .Append(" : ").Append(wrapper.Type)
            .AppendLine(", global::DotBoxD.Abstractions.IServerExtensionClientAccessor");
        builder.Append(indent).AppendLine("{");
        builder.Append(memberIndent).Append("private readonly ")
            .Append(PluginServerIdentifier.Escape(model.ClassName)).Append(' ').Append(fields.Owner).AppendLine(";");
        builder.Append(memberIndent).Append("private readonly ").Append(wrapper.Type).Append(' ').Append(fields.Inner).AppendLine(";");
        builder.Append(memberIndent).Append("public ").Append(wrapper.WrapperName)
            .Append('(').Append(PluginServerIdentifier.Escape(model.ClassName)).Append(" owner, ")
            .Append(wrapper.Type).Append(" inner) { ").Append(fields.Owner).Append(" = owner; ")
            .Append(fields.Inner).AppendLine(" = inner; }");
        AppendAccessorSurface(builder, memberIndent, fields.Owner);
        foreach (var property in wrapper.Properties)
        {
            AppendProperty(builder, property, memberIndent, fields.Owner, fields.Inner);
        }

        foreach (var method in wrapper.Methods)
        {
            AppendMethod(builder, method, memberIndent, fields.Owner, fields.Inner);
        }

        builder.Append(indent).AppendLine("}");
    }

    private static void AppendAccessorSurface(StringBuilder builder, string indent, string ownerFieldName)
    {
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            indent,
            "Registry for server extension clients installed through setup, Extend, or EnsureAnonymousKernelAsync.");
        builder.Append(indent)
            .Append("global::DotBoxD.Abstractions.IServerExtensionClientRegistry global::DotBoxD.Abstractions.IServerExtensionClientAccessor.ServerExtensions => ")
            .Append(ownerFieldName).AppendLine(".RequireFacade();");
    }

    private static void AppendProperty(
        StringBuilder builder,
        PluginServerForwardedProperty property,
        string indent,
        string ownerFieldName,
        string innerFieldName)
    {
        PluginServerXmlDocumentation.Append(builder, indent, property.Documentation);
        PluginServerFlowAttributeSource.Append(builder, indent, property.Attributes);
        builder.Append(indent).Append("public ").Append(property.Type).Append(' ')
            .Append(PluginServerIdentifier.Escape(property.Name)).AppendLine();
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).AppendLine("    get");
        builder.Append(indent).AppendLine("    {");
        builder.Append(indent).Append("        ").Append(ownerFieldName).AppendLine(".RequireFacade();");
        if (property.ReturnWrapperName is null)
        {
            builder.Append(indent).Append("        return ").Append(innerFieldName).Append('.')
                .Append(PluginServerIdentifier.Escape(property.Name)).AppendLine(";");
            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("}");
            return;
        }

        builder.Append(indent).Append("        return new ").Append(property.ReturnWrapperName).Append('(').Append(ownerFieldName).Append(", ")
            .Append(innerFieldName).Append('.')
            .Append(PluginServerIdentifier.Escape(property.Name)).AppendLine(");");
        builder.Append(indent).AppendLine("    }");
        builder.Append(indent).AppendLine("}");
    }

    private static void AppendMethod(
        StringBuilder builder,
        PluginServerForwardedMethod method,
        string indent,
        string ownerFieldName,
        string innerFieldName)
    {
        PluginServerXmlDocumentation.Append(builder, indent, method.Documentation);
        PluginServerFlowAttributeSource.Append(builder, indent, method.Attributes);
        PluginServerFlowAttributeSource.Append(builder, indent, method.ReturnAttributes);
        builder.Append(indent).Append("public ");
        if (method.ReturnWrapperKind is PluginServerReturnWrapperKind.Task or PluginServerReturnWrapperKind.ValueTask)
        {
            builder.Append("async ");
        }

        builder.Append(method.ReturnType).Append(' ').Append(PluginServerIdentifier.Escape(method.Name))
            .Append('(').Append(ParameterList(method)).AppendLine(")");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    ").Append(ownerFieldName).AppendLine(".RequireFacade();");
        if (PluginServerReturnWrapperCancellationEmitter.RequiresAsyncReturnWrapperBlock(method))
        {
            AppendAsyncReturnWrapperBlock(builder, method, indent, ownerFieldName, innerFieldName);
            builder.Append(indent).AppendLine("}");
            return;
        }

        if (method.ReturnWrapperName is null)
        {
            builder.Append(indent).Append("    ");
            if (!string.Equals(method.ReturnType, "void", StringComparison.Ordinal))
            {
                builder.Append("return ");
            }

            builder.Append("((").Append(method.ReceiverType).Append(')').Append(innerFieldName).Append(").")
                .Append(PluginServerIdentifier.Escape(method.Name))
                .Append('(').Append(ArgumentList(method)).AppendLine(");");
            builder.Append(indent).AppendLine("}");
            return;
        }

        if (method.ReturnWrapperKind is PluginServerReturnWrapperKind.Task or PluginServerReturnWrapperKind.ValueTask)
        {
            builder.Append(indent).Append("    return new ").Append(method.ReturnWrapperName).Append('(').Append(ownerFieldName).Append(", await ((")
                .Append(method.ReceiverType).Append(')').Append(innerFieldName).Append(").")
                .Append(PluginServerIdentifier.Escape(method.Name)).Append('(').Append(ArgumentList(method))
                .AppendLine(").ConfigureAwait(false));");
            builder.Append(indent).AppendLine("}");
            return;
        }

        builder.Append(indent).Append("    return new ").Append(method.ReturnWrapperName).Append('(').Append(ownerFieldName).Append(", ((")
            .Append(method.ReceiverType).Append(')').Append(innerFieldName).Append(").")
            .Append(PluginServerIdentifier.Escape(method.Name)).Append('(').Append(ArgumentList(method)).AppendLine("));");
        builder.Append(indent).AppendLine("}");
    }

    private static void AppendAsyncReturnWrapperBlock(
        StringBuilder builder,
        PluginServerForwardedMethod method,
        string indent,
        string ownerFieldName,
        string innerFieldName)
    {
        var localName = PluginServerReturnWrapperCancellationEmitter.UniqueLocalName("__dotboxdResult", method);
        builder.AppendLine();
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    var ").Append(localName).Append(" = await ((")
            .Append(method.ReceiverType).Append(')').Append(innerFieldName).Append(").")
            .Append(PluginServerIdentifier.Escape(method.Name)).Append('(').Append(ArgumentList(method))
            .AppendLine(").ConfigureAwait(false);");
        PluginServerReturnWrapperCancellationEmitter.AppendCancellationChecks(builder, method, indent + "    ");
        builder.Append(indent).Append("    return new ").Append(method.ReturnWrapperName).Append('(')
            .Append(ownerFieldName).Append(", ").Append(localName).AppendLine(");");
        builder.Append(indent).AppendLine("}");
    }

    private static WrapperBackingFields ResolveBackingFields(
        IReadOnlyList<PluginServerForwardedProperty> properties,
        IReadOnlyList<PluginServerForwardedMethod> methods)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            used.Add(property.Name);
        }

        foreach (var method in methods)
        {
            used.Add(method.Name);
        }

        return new WrapperBackingFields(
            UniqueBackingFieldName(PluginServerWrapperBackingFieldNames.Owner, used),
            UniqueBackingFieldName(PluginServerWrapperBackingFieldNames.Inner, used));
    }

    private static string UniqueBackingFieldName(string preferred, HashSet<string> used)
    {
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

    private static string ParameterList(PluginServerForwardedMethod method)
        => string.Join(
            ", ",
            method.Parameters.Select(static p =>
                p.AttributePrefix + ParamsModifier(p) + p.Type + " @" + p.Name + p.DefaultClause));

    private static string ParamsModifier(PluginServerParameter parameter)
        => parameter.IsParams ? "params " : string.Empty;

    private static string ArgumentList(PluginServerForwardedMethod method)
        => string.Join(", ", method.Parameters.Select(static p => "@" + p.Name));

    private readonly record struct WrapperBackingFields(string Owner, string Inner);
}

internal static class PluginServerWrapperBackingFieldNames
{
    public const string Owner = "_owner";

    public const string Inner = "_inner";
}
