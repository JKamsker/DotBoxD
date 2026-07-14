using DotBoxD.Kernels;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Abstractions;

/// <summary>
/// Merges an ordered sequence of mergeable-IR <see cref="LoweredPipelineStep"/> fragments into one complete,
/// verifiable <see cref="SandboxModule"/>. This is the runtime counterpart to the source generator's
/// build-time hook-chain fusion: a consumer that collected steps from a custom pipeline surface can combine
/// them by hand, which is exactly what "delete the attribute and hand-write it" requires.
/// </summary>
public static class LoweredPipelineComposer
{
    private const string CurrentPlaceholder = "$dotboxd.current";
    private const string CurrentVariablePrefix = "current";
    private const string RequiredCapabilitiesMetadataKey = "dotboxd.requiredCapabilities";
    private const string EffectsMetadataKey = "dotboxd.effects";
    private static readonly IReadOnlyDictionary<string, SandboxType> ResultTypesByTag =
        new Dictionary<string, SandboxType>(StringComparer.Ordinal)
        {
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

    private static readonly SourceSpan Span = new(1, 1);

    public static SandboxModule Compose(LoweredPipelineComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            composition.ModuleId, nameof(LoweredPipelineComposition.ModuleId));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            composition.ShouldHandleFunctionId, nameof(LoweredPipelineComposition.ShouldHandleFunctionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            composition.HandleFunctionId, nameof(LoweredPipelineComposition.HandleFunctionId));
        if (string.Equals(composition.ShouldHandleFunctionId, composition.HandleFunctionId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "ShouldHandleFunctionId and HandleFunctionId must be distinct.", nameof(composition));
        }

        var steps = composition.Steps ?? throw new ArgumentNullException(nameof(LoweredPipelineComposition.Steps));
        if (steps.Count == 0)
        {
            throw new ArgumentException("A pipeline composition requires at least one step.", nameof(composition));
        }

        var inputType = ValidateAndInputType(steps, out var outputTag);
        ValidateResultType(steps, composition.ResultType, inputType, outputTag);

        var shouldHandle = BuildShouldHandle(steps, inputType, composition.ShouldHandleFunctionId);
        var handle = BuildHandle(steps, inputType, composition.ResultType, composition.HandleFunctionId);

        return new SandboxModule(
            composition.ModuleId,
            composition.Version,
            composition.TargetSandboxVersion,
            [],
            [shouldHandle, handle],
            BuildMetadata(steps));
    }

    private static SandboxType ValidateAndInputType(IReadOnlyList<LoweredPipelineStep> steps, out string outputTag)
    {
        var first = steps[0] ?? throw new ArgumentNullException(nameof(LoweredPipelineComposition.Steps));
        var inputType = CurrentParameter(first, 0).Type;
        var currentTag = first.InputType ?? throw new ArgumentNullException(nameof(LoweredPipelineStep.InputType));
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i] ?? throw new ArgumentNullException(nameof(LoweredPipelineComposition.Steps));
            ArgumentNullException.ThrowIfNull(step.InputType, nameof(LoweredPipelineStep.InputType));
            ArgumentNullException.ThrowIfNull(step.OutputType, nameof(LoweredPipelineStep.OutputType));
            ArgumentNullException.ThrowIfNull(step.Value, nameof(LoweredPipelineStep.Value));
            ArgumentNullException.ThrowIfNull(step.Prefix, nameof(LoweredPipelineStep.Prefix));
            var parameter = CurrentParameter(step, i);
            if (step.Kind is not (LoweredPipelineStepKind.Filter or LoweredPipelineStepKind.Projection))
            {
                throw new ArgumentException(
                    $"step {i} has unsupported kind '{step.Kind}'; only Filter and Projection compose.");
            }

            if (step.Prefix.Count != 0)
            {
                throw new NotSupportedException(
                    $"step {i} carries prefix statements, which the composer does not yet support.");
            }

            if (!string.Equals(step.InputType, currentTag, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"step {i} input shape '{step.InputType}' does not match the running pipeline shape '{currentTag}'.");
            }

            if (!ParameterMatchesExactTag(parameter, step.InputType))
            {
                throw new ArgumentException(
                    $"step {i} parameter type '{parameter.Type}' does not match input shape '{step.InputType}'.");
            }

            LoweredPipelineStepValidator.ValidateFilterOutput(step, i);

            if (step.Kind == LoweredPipelineStepKind.Projection)
            {
                currentTag = step.OutputType;
            }
        }

        outputTag = currentTag;
        return inputType;
    }

    private static Parameter CurrentParameter(LoweredPipelineStep step, int index)
    {
        ArgumentNullException.ThrowIfNull(step.Parameters);
        if (step.Parameters.Count != 1)
        {
            throw new ArgumentException(
                $"step {index} must declare exactly one '{CurrentPlaceholder}' parameter.");
        }

        var parameter = step.Parameters[0] ?? throw new ArgumentNullException(nameof(LoweredPipelineStep.Parameters));
        ArgumentNullException.ThrowIfNull(parameter.Name, nameof(Parameter.Name));
        ArgumentNullException.ThrowIfNull(parameter.Type, nameof(Parameter.Type));
        if (!string.Equals(parameter.Name, CurrentPlaceholder, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"step {index} must declare exactly one '{CurrentPlaceholder}' parameter.");
        }

        return parameter;
    }

    private static bool ParameterMatchesExactTag(Parameter parameter, string tag)
    {
        if (tag is "bool")
            return parameter.Type == SandboxType.Bool;
        if (tag is "int" or "i32")
            return parameter.Type == SandboxType.I32;
        if (tag is "long" or "i64")
            return parameter.Type == SandboxType.I64;
        if (tag is "double" or "f64")
            return parameter.Type == SandboxType.F64;
        if (tag is "string")
            return parameter.Type == SandboxType.String;
        return tag is not "guid" || parameter.Type == SandboxType.Guid;
    }

    private static void ValidateResultType(
        IReadOnlyList<LoweredPipelineStep> steps,
        SandboxType resultType,
        SandboxType inputType,
        string outputTag)
    {
        ArgumentNullException.ThrowIfNull(resultType);
        if (!ResultTypeMatchesTag(resultType, outputTag))
        {
            throw new ArgumentException($"ResultType must match terminal OutputType '{outputTag}'.");
        }

        var hasProjection = false;
        foreach (var step in steps)
        {
            if (step.Kind == LoweredPipelineStepKind.Projection)
            {
                hasProjection = true;
                break;
            }
        }

        if (!hasProjection && resultType != inputType)
        {
            throw new ArgumentException(
                "ResultType must equal the pipeline input type when the pipeline has no projection.");
        }
    }

    private static bool ResultTypeMatchesTag(SandboxType type, string tag)
    {
        if (ResultTypesByTag.TryGetValue(tag, out var expectedType))
            return type == expectedType;

        return tag is not ("list" or "map" or "record") || TagMatchesStructuralType(type, tag);
    }

    private static bool TagMatchesStructuralType(SandboxType type, string tag)
        => (tag == "list" && type.Name == "List") ||
           (tag == "map" && type.Name == "Map") ||
           (tag == "record" && type.IsRecord);

    // Gating only needs the steps up to and including the LAST filter: a projection after the final filter can
    // never change whether the event is handled, so recomputing it here is pure waste (and would run an
    // effectful projection twice, once here and once in Handle). Projections BEFORE the last filter are still
    // emitted, because a later filter reads the value they produce.
    private static SandboxFunction BuildShouldHandle(
        IReadOnlyList<LoweredPipelineStep> steps,
        SandboxType inputType,
        string functionId)
    {
        var body = new List<Statement>();
        var current = InitialVariable();
        var lastFilter = LastFilterIndex(steps);
        for (var i = 0; i <= lastFilter; i++)
        {
            var step = steps[i];
            var value = Rewrite(step.Value, current);
            if (step.Kind == LoweredPipelineStepKind.Filter)
            {
                body.Add(new IfStatement(
                    new UnaryExpression("!", value, Span),
                    [new ReturnStatement(Bool(false), Span)],
                    [],
                    Span));
            }
            else
            {
                current = NextVariable(current);
                body.Add(new AssignmentStatement(current, value, Span));
            }
        }

        body.Add(new ReturnStatement(Bool(true), Span));
        return new SandboxFunction(functionId, IsEntrypoint: true, [InputParameter(inputType)], SandboxType.Bool, body);
    }

    private static int LastFilterIndex(IReadOnlyList<LoweredPipelineStep> steps)
    {
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            if (steps[i].Kind == LoweredPipelineStepKind.Filter)
            {
                return i;
            }
        }

        return -1;
    }

    private static SandboxFunction BuildHandle(
        IReadOnlyList<LoweredPipelineStep> steps,
        SandboxType inputType,
        SandboxType resultType,
        string functionId)
    {
        var body = new List<Statement>();
        var current = InitialVariable();
        foreach (var step in steps)
        {
            if (step.Kind != LoweredPipelineStepKind.Projection)
            {
                continue;
            }

            var value = Rewrite(step.Value, current);
            current = NextVariable(current);
            body.Add(new AssignmentStatement(current, value, Span));
        }

        body.Add(new ReturnStatement(new VariableExpression(current, Span), Span));
        return new SandboxFunction(functionId, IsEntrypoint: true, [InputParameter(inputType)], resultType, body);
    }

    private static Parameter InputParameter(SandboxType type) => new(InitialVariable(), type);
    private static string InitialVariable() => CurrentVariablePrefix + "0";
    private static string NextVariable(string current)
        => CurrentVariablePrefix + (int.Parse(current[CurrentVariablePrefix.Length..], System.Globalization.CultureInfo.InvariantCulture) + 1);

    private static LiteralExpression Bool(bool value) => new(SandboxValue.FromBool(value), Span);

    // Rewrites the fragment's $dotboxd.current placeholder to the scoped running-value variable.
    private static Expression Rewrite(Expression expression, string current)
        => expression switch
        {
            VariableExpression { Name: CurrentPlaceholder } variable => new VariableExpression(current, variable.Span),
            VariableExpression variable => throw new NotSupportedException(
                $"a fragment expression may only reference the '{CurrentPlaceholder}' placeholder, not the " +
                $"variable '{variable.Name}' (which could collide with the composer's reserved running-value slots)."),
            LiteralExpression => expression,
            UnaryExpression unary => new UnaryExpression(unary.Operator, Rewrite(unary.Operand, current), unary.Span),
            BinaryExpression binary => new BinaryExpression(
                Rewrite(binary.Left, current), binary.Operator, Rewrite(binary.Right, current), binary.Span),
            CallExpression call => new CallExpression(
                call.Name, RewriteAll(call.Arguments, current), call.GenericType, call.Span),
            _ => throw new NotSupportedException(
                $"the composer cannot rewrite a '{expression.GetType().Name}' fragment expression.")
        };

    private static IReadOnlyList<Expression> RewriteAll(IReadOnlyList<Expression> expressions, string current)
    {
        var rewritten = new Expression[expressions.Count];
        for (var i = 0; i < expressions.Count; i++)
        {
            rewritten[i] = Rewrite(expressions[i], current);
        }

        return rewritten;
    }

    private static IReadOnlyDictionary<string, string> BuildMetadata(IReadOnlyList<LoweredPipelineStep> steps)
    {
        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var step in steps)
        {
            AddAll(capabilities, step.RequiredCapabilities, nameof(LoweredPipelineStep.RequiredCapabilities));
            AddAll(effects, step.Effects, nameof(LoweredPipelineStep.Effects));
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (capabilities.Count != 0)
        {
            metadata[RequiredCapabilitiesMetadataKey] = string.Join(";", capabilities);
        }
        if (effects.Count != 0)
        {
            metadata[EffectsMetadataKey] = string.Join(",", effects);
        }
        return metadata;
    }

    private static void AddAll(SortedSet<string> values, IReadOnlyList<string>? source, string paramName)
    {
        ArgumentNullException.ThrowIfNull(source, paramName);
        foreach (var value in source)
            values.Add(value ?? throw new ArgumentNullException(paramName));
    }
}
