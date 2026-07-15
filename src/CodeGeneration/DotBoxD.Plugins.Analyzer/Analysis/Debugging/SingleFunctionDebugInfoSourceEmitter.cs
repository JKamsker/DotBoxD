using System.Text;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Debugging;

internal static class SingleFunctionDebugInfoSourceEmitter
{
    public static void Emit(
        StringBuilder builder,
        string functionId,
        KernelSourceLocationModel source,
        IReadOnlyList<(string SlotName, string SourceName)> bindings)
    {
        var points = SequencePoints(source).ToArray();
        EmitSpans(builder, functionId, points);
        EmitDocuments(builder, points);
        EmitBindings(builder, functionId, bindings);
        builder.Append("        var debugInfo = ").Append(TypeNames.GlobalKernelDebugInfo)
            .AppendLine(".Create(module, documents, bindings);");
    }

    private static void EmitSpans(
        StringBuilder builder,
        string functionId,
        IReadOnlyList<KernelSourceLocationModel> points)
    {
        builder.Append("        var spans = new ").Append(TypeNames.GlobalSourceSpan).AppendLine("[]");
        builder.AppendLine("        {");
        foreach (var point in points)
        {
            builder.Append("            new ").Append(TypeNames.GlobalSourceSpan).Append('(')
                .Append(point.StartLine).Append(", ").Append(point.StartColumn).Append(", ")
                .Append(LiteralReader.StringLiteral(point.DocumentId)).Append(", ")
                .Append(point.EndLine).Append(", ").Append(point.EndColumn).AppendLine("),");
        }

        builder.AppendLine("        };");
        builder.Append("        var module = ").Append(TypeNames.GlobalKernelDebugModuleMapper)
            .Append(".ApplyFunctionSequenceSpans(package.Module, new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.IReadOnlyList<global::DotBoxD.Kernels.Model.SourceSpan>> { [")
            .Append(LiteralReader.StringLiteral(functionId)).AppendLine("] = spans });");
    }

    private static void EmitDocuments(
        StringBuilder builder,
        IEnumerable<KernelSourceLocationModel> points)
    {
        builder.Append("        var documents = new ").Append(TypeNames.GlobalKernelDebugDocument).AppendLine("[]");
        builder.AppendLine("        {");
        foreach (var document in points.GroupBy(point => point.DocumentId, StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            builder.Append("            new ").Append(TypeNames.GlobalKernelDebugDocument).Append('(')
                .Append(LiteralReader.StringLiteral(document.DocumentId)).Append(", ")
                .Append(LiteralReader.StringLiteral(document.Path)).Append(", ")
                .Append(LiteralReader.StringLiteral(document.Sha256Checksum)).AppendLine("),");
        }

        builder.AppendLine("        };");
    }

    private static void EmitBindings(
        StringBuilder builder,
        string functionId,
        IReadOnlyList<(string SlotName, string SourceName)> bindings)
    {
        builder.Append("        var bindings = new ").Append(TypeNames.GlobalKernelDebugVariableBinding).AppendLine("[]");
        builder.AppendLine("        {");
        foreach (var binding in bindings)
        {
            builder.Append("            new ").Append(TypeNames.GlobalKernelDebugVariableBinding).Append('(')
                .Append(LiteralReader.StringLiteral(functionId)).Append(", ")
                .Append(LiteralReader.StringLiteral(binding.SlotName)).Append(", ")
                .Append(LiteralReader.StringLiteral(binding.SourceName)).AppendLine("),");
        }

        builder.AppendLine("        };");
    }

    private static IEnumerable<KernelSourceLocationModel> SequencePoints(KernelSourceLocationModel source)
        => source.SequencePoints.Count == 0 ? [source] : source.SequencePoints;
}
