using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Sandbox.Values;

internal static class SandboxValidatedCollectionShapeMeter
{
    public static bool TryMeasureEmptyCollection(
        SandboxValue value,
        SandboxType expectedType,
        ValidationFailure failure,
        ResourceLimits? limits,
        out ValueShape shape)
    {
        shape = new ValueShape(0, 0, 0, 0, 0, 0);
        switch (value)
        {
            case ListValue { Values.Count: 0 } list:
                if (!expectedType.IsKnown() ||
                    !SandboxValueTypeMatcher.MatchesValidationFrame(list, expectedType))
                {
                    throw SandboxValidatedValueShapeErrors.Error(failure);
                }

                SandboxValidatedValueShapeLimits.EnsureCollectionLimits(0, 0, 1, limits);
                shape = SandboxValidatedValueShapeLimits.AddCollection(shape, 0, 0, 0, 1, limits);
                return true;
            case MapValue { Values.Count: 0 } map:
                if (!expectedType.IsKnown() ||
                    !SandboxValueTypeMatcher.MatchesValidationFrame(map, expectedType))
                {
                    throw SandboxValidatedValueShapeErrors.Error(failure);
                }

                SandboxValidatedValueShapeLimits.EnsureCollectionLimits(0, 0, 1, limits);
                shape = SandboxValidatedValueShapeLimits.AddCollection(shape, 0, 0, 0, 1, limits);
                return true;
            default:
                return false;
        }
    }

    public static ValueShape AddList(
        ValueShape shape,
        ListValue list,
        SandboxType expectedType,
        int parentDepth,
        HashSet<object> active,
        Stack<Frame> stack,
        ResourceLimits? limits,
        ValidationFailure failure)
    {
        if (!SandboxValueTypeMatcher.MatchesValidationFrame(list, expectedType))
        {
            throw SandboxValidatedValueShapeErrors.Error(failure);
        }

        Enter(list, active, failure);
        var depth = parentDepth + 1;
        SandboxValidatedValueShapeLimits.EnsureCollectionLimits(list.Values.Count, 0, depth, limits);
        stack.Push(new Frame(list, expectedType, depth, Exit: true));
        for (var i = list.Values.Count - 1; i >= 0; i--)
        {
            stack.Push(new Frame(list.Values[i], list.ItemType, depth, Exit: false));
        }

        return SandboxValidatedValueShapeLimits.AddCollection(
            shape,
            list.Values.Count,
            list.Values.Count,
            0,
            depth,
            limits);
    }

    public static ValueShape AddMap(
        ValueShape shape,
        MapValue map,
        SandboxType expectedType,
        int parentDepth,
        HashSet<object> active,
        Stack<Frame> stack,
        ResourceLimits? limits,
        ValidationFailure failure)
    {
        if (!SandboxValueTypeMatcher.MatchesValidationFrame(map, expectedType))
        {
            throw SandboxValidatedValueShapeErrors.Error(failure);
        }

        Enter(map, active, failure);
        var depth = parentDepth + 1;
        SandboxValidatedValueShapeLimits.EnsureCollectionLimits(0, map.Values.Count, depth, limits);
        stack.Push(new Frame(map, expectedType, depth, Exit: true));
        foreach (var pair in map.Entries)
        {
            stack.Push(new Frame(pair.Value, map.ValueType, depth, Exit: false));
            stack.Push(new Frame(pair.Key, map.KeyType, depth, Exit: false));
        }

        return SandboxValidatedValueShapeLimits.AddCollection(
            shape,
            map.Values.Count,
            0,
            map.Values.Count,
            depth,
            limits);
    }

    public static ValueShape AddRecord(
        ValueShape shape,
        RecordValue record,
        SandboxType expectedType,
        int parentDepth,
        HashSet<object> active,
        Stack<Frame> stack,
        ResourceLimits? limits,
        ValidationFailure failure)
    {
        if (!expectedType.IsRecord || expectedType.Arguments.Count != record.Fields.Count)
        {
            throw SandboxValidatedValueShapeErrors.Error(failure);
        }

        Enter(record, active, failure);
        var depth = parentDepth + 1;
        SandboxValidatedValueShapeLimits.EnsureCollectionLimits(record.Fields.Count, 0, depth, limits);
        stack.Push(new Frame(record, expectedType, depth, Exit: true));
        for (var i = record.Fields.Count - 1; i >= 0; i--)
        {
            stack.Push(new Frame(record.Fields[i], expectedType.Arguments[i], depth, Exit: false));
        }

        return SandboxValidatedValueShapeLimits.AddCollection(
            shape,
            record.Fields.Count,
            record.Fields.Count,
            0,
            depth,
            limits);
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
}
