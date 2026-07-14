namespace DotBoxD.Abstractions;

internal static class LoweredPipelineStepValidator
{
    public static void ValidateFilterOutput(LoweredPipelineStep step, int index)
    {
        if (step.Kind == LoweredPipelineStepKind.Filter &&
            !string.Equals(step.OutputType, "bool", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"step {index} filter output shape '{step.OutputType}' must be 'bool'.",
                nameof(LoweredPipelineStep.OutputType));
        }
    }
}
