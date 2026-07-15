using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

internal sealed partial class ExpressionEvaluator
{
    private delegate SandboxValue CollectionCallHandler(
        ExpressionEvaluator evaluator,
        CallExpression call,
        SandboxValue[] args);

    private static readonly Dictionary<string, CollectionCallHandler> CollectionCalls = new(StringComparer.Ordinal)
    {
        ["list.empty"] = static (evaluator, call, _) =>
            CollectionOperations.CreateList(call.GenericType ?? SandboxType.Unit, evaluator._context),
        ["list.of"] = static (evaluator, _, args) => CollectionOperations.BuildList(args, evaluator._context),
        ["list.count"] = static (evaluator, _, args) => CollectionOperations.CountList(Arg(args, 0), evaluator._context),
        ["list.get"] = static (evaluator, _, args) => CollectionOperations.GetListItem(Arg(args, 1), Arg(args, 0), evaluator._context),
        ["list.add"] = static (evaluator, _, args) => CollectionOperations.AddListItem(Arg(args, 1), Arg(args, 0), evaluator._context),
        ["map.empty"] = static (evaluator, call, _) => CollectionOperations.CreateMap(
            call.GenericType ?? SandboxType.Map(SandboxType.Unit, SandboxType.Unit),
            evaluator._context),
        ["map.containsKey"] = static (evaluator, _, args) => CollectionOperations.ContainsMapKey(Arg(args, 1), Arg(args, 0), evaluator._context),
        ["map.get"] = static (evaluator, _, args) => CollectionOperations.GetMapValue(Arg(args, 1), Arg(args, 0), evaluator._context),
        ["map.set"] = static (evaluator, _, args) => CollectionOperations.SetMapValue(Arg(args, 2), Arg(args, 1), Arg(args, 0), evaluator._context),
        ["map.remove"] = static (evaluator, _, args) => CollectionOperations.RemoveMapValue(Arg(args, 1), Arg(args, 0), evaluator._context),
        ["record.new"] = static (evaluator, _, args) => CollectionOperations.BuildRecord(args, evaluator._context),
        ["record.get"] = static (evaluator, _, args) => CollectionOperations.GetRecordField(Arg(args, 1), Arg(args, 0), evaluator._context),
        ["numeric.toI64"] = static (_, _, args) => NumericToInt64(Arg(args, 0)),
        ["numeric.toF64"] = static (_, _, args) => NumericToDouble(Arg(args, 0)),
    };

    private ValueTask<SandboxValue> EvaluateCall(CallExpression call, InterpreterFrame frame)
    {
        if (_debug is not null)
        {
            return EvaluateDebugCallAsync(call, frame);
        }

        return EvaluateCallCore(call, frame);
    }

    private async ValueTask<SandboxValue> EvaluateDebugCallAsync(CallExpression call, InterpreterFrame frame)
    {
        await _debug!.CheckpointAsync(SandboxDebugCheckpointKind.Call, call, frame).ConfigureAwait(false);
        return await EvaluateCallCore(call, frame).ConfigureAwait(false);
    }

    private ValueTask<SandboxValue> EvaluateCallCore(CallExpression call, InterpreterFrame frame)
    {
        if (UnaryPureIntrinsicDispatcher.IsCandidate(call.Name) &&
            UnaryPureIntrinsicDispatcher.TryEvaluate(
                call, this, frame, _context, _options, _moduleHash, frame.FunctionId, out var mathValue))
        {
            return mathValue;
        }

        if (IsNumericConversion(call.Name) && call.Arguments.Count == 1)
        {
            return EvaluateNumericConversion(call.Name, call.Arguments[0], frame);
        }

        var fixedArity = CollectionIntrinsicDispatcher.FixedArity(call.Name);
        if (fixedArity >= 0 && fixedArity == call.Arguments.Count)
        {
            return EvaluateFixedArityCollectionCall(call, fixedArity, frame);
        }

        if (TryGetScalarLocalFunction(call, out var function))
        {
            return EvaluateScalarLocalFunctionCall(call, function, frame);
        }

        return EvaluateCallViaArray(call, frame);
    }

    private ValueTask<SandboxValue> EvaluateFixedArityCollectionCall(
        CallExpression call,
        int arity,
        InterpreterFrame frame)
    {
        var arg0 = SandboxValue.Unit;
        var arg1 = SandboxValue.Unit;
        var arg2 = SandboxValue.Unit;
        for (var i = 0; i < arity; i++)
        {
            var argTask = EvaluateAsync(call.Arguments[i], frame);
            if (!argTask.IsCompletedSuccessfully)
            {
                return AwaitCollectionOperands(call, arity, i, argTask, arg0, arg1, frame);
            }

            switch (i)
            {
                case 0:
                    arg0 = argTask.Result;
                    break;
                case 1:
                    arg1 = argTask.Result;
                    break;
                default:
                    arg2 = argTask.Result;
                    break;
            }
        }

        return new ValueTask<SandboxValue>(
            CollectionIntrinsicDispatcher.Dispatch(call, arg0, arg1, arg2, _context));
    }

    private async ValueTask<SandboxValue> AwaitCollectionOperands(
        CallExpression call,
        int arity,
        int pending,
        ValueTask<SandboxValue> pendingTask,
        SandboxValue arg0,
        SandboxValue arg1,
        InterpreterFrame frame)
    {
        var arg2 = SandboxValue.Unit;
        var resolved = await pendingTask.ConfigureAwait(false);
        switch (pending)
        {
            case 0:
                arg0 = resolved;
                break;
            case 1:
                arg1 = resolved;
                break;
            default:
                arg2 = resolved;
                break;
        }

        for (var i = pending + 1; i < arity; i++)
        {
            var operand = await EvaluateAsync(call.Arguments[i], frame).ConfigureAwait(false);
            switch (i)
            {
                case 1:
                    arg1 = operand;
                    break;
                default:
                    arg2 = operand;
                    break;
            }
        }

        return CollectionIntrinsicDispatcher.Dispatch(call, arg0, arg1, arg2, _context);
    }

    private bool TryGetScalarLocalFunction(CallExpression call, out SandboxFunction function)
    {
        function = null!;
        return call.Arguments.Count is 1 or 2 &&
               !CollectionCalls.ContainsKey(call.Name) &&
               _interpreter.TryGetFunction(call.Name, out function) &&
               function.Parameters.Count == call.Arguments.Count;
    }

    private ValueTask<SandboxValue> EvaluateScalarLocalFunctionCall(
        CallExpression call,
        SandboxFunction function,
        InterpreterFrame frame)
    {
        var argumentCount = call.Arguments.Count;
        var firstTask = EvaluateAsync(call.Arguments[0], frame);
        if (!firstTask.IsCompletedSuccessfully)
        {
            return AwaitCallArguments(
                call,
                new SandboxValue[argumentCount],
                pending: 0,
                firstTask,
                frame);
        }

        var first = firstTask.Result;
        if (argumentCount == 1)
        {
            return _interpreter.InvokeFunctionAsync(function, LocalFunctionArguments.FromSingle(first));
        }

        var secondTask = EvaluateAsync(call.Arguments[1], frame);
        if (!secondTask.IsCompletedSuccessfully)
        {
            var arguments = new SandboxValue[2];
            arguments[0] = first;
            return AwaitCallArguments(call, arguments, pending: 1, secondTask, frame);
        }

        return _interpreter.InvokeFunctionAsync(
            function, LocalFunctionArguments.FromPair(first, secondTask.Result));
    }

    private ValueTask<SandboxValue> EvaluateCallViaArray(CallExpression call, InterpreterFrame frame)
    {
        var arguments = call.Arguments;
        var argCount = arguments.Count;
        var args = argCount == 0 ? Array.Empty<SandboxValue>() : new SandboxValue[argCount];
        for (var i = 0; i < argCount; i++)
        {
            var argTask = EvaluateAsync(arguments[i], frame);
            if (!argTask.IsCompletedSuccessfully)
            {
                return AwaitCallArguments(call, args, i, argTask, frame);
            }

            args[i] = argTask.Result;
        }

        return DispatchCall(call, args, frame);
    }

    private async ValueTask<SandboxValue> AwaitCallArguments(
        CallExpression call,
        SandboxValue[] args,
        int pending,
        ValueTask<SandboxValue> pendingTask,
        InterpreterFrame frame)
    {
        args[pending] = await pendingTask.ConfigureAwait(false);
        for (var i = pending + 1; i < args.Length; i++)
        {
            args[i] = await EvaluateAsync(call.Arguments[i], frame).ConfigureAwait(false);
        }

        return await DispatchCall(call, args, frame).ConfigureAwait(false);
    }

    private ValueTask<SandboxValue> DispatchCall(CallExpression call, SandboxValue[] args, InterpreterFrame frame)
    {
        if (TryEvaluateCollectionCall(call, args, out var collectionValue))
        {
            return new ValueTask<SandboxValue>(collectionValue);
        }

        if (_interpreter.TryGetFunction(call.Name, out var function))
        {
            return _interpreter.InvokeFunctionAsync(function, LocalFunctionArguments.FromArray(args));
        }

        if (_context.Bindings.TryGetDescriptor(call.Name, out var descriptor))
        {
            return InterpreterBindingCaller.CallAsync(
                _context, _options, _moduleHash, descriptor, args, frame.FunctionId);
        }

        throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"unknown call '{call.Name}' at runtime"));
    }

    private bool TryEvaluateCollectionCall(
        CallExpression call,
        SandboxValue[] args,
        out SandboxValue value)
    {
        if (!CollectionCalls.TryGetValue(call.Name, out var handler))
        {
            value = SandboxValue.Unit;
            return false;
        }

        value = handler(this, call, args);
        return true;
    }

    private static SandboxValue Arg(SandboxValue[] args, int index)
        => index < args.Length
            ? args[index]
            : throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "call arity mismatch"));

}
