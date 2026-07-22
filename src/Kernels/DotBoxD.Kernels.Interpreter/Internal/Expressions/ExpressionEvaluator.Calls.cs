using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

internal readonly partial struct ExpressionEvaluator
{
    private delegate SandboxValue CollectionCallHandler(
        ExpressionEvaluator evaluator,
        CallExpression call,
        SandboxValue[] args);

    private static readonly Dictionary<string, CollectionCallHandler> CollectionCalls = new(StringComparer.Ordinal)
    {
        ["list.empty"] = static (evaluator, call, _) =>
            CollectionOperations.CreateList(call.GenericType ?? SandboxType.Unit, evaluator.Context),
        ["list.of"] = static (evaluator, _, args) => CollectionOperations.BuildList(args, evaluator.Context),
        ["list.count"] = static (evaluator, _, args) => CollectionOperations.CountList(Arg(args, 0), evaluator.Context),
        ["list.get"] = static (evaluator, _, args) => CollectionOperations.GetListItem(Arg(args, 1), Arg(args, 0), evaluator.Context),
        ["list.add"] = static (evaluator, _, args) => CollectionOperations.AddListItem(Arg(args, 1), Arg(args, 0), evaluator.Context),
        ["map.empty"] = static (evaluator, call, _) => CollectionOperations.CreateMap(
            call.GenericType ?? SandboxType.Map(SandboxType.Unit, SandboxType.Unit),
            evaluator.Context),
        ["map.containsKey"] = static (evaluator, _, args) => CollectionOperations.ContainsMapKey(Arg(args, 1), Arg(args, 0), evaluator.Context),
        ["map.get"] = static (evaluator, _, args) => CollectionOperations.GetMapValue(Arg(args, 1), Arg(args, 0), evaluator.Context),
        ["map.set"] = static (evaluator, _, args) => CollectionOperations.SetMapValue(Arg(args, 2), Arg(args, 1), Arg(args, 0), evaluator.Context),
        ["map.remove"] = static (evaluator, _, args) => CollectionOperations.RemoveMapValue(Arg(args, 1), Arg(args, 0), evaluator.Context),
        ["record.new"] = static (evaluator, _, args) => CollectionOperations.BuildRecord(args, evaluator.Context),
        ["record.get"] = static (evaluator, _, args) => CollectionOperations.GetRecordField(Arg(args, 1), Arg(args, 0), evaluator.Context),
        ["numeric.toI64"] = static (_, _, args) => NumericToInt64(Arg(args, 0)),
        ["numeric.toF64"] = static (_, _, args) => NumericToDouble(Arg(args, 0)),
    };

    private ValueTask<SandboxValue> EvaluateCall(CallExpression call, InterpreterFrame frame)
    {
        if (UnaryPureIntrinsicDispatcher.IsCandidate(call.Name) &&
            UnaryPureIntrinsicDispatcher.TryEvaluate(
                call, this, frame, Context, Options, ModuleHash, frame.FunctionId, out var mathValue))
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

        if (CollectionCalls.ContainsKey(call.Name))
        {
            return EvaluateCallViaArray(call, frame);
        }

        if (_interpreter.TryGetFunction(call.Name, out var function))
        {
            return EvaluateLocalFunctionCall(call, function, frame);
        }

        if (Context.Bindings.TryGetDescriptor(call.Name, out var descriptor))
        {
            return EvaluateBindingCall(call, descriptor, frame);
        }

        return EvaluateCallViaArray(call, frame);
    }

    private ValueTask<SandboxValue> EvaluateLocalFunctionCall(
        CallExpression call, SandboxFunction function, InterpreterFrame frame) =>
        call.Arguments.Count is 1 or 2 or 3 && function.Parameters.Count == call.Arguments.Count
            ? LocalFunctionCallEvaluator.Evaluate(this, call, function, frame)
            : EvaluateCallViaArray(call, frame);

    private ValueTask<SandboxValue> EvaluateBindingCall(
        CallExpression call, BindingDescriptor descriptor, InterpreterFrame frame) =>
        CanUseScalarBinding(call, descriptor)
            ? BindingCallEvaluator.Evaluate(this, call, descriptor, frame)
            : EvaluateCallViaArray(call, frame, descriptor);

    private ValueTask<SandboxValue> EvaluateNumericConversion(
        string name,
        Expression operandExpression,
        InterpreterFrame frame)
    {
        var operand = EvaluateAsync(operandExpression, frame);
        return operand.IsCompletedSuccessfully
            ? new ValueTask<SandboxValue>(ConvertNumeric(name, operand.Result))
            : AwaitNumericConversion(name, operand);
    }

    private static async ValueTask<SandboxValue> AwaitNumericConversion(
        string name,
        ValueTask<SandboxValue> operand)
        => ConvertNumeric(name, await operand.ConfigureAwait(false));

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
            CollectionIntrinsicDispatcher.Dispatch(call, arg0, arg1, arg2, Context));
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

        return CollectionIntrinsicDispatcher.Dispatch(call, arg0, arg1, arg2, Context);
    }

    private static bool CanUseScalarBinding(CallExpression call, BindingDescriptor descriptor)
        => descriptor.Parameters.Count == call.Arguments.Count &&
           ((call.Arguments.Count == 1 && descriptor.Invoke.Target is IOneArgumentBindingInvoker) ||
            (call.Arguments.Count == 2 && descriptor.Invoke.Target is ITwoArgumentBindingInvoker) ||
            (call.Arguments.Count == 3 && descriptor.Invoke.Target is IThreeArgumentBindingInvoker));

    private ValueTask<SandboxValue> EvaluateCallViaArray(
        CallExpression call,
        InterpreterFrame frame,
        BindingDescriptor? resolvedBinding = null)
    {
        var arguments = call.Arguments;
        var argCount = arguments.Count;
        var args = argCount == 0 ? Array.Empty<SandboxValue>() : new SandboxValue[argCount];
        for (var i = 0; i < argCount; i++)
        {
            var argTask = EvaluateAsync(arguments[i], frame);
            if (!argTask.IsCompletedSuccessfully)
            {
                return AwaitCallArguments(call, args, i, argTask, frame, resolvedBinding);
            }

            args[i] = argTask.Result;
        }

        return DispatchCall(call, args, frame, resolvedBinding);
    }

    private async ValueTask<SandboxValue> AwaitCallArguments(
        CallExpression call,
        SandboxValue[] args,
        int pending,
        ValueTask<SandboxValue> pendingTask,
        InterpreterFrame frame,
        BindingDescriptor? resolvedBinding)
    {
        args[pending] = await pendingTask.ConfigureAwait(false);
        for (var i = pending + 1; i < args.Length; i++)
        {
            args[i] = await EvaluateAsync(call.Arguments[i], frame).ConfigureAwait(false);
        }

        return await DispatchCall(call, args, frame, resolvedBinding).ConfigureAwait(false);
    }

    private ValueTask<SandboxValue> DispatchCall(
        CallExpression call,
        SandboxValue[] args,
        InterpreterFrame frame,
        BindingDescriptor? resolvedBinding)
    {
        if (TryEvaluateCollectionCall(call, args, out var collectionValue))
        {
            return new ValueTask<SandboxValue>(collectionValue);
        }

        if (_interpreter.TryGetFunction(call.Name, out var function))
        {
            return _interpreter.InvokeFunctionAsync(function, LocalFunctionArguments.FromArray(args));
        }

        if (resolvedBinding is not null)
        {
            return InterpreterBindingCaller.CallAsync(
                Context, Options, ModuleHash, resolvedBinding, args, frame.FunctionId);
        }

        if (Context.Bindings.TryGetDescriptor(call.Name, out var descriptor))
        {
            return InterpreterBindingCaller.CallAsync(
                Context, Options, ModuleHash, descriptor, args, frame.FunctionId);
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

    private static SandboxValue NumericToInt64(SandboxValue value)
        => value switch
        {
            I32Value number => SandboxValue.FromInt64(number.Value),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "expected I32 value"))
        };

    private static SandboxValue NumericToDouble(SandboxValue value)
        => value switch
        {
            I32Value number => SandboxValue.FromDouble(number.Value),
            I64Value number => SandboxValue.FromDouble(number.Value),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "expected I32 or I64 value"))
        };

    private static SandboxValue ConvertNumeric(string name, SandboxValue value)
        => name switch
        {
            "numeric.toI64" => NumericToInt64(value),
            "numeric.toF64" => NumericToDouble(value),
            _ => throw new InvalidOperationException("unsupported numeric conversion")
        };

    private static bool IsNumericConversion(string name)
        => name is "numeric.toI64" or "numeric.toF64";
}
