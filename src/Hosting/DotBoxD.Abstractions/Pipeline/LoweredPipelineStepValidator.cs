namespace DotBoxD.Abstractions;

internal static class LoweredPipelineStepValidator
{
    public static void ValidateFilterOutput(LoweredPipelineStep step, int index)
    {
        if (step.Kind == LoweredPipelineStepKind.Filter &&
            !IsBooleanOutput(step.OutputType))
        {
            throw new ArgumentException(
                $"step {index} filter output shape '{step.OutputType}' must be 'bool'.",
                nameof(LoweredPipelineStep.OutputType));
        }
    }

    private static bool IsBooleanOutput(string outputType)
        => string.Equals(outputType, "bool", StringComparison.Ordinal) ||
           string.Equals(outputType, "System.Boolean", StringComparison.Ordinal);
}
