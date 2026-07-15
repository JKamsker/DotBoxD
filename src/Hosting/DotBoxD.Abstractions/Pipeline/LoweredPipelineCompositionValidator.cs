using DotBoxD.Kernels;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Abstractions;

internal static class LoweredPipelineCompositionValidator
{
    private const string CurrentPlaceholder = "$dotboxd.current";
    private static readonly IReadOnlyDictionary<string, SandboxType> ScalarTypesByTag =
        new Dictionary<string, SandboxType>(StringComparer.Ordinal)
        {
            ["unit"] = SandboxType.Unit,
            ["bool"] = SandboxType.Bool,
            ["int"] = SandboxType.I32,
            ["i32"] = SandboxType.I32,
            ["long"] = SandboxType.I64,
            ["i64"] = SandboxType.I64,
            ["double"] = SandboxType.F64,
            ["f64"] = SandboxType.F64,
            ["string"] = SandboxType.String,
            ["guid"] = SandboxType.Guid
        };

    public static SandboxType Validate(IReadOnlyList<LoweredPipelineStep> steps, SandboxType resultType)
    {
        var inputType = ValidateSteps(steps, out var outputTag);
        ValidateResultType(steps, resultType, inputType, outputTag);
        return inputType;
    }

    private static SandboxType ValidateSteps(IReadOnlyList<LoweredPipelineStep> steps, out string outputTag)
    {
        var first = steps[0] ?? throw new ArgumentNullException(nameof(LoweredPipelineComposition.Steps));
        var inputType = CurrentParameter(first, 0).Type;
        var currentTag = first.InputType ?? throw new ArgumentNullException(nameof(LoweredPipelineStep.InputType));
        for (var i = 0; i < steps.Count; i++)
        {
            var step = ValidateStep(steps[i], i, currentTag);
            currentTag = step.Kind == LoweredPipelineStepKind.Projection ? step.OutputType : currentTag;
        }

        outputTag = currentTag;
        return inputType;
    }

    private static LoweredPipelineStep ValidateStep(LoweredPipelineStep? step, int index, string currentTag)
    {
        if (step is null)
        {
            throw new ArgumentNullException(nameof(LoweredPipelineComposition.Steps));
        }
        ArgumentNullException.ThrowIfNull(step.InputType, nameof(LoweredPipelineStep.InputType));
        ArgumentNullException.ThrowIfNull(step.OutputType, nameof(LoweredPipelineStep.OutputType));
        ArgumentNullException.ThrowIfNull(step.Value, nameof(LoweredPipelineStep.Value));
        ArgumentNullException.ThrowIfNull(step.Prefix, nameof(LoweredPipelineStep.Prefix));
        if (step.Kind is not (LoweredPipelineStepKind.Filter or LoweredPipelineStepKind.Projection))
        {
            throw new ArgumentException(
                $"step {index} has unsupported kind '{step.Kind}'; only Filter and Projection compose.");
        }

        if (step.Prefix.Count != 0)
        {
            throw new NotSupportedException(
                $"step {index} carries prefix statements, which the composer does not yet support.");
        }

        if (!string.Equals(step.InputType, currentTag, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"step {index} input shape '{step.InputType}' does not match the running pipeline shape '{currentTag}'.");
        }

        var parameter = CurrentParameter(step, index);
        if (!ParameterMatchesTag(parameter.Type, step.InputType))
        {
            throw new ArgumentException(
                $"step {index} parameter type '{parameter.Type}' does not match InputType shape '{step.InputType}'.");
        }

        LoweredPipelineStepValidator.ValidateFilterOutput(step, index);
        return step;
    }

    private static Parameter CurrentParameter(LoweredPipelineStep step, int index)
    {
        ArgumentNullException.ThrowIfNull(step.Parameters);
        if (step.Parameters.Count != 1)
        {
            throw InvalidCurrentParameter(index);
        }

        var parameter = step.Parameters[0] ?? throw new ArgumentNullException(nameof(LoweredPipelineStep.Parameters));
        ArgumentNullException.ThrowIfNull(parameter.Name, nameof(Parameter.Name));
        ArgumentNullException.ThrowIfNull(parameter.Type, nameof(Parameter.Type));
        if (!string.Equals(parameter.Name, CurrentPlaceholder, StringComparison.Ordinal))
        {
            throw InvalidCurrentParameter(index);
        }

        return parameter;
    }

    private static ArgumentException InvalidCurrentParameter(int index)
        => new($"step {index} must declare exactly one '{CurrentPlaceholder}' parameter.");

    private static bool ParameterMatchesTag(SandboxType type, string tag)
    {
        if (ScalarTypesByTag.TryGetValue(tag, out var expectedType))
        {
            return type == expectedType;
        }

        return tag switch
        {
            "list" => type.Name == "List" && type.Arguments.Count == 1,
            "map" => type.Name == "Map" && type.Arguments.Count == 2,
            "record" => type.IsRecord,
            _ => true
        };
    }

    private static void ValidateResultType(
        IReadOnlyList<LoweredPipelineStep> steps,
        SandboxType resultType,
        SandboxType inputType,
        string outputTag)
    {
        ArgumentNullException.ThrowIfNull(resultType);
        if (!TypeMatchesTag(resultType, outputTag))
        {
            throw new ArgumentException($"ResultType must match terminal OutputType '{outputTag}'.");
        }

        if (!steps.Any(static step => step.Kind == LoweredPipelineStepKind.Projection) && resultType != inputType)
        {
            throw new ArgumentException(
                "ResultType must equal the pipeline input type when the pipeline has no projection.");
        }
    }

    private static bool TypeMatchesTag(SandboxType type, string tag)
    {
        if (ScalarTypesByTag.TryGetValue(tag, out var expectedType))
        {
            return type == expectedType;
        }

        return ParameterMatchesTag(type, tag);
    }
}
