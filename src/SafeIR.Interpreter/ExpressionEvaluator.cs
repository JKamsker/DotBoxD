namespace SafeIR.Interpreter;

using SafeIR;

internal sealed class ExpressionEvaluator
{
    private readonly SandboxContext _context;
    private readonly InterpreterEvaluator _interpreter;

    public ExpressionEvaluator(SandboxContext context, InterpreterEvaluator interpreter)
    {
        _context = context;
        _interpreter = interpreter;
    }

    public async ValueTask<SandboxValue> EvaluateAsync(Expression expression, InterpreterFrame frame)
    {
        _context.ChargeFuel(1);
        return expression switch {
            LiteralExpression literal => literal.Value,
            VariableExpression variable => frame.Locals[variable.Name],
            UnaryExpression unary => EvaluateUnary(unary, frame),
            BinaryExpression binary => await EvaluateBinaryAsync(binary, frame).ConfigureAwait(false),
            CallExpression call => await EvaluateCallAsync(call, frame).ConfigureAwait(false),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported expression"))
        };
    }

    private SandboxValue EvaluateUnary(UnaryExpression unary, InterpreterFrame frame)
    {
        var value = EvaluateAsync(unary.Operand, frame).AsTask().GetAwaiter().GetResult();
        return unary.Operator switch {
            "!" => SandboxValue.FromBool(!((BoolValue)value).Value),
            "-" => SandboxValue.FromInt32(-((I32Value)value).Value),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported unary operator"))
        };
    }

    private async ValueTask<SandboxValue> EvaluateBinaryAsync(BinaryExpression binary, InterpreterFrame frame)
    {
        var left = await EvaluateAsync(binary.Left, frame).ConfigureAwait(false);
        var right = await EvaluateAsync(binary.Right, frame).ConfigureAwait(false);
        return binary.Operator switch {
            "+" when left is StringValue l && right is StringValue r => Concat(l.Value, r.Value),
            "+" => SandboxValue.FromInt32(((I32Value)left).Value + ((I32Value)right).Value),
            "-" => SandboxValue.FromInt32(((I32Value)left).Value - ((I32Value)right).Value),
            "*" => SandboxValue.FromInt32(((I32Value)left).Value * ((I32Value)right).Value),
            "/" => SandboxValue.FromInt32(((I32Value)left).Value / ((I32Value)right).Value),
            "%" => SandboxValue.FromInt32(((I32Value)left).Value % ((I32Value)right).Value),
            "==" => SandboxValue.FromBool(Equals(left, right)),
            "!=" => SandboxValue.FromBool(!Equals(left, right)),
            "<" => SandboxValue.FromBool(((I32Value)left).Value < ((I32Value)right).Value),
            "<=" => SandboxValue.FromBool(((I32Value)left).Value <= ((I32Value)right).Value),
            ">" => SandboxValue.FromBool(((I32Value)left).Value > ((I32Value)right).Value),
            ">=" => SandboxValue.FromBool(((I32Value)left).Value >= ((I32Value)right).Value),
            "&&" => SandboxValue.FromBool(((BoolValue)left).Value && ((BoolValue)right).Value),
            "||" => SandboxValue.FromBool(((BoolValue)left).Value || ((BoolValue)right).Value),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported binary operator"))
        };
    }

    private async ValueTask<SandboxValue> EvaluateCallAsync(CallExpression call, InterpreterFrame frame)
    {
        if (call.Name == "list.empty") {
            _context.ChargeAllocation(8);
            return SandboxValue.FromList([]);
        }

        var args = new List<SandboxValue>(call.Arguments.Count);
        foreach (var arg in call.Arguments) {
            args.Add(await EvaluateAsync(arg, frame).ConfigureAwait(false));
        }

        if (call.Name == "list.of") {
            _context.ChargeAllocation(args.Count * 16);
            return SandboxValue.FromList(args);
        }

        if (_context.Bindings.TryGet(call.Name, out _)) {
            return await CallBindingAsync(call.Name, args).ConfigureAwait(false);
        }

        if (_interpreter.TryGetFunction(call.Name, out var function)) {
            return await _interpreter.InvokeFunctionAsync(function, args).ConfigureAwait(false);
        }

        throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"unknown call '{call.Name}' at runtime"));
    }

    public async ValueTask<SandboxValue> InvokeFunctionAsync(SandboxFunction function, IReadOnlyList<SandboxValue> args)
        => await _interpreter.InvokeFunctionAsync(function, args).ConfigureAwait(false);

    private async ValueTask<SandboxValue> CallBindingAsync(string id, IReadOnlyList<SandboxValue> args)
    {
        var descriptor = _context.Bindings.GetDescriptor(id);
        if (descriptor.RequiredCapability is not null) {
            _context.RequireCapability(descriptor.RequiredCapability);
        }

        _context.Budget.ChargeHostCall(id);
        _context.ChargeFuel(descriptor.CostModel.BaseFuel);
        try {
            return await descriptor.Interpreter(_context, args, _context.CancellationToken).ConfigureAwait(false);
        }
        catch (SandboxRuntimeException) {
            throw;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{id}' failed"));
        }
    }

    private SandboxValue Concat(string left, string right)
    {
        var text = left + right;
        _context.ChargeAllocation(text.Length * sizeof(char));
        return SandboxValue.FromString(text);
    }
}
