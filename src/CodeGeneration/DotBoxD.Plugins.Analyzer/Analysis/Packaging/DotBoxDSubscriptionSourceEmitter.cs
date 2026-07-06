using System.Text;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class DotBoxDSubscriptionSourceEmitter
{
    private const string IndexedPredicatesProperty = "IndexedPredicates";
    private const string IndexCoversPredicateProperty = "IndexCoversPredicate";
    private const string LocalTerminalProperty = "LocalTerminal";
    private const string ProjectedTypeProperty = "ProjectedType";
    private const string ResultTypeProperty = "ResultType";
    private const string ResultLocalTerminalProperty = "ResultLocalTerminal";

    public static void Emit(StringBuilder builder, PluginKernelModel model)
    {
        var head = $"            [new {TypeNames.GlobalHookSubscriptionManifest}(" +
            $"{LiteralReader.StringLiteral(model.EventName)}, {LiteralReader.StringLiteral(model.KernelName)})";

        if (!RequiresInitializer(model))
        {
            builder.Append(head).AppendLine("])");
            return;
        }

        builder.AppendLine(head);
        builder.AppendLine("            {");
        EmitIndexMetadata(builder, model);
        EmitLocalTerminalMetadata(builder, model);
        EmitResultMetadata(builder, model);
        builder.AppendLine("            }])");
    }

    private static bool RequiresInitializer(PluginKernelModel model)
        => model.IndexPredicates.Count > 0 ||
           model.LocalTerminal ||
           model.ResultType is not null ||
           model.ResultLocalTerminal;

    private static void EmitIndexMetadata(StringBuilder builder, PluginKernelModel model)
    {
        if (model.IndexPredicates.Count == 0)
        {
            return;
        }

        builder.Append("                ").Append(IndexedPredicatesProperty).Append(" = [");
        for (var i = 0; i < model.IndexPredicates.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            EmitIndexPredicate(builder, model.IndexPredicates[i]);
        }

        builder.AppendLine("],");
        builder.Append("                ").Append(IndexCoversPredicateProperty).Append(" = ")
            .Append(model.IndexCoversPredicate
                ? DotBoxDGenerationNames.CSharpLiterals.True
                : DotBoxDGenerationNames.CSharpLiterals.False)
            .AppendLine(",");
    }

    private static void EmitIndexPredicate(StringBuilder builder, IndexPredicateModel predicate)
    {
        builder.Append("new ").Append(TypeNames.GlobalIndexedPredicate).Append('(')
            .Append(LiteralReader.StringLiteral(predicate.Path)).Append(", ")
            .Append(TypeNames.GlobalIndexPredicateOperator).Append('.').Append(predicate.Operator).Append(", ")
            .Append(predicate.ValueLiteral).Append(", ")
            .Append(LiteralReader.StringLiteral(predicate.ValueType)).Append(')');
    }

    private static void EmitLocalTerminalMetadata(StringBuilder builder, PluginKernelModel model)
    {
        if (!model.LocalTerminal)
        {
            return;
        }

        builder.Append("                ").Append(LocalTerminalProperty).Append(" = ")
            .Append(DotBoxDGenerationNames.CSharpLiterals.True).AppendLine(",");
        if (model.ProjectedType is not null)
        {
            builder.Append("                ").Append(ProjectedTypeProperty).Append(" = ")
                .Append(LiteralReader.StringLiteral(model.ProjectedType)).AppendLine(",");
        }
    }

    private static void EmitResultMetadata(StringBuilder builder, PluginKernelModel model)
    {
        if (model.ResultType is not null)
        {
            builder.Append("                ").Append(ResultTypeProperty).Append(" = ")
                .Append(LiteralReader.StringLiteral(model.ResultType)).AppendLine(",");
        }

        if (model.ResultLocalTerminal)
        {
            builder.Append("                ").Append(ResultLocalTerminalProperty).Append(" = ")
                .Append(DotBoxDGenerationNames.CSharpLiterals.True).AppendLine(",");
        }
    }
}
