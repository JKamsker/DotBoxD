namespace SafeIR.Interpreter;

using SafeIR;

internal sealed class BytecodeInterpreter
{
    private readonly ExecutionPlan _plan;
    private readonly SandboxContext _context;
    private readonly SandboxExecutionOptions _options;
    private int _callDepth;

    public BytecodeInterpreter(ExecutionPlan plan, SandboxContext context, SandboxExecutionOptions options)
    {
        _plan = plan;
        _context = context;
        _options = options;
    }

    public ValueTask<SandboxValue> ExecuteEntrypointAsync(string entrypoint, SandboxValue input)
    {
        if (!_plan.Bytecode.Functions.TryGetValue(entrypoint, out var function) || !function.IsEntrypoint) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"entrypoint '{entrypoint}' is not available"));
        }

        return InvokeFunctionAsync(function, BuildArguments(function, input));
    }

    private async ValueTask<SandboxValue> InvokeFunctionAsync(BytecodeFunction function, IReadOnlyList<SandboxValue> args)
    {
        if (++_callDepth > _context.Budget.Limits.MaxCallDepth) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.QuotaExceeded, "call depth exceeded"));
        }

        try {
            var frame = BytecodeFrame.Create(function, args);
            while (frame.InstructionPointer < function.Instructions.Count) {
                var instruction = function.Instructions[frame.InstructionPointer++];
                Trace(function, instruction);
                _context.ChargeFuel(1);
                var returned = await ExecuteInstructionAsync(frame, instruction).ConfigureAwait(false);
                if (returned is not null) {
                    return returned;
                }
            }

            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"function '{function.Id}' returned no value"));
        }
        finally {
            _callDepth--;
        }
    }

    private async ValueTask<SandboxValue?> ExecuteInstructionAsync(BytecodeFrame frame, BytecodeInstruction instruction)
    {
        switch (instruction.Op) {
            case BytecodeOp.LoadConst:
                frame.Push((SandboxValue)instruction.Operand!);
                break;
            case BytecodeOp.LoadLocal:
                frame.Push(frame.Locals[(string)instruction.Operand!]);
                break;
            case BytecodeOp.StoreLocal:
                frame.Locals[(string)instruction.Operand!] = frame.Pop();
                break;
            case BytecodeOp.Pop:
                _ = frame.Pop();
                break;
            case BytecodeOp.Unary:
                frame.Push(EvaluateUnary((string)instruction.Operand!, frame.Pop()));
                break;
            case BytecodeOp.Binary:
                frame.Push(EvaluateBinary((string)instruction.Operand!, frame.Pop(), frame.Pop()));
                break;
            case BytecodeOp.Jump:
                Jump(frame, (int)instruction.Operand!);
                break;
            case BytecodeOp.JumpIfFalse:
                if (!((BoolValue)frame.Pop()).Value) {
                    Jump(frame, (int)instruction.Operand!);
                }

                break;
            case BytecodeOp.CallBinding:
                frame.Push(await CallBindingAsync((BytecodeCall)instruction.Operand!, frame).ConfigureAwait(false));
                break;
            case BytecodeOp.CallFunction:
                frame.Push(await CallFunctionAsync((BytecodeCall)instruction.Operand!, frame).ConfigureAwait(false));
                break;
            case BytecodeOp.ListEmpty:
                _context.ChargeAllocation(8);
                frame.Push(SandboxValue.FromList([]));
                break;
            case BytecodeOp.ListOf:
                frame.Push(BuildList((int)instruction.Operand!, frame));
                break;
            case BytecodeOp.Return:
                return frame.Pop();
        }

        return null;
    }

    private SandboxValue EvaluateUnary(string op, SandboxValue value)
        => op switch {
            "!" => SandboxValue.FromBool(!((BoolValue)value).Value),
            "-" => SandboxValue.FromInt32(-((I32Value)value).Value),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported unary operator"))
        };

    private SandboxValue EvaluateBinary(string op, SandboxValue right, SandboxValue left)
        => op switch {
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

    private async ValueTask<SandboxValue> CallFunctionAsync(BytecodeCall call, BytecodeFrame frame)
    {
        if (!_plan.Bytecode.Functions.TryGetValue(call.Id, out var function)) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"unknown function '{call.Id}'"));
        }

        return await InvokeFunctionAsync(function, PopArguments(frame, call.ArgumentCount)).ConfigureAwait(false);
    }

    private async ValueTask<SandboxValue> CallBindingAsync(BytecodeCall call, BytecodeFrame frame)
    {
        var descriptor = _context.Bindings.GetDescriptor(call.Id);
        if (descriptor.RequiredCapability is not null) {
            _context.RequireCapability(descriptor.RequiredCapability);
        }

        _context.Budget.ChargeHostCall(call.Id);
        _context.ChargeFuel(descriptor.CostModel.BaseFuel);
        try {
            return await descriptor.Interpreter(_context, PopArguments(frame, call.ArgumentCount), _context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (SandboxRuntimeException) {
            throw;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{call.Id}' failed"));
        }
    }

    private SandboxValue BuildList(int count, BytecodeFrame frame)
    {
        var values = PopArguments(frame, count);
        _context.ChargeAllocation(values.Count * 16);
        return SandboxValue.FromList(values);
    }

    private static IReadOnlyList<SandboxValue> PopArguments(BytecodeFrame frame, int count)
    {
        var values = new SandboxValue[count];
        for (var i = count - 1; i >= 0; i--) {
            values[i] = frame.Pop();
        }

        return values;
    }

    private static IReadOnlyList<SandboxValue> BuildArguments(BytecodeFunction function, SandboxValue input)
    {
        if (function.Parameters.Count == 0) {
            return [];
        }

        if (function.Parameters.Count == 1) {
            return [input];
        }

        if (input is ListValue list && list.Values.Count == function.Parameters.Count) {
            return list.Values;
        }

        throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.InvalidInput, "entrypoint input argument mismatch"));
    }

    private void Jump(BytecodeFrame frame, int target)
    {
        if (target < frame.InstructionPointer) {
            _context.ChargeFuel(5);
        }

        frame.InstructionPointer = target;
    }

    private SandboxValue Concat(string left, string right)
    {
        var text = left + right;
        _context.ChargeAllocation(text.Length * sizeof(char));
        return SandboxValue.FromString(text);
    }

    private void Trace(BytecodeFunction function, BytecodeInstruction instruction)
    {
        if (!_options.EnableDebugTrace) {
            return;
        }

        _context.Audit.Write(new SandboxAuditEvent(
            _context.RunId,
            "DebugTrace",
            DateTimeOffset.UtcNow,
            true,
            Message: $"{function.Id}:{instruction.Op} fuelRemaining={_context.Budget.Limits.MaxFuel - _context.Budget.FuelUsed}"));
    }
}
