using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

/// <summary>
/// Direct, array-free dispatch for the fixed-arity collection intrinsics
/// (<c>list.*</c> / <c>map.*</c> except the variadic <c>list.of</c>).
///
/// These intrinsics complete synchronously, read their operands positionally, and
/// never let an argument escape into host code, so when the operands are already
/// evaluated they can be handed to <see cref="CollectionOperations"/> straight from
/// locals. That avoids the per-call <c>SandboxValue[]</c> the general call path would
/// otherwise allocate for cheap collection calls inside loops (PAL-0038). The operand
/// ordering and charged operations match <see cref="ExpressionEvaluator"/>'s
/// array-backed path exactly, so observable behavior is unchanged.
/// </summary>
internal static class CollectionIntrinsicDispatcher
{
    private static readonly IReadOnlyDictionary<string, int> FixedArities =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["list.empty"] = 0,
            ["map.empty"] = 0,
            ["list.count"] = 1,
            ["list.get"] = 2,
            ["list.add"] = 2,
            ["record.get"] = 2,
            ["map.containsKey"] = 2,
            ["map.get"] = 2,
            ["map.set"] = 3,
            ["map.remove"] = 2
        };

    /// <summary>
    /// Returns the fixed arity of a collection intrinsic, or <c>-1</c> when the call is
    /// not a fixed-arity collection intrinsic eligible for array-free dispatch. The
    /// variadic <c>list.of</c> is intentionally excluded: it still flows through the
    /// general array path because it must observe the exact argument count.
    /// </summary>
    public static int FixedArity(string name)
        => FixedArities.TryGetValue(name, out var arity) ? arity : -1;

    /// <summary>
    /// Dispatches a fixed-arity collection intrinsic from already-evaluated operands.
    /// <paramref name="arg0"/> through <paramref name="arg2"/> are the operands in
    /// source order (the same order the array path fills them); unused slots are ignored.
    /// </summary>
    public static SandboxValue Dispatch(
        CallExpression call,
        SandboxValue arg0,
        SandboxValue arg1,
        SandboxValue arg2,
        SandboxContext context)
    {
        if (TryDispatchList(call, arg0, arg1, context, out var listResult))
        {
            return listResult;
        }

        if (TryDispatchRecord(call, arg0, arg1, context, out var recordResult))
        {
            return recordResult;
        }

        if (TryDispatchMap(call, arg0, arg1, arg2, context, out var mapResult))
        {
            return mapResult;
        }

        throw new SandboxRuntimeException(
            new SandboxError(SandboxErrorCode.ValidationError, $"'{call.Name}' is not a fixed-arity collection intrinsic"));
    }

    private static bool TryDispatchList(
        CallExpression call,
        SandboxValue arg0,
        SandboxValue arg1,
        SandboxContext context,
        out SandboxValue result)
    {
        result = call.Name switch
        {
            "list.empty" => CollectionOperations.CreateList(call.GenericType ?? SandboxType.Unit, context),
            "list.count" => CollectionOperations.CountList(arg0, context),
            "list.get" => CollectionOperations.GetListItem(arg1, arg0, context),
            "list.add" => CollectionOperations.AddListItem(arg1, arg0, context),
            _ => SandboxValue.Unit
        };
        return call.Name is "list.empty" or "list.count" or "list.get" or "list.add";
    }

    private static bool TryDispatchRecord(
        CallExpression call,
        SandboxValue arg0,
        SandboxValue arg1,
        SandboxContext context,
        out SandboxValue result)
    {
        if (call.Name == "record.get")
        {
            result = CollectionOperations.GetRecordField(arg1, arg0, context);
            return true;
        }

        result = SandboxValue.Unit;
        return false;
    }

    private static bool TryDispatchMap(
        CallExpression call,
        SandboxValue arg0,
        SandboxValue arg1,
        SandboxValue arg2,
        SandboxContext context,
        out SandboxValue result)
    {
        if (!IsMapCall(call.Name))
        {
            result = SandboxValue.Unit;
            return false;
        }

        result = call.Name switch
        {
            "map.empty" => CollectionOperations.CreateMap(
                call.GenericType ?? SandboxType.Map(SandboxType.Unit, SandboxType.Unit),
                context),
            "map.containsKey" => CollectionOperations.ContainsMapKey(arg1, arg0, context),
            "map.get" => CollectionOperations.GetMapValue(arg1, arg0, context),
            "map.set" => CollectionOperations.SetMapValue(arg2, arg1, arg0, context),
            "map.remove" => CollectionOperations.RemoveMapValue(arg1, arg0, context),
            _ => SandboxValue.Unit
        };
        return true;
    }

    private static bool IsMapCall(string name)
        => name is "map.empty" or "map.containsKey" or "map.get" or "map.set" or "map.remove";
}
