namespace SafeIR.Interpreter;

using SafeIR;

internal sealed class ExpressionEvaluator
{
    private readonly SandboxContext _context;
    private readonly InterpreterEvaluator _interpreter;
    private readonly IReadOnlyDictionary<string, FunctionAnalysis> _functionAnalysis;
    private readonly SandboxExecutionOptions _options;
    private readonly string _moduleHash;

    public ExpressionEvaluator(
        SandboxContext context,
        InterpreterEvaluator interpreter,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis,
        SandboxExecutionOptions options,
        string moduleHash)
    {
        _context = context;
        _interpreter = interpreter;
        _functionAnalysis = functionAnalysis;
        _options = options;
        _moduleHash = moduleHash;
    }

    // Non-async dispatch: literals, variables, arithmetic, and pure helper calls all
    // complete synchronously, so they return a finished ValueTask without ever
    // allocating an async state machine. Only genuinely asynchronous work (a host
    // binding whose ValueTask is still pending) walks the async continuation path.
    public ValueTask<SandboxValue> EvaluateAsync(Expression expression, InterpreterFrame frame)
    {
        _context.ChargeFuel(1);
        InterpreterTrace.Write(
            _context,
            _options,
            _moduleHash,
            frame.FunctionId,
            "expression",
            expression.GetType().Name,
            expression.Span);
        return expression switch
        {
            LiteralExpression literal => new ValueTask<SandboxValue>(ChargeLiteral(literal.Value)),
            VariableExpression variable => new ValueTask<SandboxValue>(frame.Read(variable.Name)),
            UnaryExpression unary => EvaluateUnary(unary, frame),
            BinaryExpression binary => EvaluateBinary(binary, frame),
            CallExpression call => EvaluateCall(call, frame),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported expression"))
        };
    }

    private ValueTask<SandboxValue> EvaluateUnary(UnaryExpression unary, InterpreterFrame frame)
    {
        var operand = EvaluateAsync(unary.Operand, frame);
        return operand.IsCompletedSuccessfully
            ? new ValueTask<SandboxValue>(ApplyUnary(unary, operand.Result))
            : AwaitUnary(unary, operand);
    }

    private async ValueTask<SandboxValue> AwaitUnary(UnaryExpression unary, ValueTask<SandboxValue> operand)
        => ApplyUnary(unary, await operand.ConfigureAwait(false));

    private SandboxValue ApplyUnary(UnaryExpression unary, SandboxValue value)
        => unary.Operator switch
        {
            "!" => SandboxValue.FromBool(!((BoolValue)value).Value),
            "-" => SandboxNumericOperations.Negate(value),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported unary operator"))
        };

    private ValueTask<SandboxValue> EvaluateBinary(BinaryExpression binary, InterpreterFrame frame)
    {
        if (binary.Operator is "&&" or "||")
        {
            return EvaluateShortCircuit(binary, frame);
        }

        var leftTask = EvaluateAsync(binary.Left, frame);
        if (!leftTask.IsCompletedSuccessfully)
        {
            return AwaitBinary(binary, leftTask, frame);
        }

        var rightTask = EvaluateAsync(binary.Right, frame);
        if (!rightTask.IsCompletedSuccessfully)
        {
            return AwaitBinaryRight(binary, leftTask.Result, rightTask);
        }

        return new ValueTask<SandboxValue>(ApplyBinary(binary, leftTask.Result, rightTask.Result));
    }

    private async ValueTask<SandboxValue> AwaitBinary(
        BinaryExpression binary,
        ValueTask<SandboxValue> leftTask,
        InterpreterFrame frame)
    {
        var left = await leftTask.ConfigureAwait(false);
        var right = await EvaluateAsync(binary.Right, frame).ConfigureAwait(false);
        return ApplyBinary(binary, left, right);
    }

    private async ValueTask<SandboxValue> AwaitBinaryRight(
        BinaryExpression binary,
        SandboxValue left,
        ValueTask<SandboxValue> rightTask)
        => ApplyBinary(binary, left, await rightTask.ConfigureAwait(false));

    private ValueTask<SandboxValue> EvaluateShortCircuit(BinaryExpression binary, InterpreterFrame frame)
    {
        var shortCircuitOn = binary.Operator == "||";
        var order = ShortCircuitExpressionOrder.Choose(binary, _context.Bindings, _functionAnalysis);
        var firstTask = EvaluateAsync(order.First, frame);
        if (!firstTask.IsCompletedSuccessfully)
        {
            return AwaitShortCircuit(order, shortCircuitOn, firstTask, frame);
        }

        if (((BoolValue)firstTask.Result).Value == shortCircuitOn)
        {
            return new ValueTask<SandboxValue>(SandboxValue.FromBool(shortCircuitOn));
        }

        var secondTask = EvaluateAsync(order.Second, frame);
        return secondTask.IsCompletedSuccessfully
            ? new ValueTask<SandboxValue>(SandboxValue.FromBool(((BoolValue)secondTask.Result).Value))
            : AwaitShortCircuitSecond(secondTask);
    }

    private async ValueTask<SandboxValue> AwaitShortCircuit(
        ShortCircuitOperands order,
        bool shortCircuitOn,
        ValueTask<SandboxValue> firstTask,
        InterpreterFrame frame)
    {
        var first = (BoolValue)await firstTask.ConfigureAwait(false);
        if (first.Value == shortCircuitOn)
        {
            return SandboxValue.FromBool(shortCircuitOn);
        }

        var second = (BoolValue)await EvaluateAsync(order.Second, frame).ConfigureAwait(false);
        return SandboxValue.FromBool(second.Value);
    }

    private static async ValueTask<SandboxValue> AwaitShortCircuitSecond(ValueTask<SandboxValue> secondTask)
        => SandboxValue.FromBool(((BoolValue)await secondTask.ConfigureAwait(false)).Value);

    private SandboxValue ApplyBinary(BinaryExpression binary, SandboxValue left, SandboxValue right)
        => binary.Operator switch
        {
            "+" when left is StringValue l && right is StringValue r => Concat(l.Value, r.Value),
            "+" => SandboxNumericOperations.Add(left, right),
            "-" => SandboxNumericOperations.Subtract(left, right),
            "*" => SandboxNumericOperations.Multiply(left, right),
            "/" => SandboxNumericOperations.Divide(left, right),
            "%" => SandboxNumericOperations.Remainder(left, right),
            "==" => SandboxValue.FromBool(Equals(left, right)),
            "!=" => SandboxValue.FromBool(!Equals(left, right)),
            "<" => SandboxNumericOperations.LessThan(left, right),
            "<=" => SandboxNumericOperations.LessThanOrEqual(left, right),
            ">" => SandboxNumericOperations.GreaterThan(left, right),
            ">=" => SandboxNumericOperations.GreaterThanOrEqual(left, right),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported binary operator"))
        };

    private ValueTask<SandboxValue> EvaluateCall(CallExpression call, InterpreterFrame frame)
    {
        var arguments = call.Arguments;
        var argCount = arguments.Count;
        // Evaluated arguments are passed to callees as a fixed read-only list, so
        // size the backing array exactly and reuse the shared empty array for the
        // common zero-argument call instead of allocating a growable List per call.
        var args = argCount == 0 ? System.Array.Empty<SandboxValue>() : new SandboxValue[argCount];
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
            return _interpreter.InvokeFunctionAsync(function, args);
        }

        if (_context.Bindings.Contains(call.Name))
        {
            return CallBindingAsync(call.Name, args, frame.FunctionId);
        }

        throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"unknown call '{call.Name}' at runtime"));
    }

    private bool TryEvaluateCollectionCall(
        CallExpression call,
        IReadOnlyList<SandboxValue> args,
        out SandboxValue value)
    {
        value = call.Name switch
        {
            "list.empty" => CollectionOperations.CreateList(call.GenericType ?? SandboxType.Unit, _context),
            "list.of" => CollectionOperations.BuildList(args, _context),
            "list.count" => CollectionOperations.CountList(Arg(args, 0), _context),
            "list.get" => CollectionOperations.GetListItem(Arg(args, 1), Arg(args, 0), _context),
            "list.add" => CollectionOperations.AddListItem(Arg(args, 1), Arg(args, 0), _context),
            "map.empty" => CollectionOperations.CreateMap(
                call.GenericType ?? SandboxType.Map(SandboxType.Unit, SandboxType.Unit),
                _context),
            "map.containsKey" => CollectionOperations.ContainsMapKey(Arg(args, 1), Arg(args, 0), _context),
            "map.get" => CollectionOperations.GetMapValue(Arg(args, 1), Arg(args, 0), _context),
            "map.set" => CollectionOperations.SetMapValue(Arg(args, 2), Arg(args, 1), Arg(args, 0), _context),
            "map.remove" => CollectionOperations.RemoveMapValue(Arg(args, 1), Arg(args, 0), _context),
            _ => SandboxValue.Unit
        };
        return call.Name is "list.empty" or "list.of" or "list.count" or "list.get" or "list.add"
            or "map.empty" or "map.containsKey" or "map.get" or "map.set" or "map.remove";
    }

    public ValueTask<SandboxValue> InvokeFunctionAsync(SandboxFunction function, IReadOnlyList<SandboxValue> args)
        => _interpreter.InvokeFunctionAsync(function, args);

    private async ValueTask<SandboxValue> CallBindingAsync(
        string id,
        IReadOnlyList<SandboxValue> args,
        string functionId)
    {
        var descriptor = _context.Bindings.GetDescriptor(id);
        InterpreterTrace.WriteBindingCall(_context, _options, _moduleHash, functionId, descriptor);
        var auditCheckpoint = _context.AuditCheckpoint();
        try
        {
            _context.ChargeBindingCall(descriptor);
        }
        catch (SandboxRuntimeException ex)
        {
            _context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, ex.Error.Code);
            throw;
        }

        CancellationTokenSource? timeout = null;
        try
        {
            timeout = _context.CreateWallTimeToken();
            using var returnCredits = _context.BeginBindingReturnCreditScope();
            var value = await descriptor.Invoke(_context, args, timeout.Token).ConfigureAwait(false);
            value = _context.ChargeBindingReturn(descriptor, value);
            _context.EnsureRequiredBindingSuccessAudit(descriptor, auditCheckpoint);
            return value;
        }
        catch (SandboxRuntimeException ex)
        {
            _context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) when (_context.CancellationToken.IsCancellationRequested)
        {
            _context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, SandboxErrorCode.Cancelled);
            throw;
        }
        catch (OperationCanceledException) when (timeout?.IsCancellationRequested == true)
        {
            var error = new SandboxError(SandboxErrorCode.Timeout, $"binding '{id}' timed out");
            _context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{id}' failed");
            _context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{id}' failed");
            _context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, error.Code);
            throw new SandboxRuntimeException(error);
        }
        finally
        {
            timeout?.Dispose();
        }
    }

    private SandboxValue Concat(string left, string right)
    {
        var text = _context.CreateChargedStringConcat(left, right);
        return SandboxValue.FromString(text);
    }

    private SandboxValue ChargeLiteral(SandboxValue value)
    {
        _context.ChargeValue(value);
        return value;
    }

    private static SandboxValue Arg(IReadOnlyList<SandboxValue> args, int index)
        => index < args.Count
            ? args[index]
            : throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "call arity mismatch"));
}
