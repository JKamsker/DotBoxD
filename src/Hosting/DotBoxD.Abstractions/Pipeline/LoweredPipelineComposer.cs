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
    private static readonly SourceSpan Span = new(1, 1, SequencePointKind: SourceSequencePointKind.Hidden);

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

        var inputType = LoweredPipelineCompositionValidator.Validate(steps, composition.ResultType);

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

    // Gating only needs the steps through the last filter; later projections cannot affect whether the event is
    // handled and may be effectful, while earlier projections still feed later filters.
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
                    [new ReturnStatement(Bool(false), value.Span)],
                    [],
                    value.Span));
            }
            else
            {
                current = NextVariable(current);
                body.Add(new AssignmentStatement(current, value, value.Span));
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
            body.Add(new AssignmentStatement(current, value, value.Span));
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
            LiteralExpression literal => new LiteralExpression(literal.Value, literal.Span),
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
