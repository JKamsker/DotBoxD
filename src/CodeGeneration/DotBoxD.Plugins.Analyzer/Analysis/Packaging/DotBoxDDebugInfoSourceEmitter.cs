using System.Text;
using DotBoxD.Plugins.Analyzer.Analysis.Debugging;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class DotBoxDDebugInfoSourceEmitter
{
    public static void EmitDefaultSpan(StringBuilder builder)
    {
        builder.Append("    private static readonly ").Append(TypeNames.GlobalSourceSpan).Append(" Span = new(")
            .Append(DotBoxDGenerationNames.GeneratedSpanLine)
            .Append(", ")
            .Append(DotBoxDGenerationNames.GeneratedSpanColumn)
            .Append(", SequencePointKind: ")
            .Append(TypeNames.GlobalSourceSequencePointKind)
            .AppendLine(".Hidden);");
    }

    public static void EmitPackageReturn(StringBuilder builder, PluginKernelModel model)
    {
        var sources = Sources(model);
        if (sources.Count == 0)
        {
            builder.Append("        return ").Append(TypeNames.GlobalPluginPackage)
                .AppendLine(".Create(manifest, CreateModule(settings));");
            return;
        }

        builder.AppendLine("        var module = CreateModule(settings);");
        EmitDocuments(builder, sources);
        EmitFunctionSpans(builder, model);
        EmitVariableBindings(builder, model);
        builder.Append("        var debugInfo = ").Append(TypeNames.GlobalKernelDebugInfo)
            .AppendLine(".Create(module, documents, variableBindings);");
        builder.Append("        return ").Append(TypeNames.GlobalPluginPackage)
            .AppendLine(".Create(manifest, module, debugInfo: debugInfo);");
    }

    private static IReadOnlyList<KernelSourceLocationModel> Sources(PluginKernelModel model)
    {
        var sources = new List<KernelSourceLocationModel>(2);
        if (model.ShouldHandleSource is not null)
        {
            sources.Add(model.ShouldHandleSource);
        }

        if (model.HandleSource is not null)
        {
            sources.Add(model.HandleSource);
        }

        return sources;
    }

    private static void EmitDocuments(
        StringBuilder builder,
        IReadOnlyList<KernelSourceLocationModel> sources)
    {
        builder.Append("        var documents = new ").Append(TypeNames.GlobalKernelDebugDocument).AppendLine("[]");
        builder.AppendLine("        {");
        foreach (var source in sources.SelectMany(SequencePoints)
                     .GroupBy(item => item.DocumentId, StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            builder.Append("            new ").Append(TypeNames.GlobalKernelDebugDocument).Append('(')
                .Append(LiteralReader.StringLiteral(source.DocumentId)).Append(", ")
                .Append(LiteralReader.StringLiteral(source.Path)).Append(", ")
                .Append(LiteralReader.StringLiteral(source.Sha256Checksum)).AppendLine("),");
        }

        builder.AppendLine("        };");
    }

    private static void EmitFunctionSpans(StringBuilder builder, PluginKernelModel model)
    {
        builder.Append("        var functionSpans = new ")
            .Append(TypeNames.GlobalDictionary).Append("<string, global::System.Collections.Generic.IReadOnlyList<")
            .Append(TypeNames.GlobalSourceSpan).AppendLine(">>");
        builder.AppendLine("        {");
        EmitFunctionSpan(builder, DotBoxDGenerationNames.Entrypoints.ShouldHandle, model.ShouldHandleSource);
        EmitFunctionSpan(builder, DotBoxDGenerationNames.Entrypoints.Handle, model.HandleSource);
        builder.AppendLine("        };");
        builder.Append("        module = ").Append(TypeNames.GlobalKernelDebugModuleMapper)
            .AppendLine(".ApplyFunctionSequenceSpans(module, functionSpans);");
    }

    private static void EmitFunctionSpan(
        StringBuilder builder,
        string functionId,
        KernelSourceLocationModel? source)
    {
        if (source is null)
        {
            return;
        }

        builder.Append("            [").Append(LiteralReader.StringLiteral(functionId)).AppendLine("] = new[]");
        builder.AppendLine("            {");
        foreach (var point in SequencePoints(source))
        {
            builder.Append("                new ").Append(TypeNames.GlobalSourceSpan).Append('(')
                .Append(point.StartLine).Append(", ")
                .Append(point.StartColumn).Append(", ")
                .Append(LiteralReader.StringLiteral(point.DocumentId)).Append(", ")
                .Append(point.EndLine).Append(", ")
                .Append(point.EndColumn).AppendLine("),");
        }

        builder.AppendLine("            },");
    }

    private static void EmitVariableBindings(StringBuilder builder, PluginKernelModel model)
    {
        builder.Append("        var variableBindings = new ")
            .Append(TypeNames.GlobalKernelDebugVariableBinding).AppendLine("[]");
        builder.AppendLine("        {");
        EmitFunctionBindings(
            builder,
            DotBoxDGenerationNames.Entrypoints.ShouldHandle,
            model.EventParameterName,
            model.ContextParameterName,
            projectedSlotName: null,
            model);
        EmitFunctionBindings(
            builder,
            DotBoxDGenerationNames.Entrypoints.Handle,
            model.HandleEventParameterName,
            model.HandleContextParameterName,
            model.HandleProjectedSlotName,
            model);
        builder.AppendLine("        };");
    }

    private static void EmitFunctionBindings(
        StringBuilder builder,
        string functionId,
        string eventParameter,
        string contextParameter,
        string? projectedSlotName,
        PluginKernelModel model)
    {
        if (projectedSlotName is not null)
        {
            EmitVariableBinding(builder, functionId, projectedSlotName, eventParameter);
        }
        else
        {
            foreach (var property in model.EventProperties)
            {
                EmitVariableBinding(
                    builder,
                    functionId,
                    DotBoxDExpressionModelFactory.EventVariable(property.Name),
                    eventParameter + "." + property.Name);
            }
        }

        foreach (var setting in model.LiveSettings)
        {
            EmitVariableBinding(builder, functionId, setting.Name, setting.Name);
        }

        EmitSyntheticBinding(
            builder,
            functionId,
            "$dotboxd.context",
            contextParameter,
            "DotBoxD.Abstractions.HookContext",
            "{HookContext}");
        EmitSyntheticBinding(
            builder,
            functionId,
            "$dotboxd.context.messages",
            contextParameter + ".Messages",
            "DotBoxD.Abstractions.IPluginMessageSink",
            "<host capability proxy>");
        EmitSyntheticBinding(
            builder,
            functionId,
            "$dotboxd.context.cancellationToken",
            contextParameter + ".CancellationToken",
            "System.Threading.CancellationToken",
            "<execution cancellation token>");
    }

    private static void EmitVariableBinding(
        StringBuilder builder,
        string functionId,
        string slotName,
        string sourceName)
        => builder.Append("            new ").Append(TypeNames.GlobalKernelDebugVariableBinding).Append('(')
            .Append(LiteralReader.StringLiteral(functionId)).Append(", ")
            .Append(LiteralReader.StringLiteral(slotName)).Append(", ")
            .Append(LiteralReader.StringLiteral(sourceName)).AppendLine("),");

    private static void EmitSyntheticBinding(
        StringBuilder builder,
        string functionId,
        string slotName,
        string sourceName,
        string typeName,
        string displayValue)
        => builder.Append("            new ").Append(TypeNames.GlobalKernelDebugVariableBinding).Append('(')
            .Append(LiteralReader.StringLiteral(functionId)).Append(", ")
            .Append(LiteralReader.StringLiteral(slotName)).Append(", ")
            .Append(LiteralReader.StringLiteral(sourceName)).Append(", null, null, ")
            .Append(LiteralReader.StringLiteral(typeName)).Append(", ")
            .Append(LiteralReader.StringLiteral(displayValue)).AppendLine("),");

    private static IEnumerable<KernelSourceLocationModel> SequencePoints(KernelSourceLocationModel source)
        => source.SequencePoints.Count == 0 ? [source] : source.SequencePoints;
}
