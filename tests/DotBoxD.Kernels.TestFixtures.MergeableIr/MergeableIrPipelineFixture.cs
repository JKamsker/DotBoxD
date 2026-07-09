using DotBoxD.Abstractions;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.TestFixtures.MergeableIr;

public sealed record ProbeEvent([property: Capability("probe.read.distance")] int Distance, string TargetId);

public sealed class StepPipeline<T>
{
    private readonly List<LoweredPipelineStep> _steps;

    public StepPipeline() : this([])
    {
    }

    private StepPipeline(List<LoweredPipelineStep> steps) => _steps = steps;

    public IReadOnlyList<LoweredPipelineStep> Steps => _steps;

    public StepPipeline<T> Where(
        Func<T, bool> predicate,
        [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate = null)
    {
        _steps.Add(RequiredStep(irPredicate, nameof(irPredicate)));
        return this;
    }

    public StepPipeline<TNext> Select<TNext>(
        Func<T, TNext> selector,
        [IRBodyOf(nameof(selector))] IRFunc<T, TNext>? irSelector = null)
    {
        _steps.Add(RequiredStep(irSelector, nameof(irSelector)));
        return new StepPipeline<TNext>(_steps);
    }

    public T? Run<TInput>(TInput input)
    {
        var value = MergeableIrStepRuntime.Execute(_steps, input);
        return value is null ? default : (T)value;
    }

    private static LoweredPipelineStep RequiredStep<TInput, TOutput>(
        IRFunc<TInput, TOutput>? irFunc,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(irFunc, parameterName);
        return irFunc.Step;
    }
}

public static class MergeableIrPipelineFixture
{
    public static StepPipeline<string> Configure(StepPipeline<ProbeEvent> pipeline)
        => pipeline.Where(e => e.Distance >= 4).Select(e => e.TargetId);

    public static IReadOnlyList<LoweredPipelineStep> ConfigureSteps()
        => Configure(new StepPipeline<ProbeEvent>()).Steps;

    public static string? Project(ProbeEvent input)
        => Configure(new StepPipeline<ProbeEvent>()).Run(input);
}

internal static class MergeableIrStepRuntime
{
    public static object? Execute(IReadOnlyList<LoweredPipelineStep> steps, object? input)
    {
        var current = ToValue(input);
        foreach (var step in steps)
        {
            var value = Evaluate(step.Value, current);
            if (step.Kind == LoweredPipelineStepKind.Filter)
            {
                if (!((BoolValue)value).Value)
                {
                    return null;
                }

                continue;
            }

            current = value;
        }

        return FromValue(current);
    }

    private static SandboxValue Evaluate(Expression expression, SandboxValue current)
        => expression switch
        {
            LiteralExpression literal => literal.Value,
            VariableExpression { Name: "$dotboxd.current" } => current,
            CallExpression { Name: "record.get" } call => RecordGet(call, current),
            BinaryExpression binary => EvaluateBinary(binary, current),
            _ => throw new NotSupportedException(expression.GetType().Name)
        };

    private static SandboxValue RecordGet(CallExpression call, SandboxValue current)
    {
        var record = (RecordValue)Evaluate(call.Arguments[0], current);
        var index = (I32Value)Evaluate(call.Arguments[1], current);
        return record.Fields[index.Value];
    }

    private static SandboxValue EvaluateBinary(BinaryExpression binary, SandboxValue current)
    {
        var left = Evaluate(binary.Left, current);
        var right = Evaluate(binary.Right, current);
        return binary.Operator switch
        {
            ">=" => SandboxValue.FromBool(((I32Value)left).Value >= ((I32Value)right).Value),
            _ => throw new NotSupportedException(binary.Operator)
        };
    }

    private static SandboxValue ToValue(object? input)
        => input is ProbeEvent probe
            ? SandboxValue.FromRecord(
                [
                    SandboxValue.FromInt32(probe.Distance),
                    SandboxValue.FromString(probe.TargetId)
                ])
            : throw new NotSupportedException(input?.GetType().Name);

    private static object FromValue(SandboxValue value)
        => value switch
        {
            StringValue text => text.Value,
            I32Value number => number.Value,
            BoolValue boolean => boolean.Value,
            _ => throw new NotSupportedException(value.GetType().Name)
        };
}
