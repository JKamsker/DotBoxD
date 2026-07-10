using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Sandbox;

public static class SandboxValueValidator
{
    public static void RequireType(SandboxValue value, SandboxType expectedType, string message)
        => RequireType(value, expectedType, SandboxErrorCode.InvalidInput, message);

    public static void RequireType(
        SandboxValue value,
        SandboxType expectedType,
        SandboxErrorCode errorCode,
        string message)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(expectedType);
        ArgumentNullException.ThrowIfNull(message);

        // Scalars have no nested structure, so they can never form a cycle and need
        // no traversal bookkeeping. Validate them inline to avoid allocating the
        // HashSet/Stack the recursive collection walk requires; this is the hot path
        // for every function return and binding argument check.
        if (value is not ListValue and not MapValue and not RecordValue)
        {
            RequireScalarType(value, expectedType, errorCode, message);
            return;
        }

        if (TryRequireEmptyCollectionType(value, expectedType, errorCode, message))
        {
            return;
        }

        if (!expectedType.IsKnown())
        {
            throw Error(errorCode, message);
        }

        var state = SandboxTraversalState<Frame>.Rent();
        var active = state.Active;
        var stack = state.Stack;
        try
        {
            stack.Push(new Frame(value, expectedType, Exit: false));
            while (stack.Count > 0)
            {
                var frame = stack.Pop();
                if (frame.Exit)
                {
                    active.Remove(frame.Value);
                    continue;
                }

                if (!SandboxValueTypeMatcher.MatchesValidationFrame(frame.Value, frame.ExpectedType))
                {
                    throw Error(errorCode, message);
                }

                SandboxScalarValueValidator.RequireScalarInvariants(frame.Value, errorCode, message);
                switch (frame.Value)
                {
                    case OpaqueIdValue id:
                        SandboxScalarValueValidator.RequireOpaqueId(id, errorCode, message);
                        break;
                    case ListValue list:
                        PushList(list, frame.ExpectedType, active, stack, errorCode, message);
                        break;
                    case MapValue map:
                        PushMap(map, frame.ExpectedType, active, stack, errorCode, message);
                        break;
                    case RecordValue record:
                        PushRecord(record, frame.ExpectedType, active, stack, errorCode, message);
                        break;
                }
            }
        }
        finally
        {
            SandboxTraversalState<Frame>.Return(state);
        }
    }

    private static bool TryRequireEmptyCollectionType(
        SandboxValue value,
        SandboxType expectedType,
        SandboxErrorCode errorCode,
        string message)
    {
        switch (value)
        {
            case ListValue { Values.Count: 0 } list:
                if (!expectedType.IsKnown() ||
                    !SandboxValueTypeMatcher.MatchesValidationFrame(list, expectedType))
                {
                    throw Error(errorCode, message);
                }

                return true;
            case MapValue { Values.Count: 0 } map:
                if (!expectedType.IsKnown() ||
                    !SandboxValueTypeMatcher.MatchesValidationFrame(map, expectedType))
                {
                    throw Error(errorCode, message);
                }

                return true;
            default:
                return false;
        }
    }

    private static void RequireScalarType(
        SandboxValue value,
        SandboxType expectedType,
        SandboxErrorCode errorCode,
        string message)
    {
        if (SandboxScalarValueValidator.IsBuiltInScalarType(value, expectedType))
        {
            SandboxScalarValueValidator.RequireScalarInvariants(value, errorCode, message);
            return;
        }

        if (!SandboxValueTypeMatcher.MatchesValidationFrame(value, expectedType) ||
            !expectedType.IsKnown())
        {
            throw Error(errorCode, message);
        }

        SandboxScalarValueValidator.RequireScalarInvariants(value, errorCode, message);
        if (value is OpaqueIdValue id)
        {
            SandboxScalarValueValidator.RequireOpaqueId(id, errorCode, message);
        }
    }

    private static void PushList(
        ListValue list,
        SandboxType expectedType,
        HashSet<object> active,
        Stack<Frame> stack,
        SandboxErrorCode errorCode,
        string message)
    {
        if (!SandboxValueTypeMatcher.MatchesValidationFrame(list, expectedType))
        {
            throw Error(errorCode, message);
        }

        Enter(list, active, errorCode, message);
        stack.Push(new Frame(list, expectedType, Exit: true));
        for (var i = list.Values.Count - 1; i >= 0; i--)
        {
            stack.Push(new Frame(list.Values[i], list.ItemType, Exit: false));
        }
    }

    private static void PushMap(
        MapValue map,
        SandboxType expectedType,
        HashSet<object> active,
        Stack<Frame> stack,
        SandboxErrorCode errorCode,
        string message)
    {
        if (!SandboxValueTypeMatcher.MatchesValidationFrame(map, expectedType))
        {
            throw Error(errorCode, message);
        }

        Enter(map, active, errorCode, message);
        stack.Push(new Frame(map, expectedType, Exit: true));
        foreach (var pair in map.Entries)
        {
            stack.Push(new Frame(pair.Value, map.ValueType, Exit: false));
            stack.Push(new Frame(pair.Key, map.KeyType, Exit: false));
        }
    }

    private static void PushRecord(
        RecordValue record,
        SandboxType expectedType,
        HashSet<object> active,
        Stack<Frame> stack,
        SandboxErrorCode errorCode,
        string message)
    {
        if (!expectedType.IsRecord ||
            expectedType.Arguments.Count != record.Fields.Count)
        {
            throw Error(errorCode, message);
        }

        Enter(record, active, errorCode, message);
        stack.Push(new Frame(record, expectedType, Exit: true));
        for (var i = record.Fields.Count - 1; i >= 0; i--)
        {
            stack.Push(new Frame(record.Fields[i], expectedType.Arguments[i], Exit: false));
        }
    }

    private static void Enter(
        object value,
        HashSet<object> active,
        SandboxErrorCode errorCode,
        string message)
    {
        if (!active.Add(value))
        {
            throw Error(errorCode, message);
        }
    }

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message)
        => new(new SandboxError(code, message));

    private readonly record struct Frame(SandboxValue Value, SandboxType ExpectedType, bool Exit);
}
