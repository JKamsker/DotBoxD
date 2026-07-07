using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Sandbox.Values;

internal static class SandboxValidatedValueShapeMeter
{
    public static ValueShape MeasureBindingReturn(
        SandboxValue value,
        SandboxType expectedType,
        string bindingId,
        ResourceLimits? limits = null,
        CancellationToken cancellationToken = default,
        ResourceMeter? meter = null)
        => MeasureCore(
            value,
            expectedType,
            ValidationFailure.BindingReturn(bindingId),
            limits,
            cancellationToken,
            meter);

    public static ValueShape Measure(
        SandboxValue value,
        SandboxType expectedType,
        SandboxErrorCode errorCode,
        string message,
        ResourceLimits? limits = null,
        CancellationToken cancellationToken = default,
        ResourceMeter? meter = null)
        => MeasureCore(
            value,
            expectedType,
            ValidationFailure.Fixed(errorCode, message),
            limits,
            cancellationToken,
            meter);

    private static ValueShape MeasureCore(
        SandboxValue value,
        SandboxType expectedType,
        ValidationFailure failure,
        ResourceLimits? limits,
        CancellationToken cancellationToken,
        ResourceMeter? meter)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SandboxValidatedScalarShapeMeter.TryMeasureScalar(value, expectedType, failure, limits, out var scalarShape))
        {
            return scalarShape;
        }

        if (SandboxValidatedCollectionShapeMeter.TryMeasureEmptyCollection(
                value,
                expectedType,
                failure,
                limits,
                out var emptyShape))
        {
            return emptyShape;
        }

        if (!expectedType.IsKnown())
        {
            throw SandboxValidatedValueShapeErrors.Error(failure);
        }

        return MeasureKnownValue(value, expectedType, failure, limits, cancellationToken, meter);
    }

    private static ValueShape MeasureKnownValue(
        SandboxValue value,
        SandboxType expectedType,
        ValidationFailure failure,
        ResourceLimits? limits,
        CancellationToken cancellationToken,
        ResourceMeter? meter)
    {
        var state = SandboxTraversalState<Frame>.Rent();
        var active = state.Active;
        var stack = state.Stack;
        var shape = new ValueShape(0, 0, 0, 0, 0, 0);
        var scanned = 0;
        try
        {
            stack.Push(new Frame(value, expectedType, Depth: 0, Exit: false));
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++scanned % 64 == 0)
                {
                    meter?.ChargeFuel(1);
                    meter?.CheckDeadline();
                }

                var frame = stack.Pop();
                if (frame.Exit)
                {
                    active.Remove(frame.Value);
                    continue;
                }

                ValidateKnownType(frame.Value, frame.ExpectedType, failure);
                SandboxValidatedScalarShapeMeter.RequireScalarInvariants(frame.Value, failure);
                shape = MeasureFrame(shape, frame, active, stack, limits, failure);
            }

            return shape;
        }
        finally
        {
            SandboxTraversalState<Frame>.Return(state);
        }
    }

    private static ValueShape MeasureFrame(
        ValueShape shape,
        Frame frame,
        HashSet<object> active,
        Stack<Frame> stack,
        ResourceLimits? limits,
        ValidationFailure failure)
    {
        if (TryMeasureTextFrame(shape, frame.Value, limits, failure, out var textShape))
        {
            return textShape;
        }

        if (TryMeasureCollectionFrame(shape, frame, active, stack, limits, failure, out var collectionShape))
        {
            return collectionShape;
        }

        return shape;
    }

    private static bool TryMeasureTextFrame(
        ValueShape shape,
        SandboxValue value,
        ResourceLimits? limits,
        ValidationFailure failure,
        out ValueShape result)
    {
        switch (value)
        {
            case StringValue text:
                result = SandboxValidatedValueShapeLimits.AddText(
                    shape,
                    SandboxLiteralConstraints.TextShape(text.Value),
                    limits);
                return true;
            case OpaqueIdValue id:
                SandboxValidatedScalarShapeMeter.RequireOpaqueId(id, failure);
                result = SandboxValidatedValueShapeLimits.AddText(
                    shape,
                    SandboxLiteralConstraints.TextShape(id.Value),
                    limits);
                return true;
            case SandboxPathValue path:
                result = SandboxValidatedValueShapeLimits.AddText(
                    shape,
                    SandboxLiteralConstraints.TextShape(path.Value.RelativePath),
                    limits);
                return true;
            case SandboxUriValue uri:
                result = SandboxValidatedValueShapeLimits.AddText(
                    shape,
                    SandboxLiteralConstraints.TextShape(uri.Value.Value),
                    limits);
                return true;
            default:
                result = shape;
                return false;
        }
    }

    private static bool TryMeasureCollectionFrame(
        ValueShape shape,
        Frame frame,
        HashSet<object> active,
        Stack<Frame> stack,
        ResourceLimits? limits,
        ValidationFailure failure,
        out ValueShape result)
    {
        switch (frame.Value)
        {
            case ListValue list:
                result = SandboxValidatedCollectionShapeMeter.AddList(
                    shape,
                    list,
                    frame.ExpectedType,
                    frame.Depth,
                    active,
                    stack,
                    limits,
                    failure);
                return true;
            case MapValue map:
                result = SandboxValidatedCollectionShapeMeter.AddMap(
                    shape,
                    map,
                    frame.ExpectedType,
                    frame.Depth,
                    active,
                    stack,
                    limits,
                    failure);
                return true;
            case RecordValue record:
                result = SandboxValidatedCollectionShapeMeter.AddRecord(
                    shape,
                    record,
                    frame.ExpectedType,
                    frame.Depth,
                    active,
                    stack,
                    limits,
                    failure);
                return true;
            default:
                result = shape;
                return false;
        }
    }

    private static void Enter(
        object value,
        HashSet<object> active,
        ValidationFailure failure)
    {
        if (!active.Add(value))
        {
            throw SandboxValidatedValueShapeErrors.Error(failure);
        }
    }

    private static void ValidateKnownType(
        SandboxValue value,
        SandboxType expectedType,
        ValidationFailure failure)
    {
        if (!SandboxValueTypeMatcher.MatchesValidationFrame(value, expectedType))
        {
            throw SandboxValidatedValueShapeErrors.Error(failure);
        }
    }
}
