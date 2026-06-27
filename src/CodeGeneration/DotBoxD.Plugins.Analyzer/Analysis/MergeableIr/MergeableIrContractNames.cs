using DotBoxD.Plugins.Analyzer.Analysis.Lowering;

namespace DotBoxD.Plugins.Analyzer.Analysis.MergeableIr;

internal static class MergeableIrContractNames
{
    public const string LoweredPipelineStep = "DotBoxD.Abstractions.LoweredPipelineStep";
    public const string GlobalLoweredPipelineStep = DotBoxDGenerationNames.TypeNames.GlobalPrefix + LoweredPipelineStep;
    public const string GlobalLoweredPipelineStepKind =
        DotBoxDGenerationNames.TypeNames.GlobalPrefix + "DotBoxD.Abstractions.LoweredPipelineStepKind";
}
