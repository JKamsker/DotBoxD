namespace SafeIR.Compiler;

using System.Reflection;
using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;

internal sealed class MethodEmitter
{
    private readonly ILGenerator _il;
    private readonly SandboxFunction _function;
    private readonly Dictionary<string, LocalBuilder> _locals = new(StringComparer.Ordinal);

    public MethodEmitter(ILGenerator il, SandboxFunction function)
    {
        _il = il;
        _function = function;
    }

    public void Emit()
    {
        EmitParameters();
        foreach (var statement in _function.Body) {
            EmitStatement(statement);
        }

        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Ret);
    }

    private void EmitParameters()
    {
        for (var i = 0; i < _function.Parameters.Count; i++) {
            var local = Declare(_function.Parameters[i].Name);
            _il.Emit(OpCodes.Ldarg_1);
            EmitInt(i);
            _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.GetInputArgument)));
            _il.Emit(OpCodes.Stloc, local);
        }
    }

    private void EmitStatement(Statement statement)
    {
        EmitFuel(1);
        switch (statement) {
            case AssignmentStatement assignment:
                EmitExpression(assignment.Value);
                _il.Emit(OpCodes.Stloc, Declare(assignment.Name));
                break;
            case ReturnStatement ret:
                EmitExpression(ret.Value);
                _il.Emit(OpCodes.Ret);
                break;
            case IfStatement branch:
                EmitIf(branch);
                break;
            case ForRangeStatement range:
                EmitForRange(range);
                break;
            case WhileStatement loop:
                EmitWhile(loop);
                break;
            default:
                EmitUnsupported("statement not supported");
                break;
        }
    }

    private void EmitIf(IfStatement branch)
    {
        var elseLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        EmitExpression(branch.Condition);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AsBool)));
        _il.Emit(OpCodes.Brfalse, elseLabel);
        branch.Then.ToList().ForEach(EmitStatement);
        _il.Emit(OpCodes.Br, endLabel);
        _il.MarkLabel(elseLabel);
        branch.Else.ToList().ForEach(EmitStatement);
        _il.MarkLabel(endLabel);
    }

    private void EmitForRange(ForRangeStatement range)
    {
        var index = _il.DeclareLocal(typeof(int));
        var end = _il.DeclareLocal(typeof(int));
        EmitExpression(range.Start);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AsI32)));
        _il.Emit(OpCodes.Stloc, index);
        EmitExpression(range.End);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AsI32)));
        _il.Emit(OpCodes.Stloc, end);

        var startLabel = _il.DefineLabel();
        var finishLabel = _il.DefineLabel();
        _il.MarkLabel(startLabel);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Ldloc, end);
        _il.Emit(OpCodes.Bge, finishLabel);
        EmitFuel(5);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.I32)));
        _il.Emit(OpCodes.Stloc, Declare(range.LocalName));
        range.Body.ToList().ForEach(EmitStatement);
        _il.Emit(OpCodes.Ldloc, index);
        EmitInt(1);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stloc, index);
        _il.Emit(OpCodes.Br, startLabel);
        _il.MarkLabel(finishLabel);
    }

    private void EmitWhile(WhileStatement loop)
    {
        var startLabel = _il.DefineLabel();
        var finishLabel = _il.DefineLabel();
        _il.MarkLabel(startLabel);
        EmitExpression(loop.Condition);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AsBool)));
        _il.Emit(OpCodes.Brfalse, finishLabel);
        EmitFuel(5);
        loop.Body.ToList().ForEach(EmitStatement);
        _il.Emit(OpCodes.Br, startLabel);
        _il.MarkLabel(finishLabel);
    }

    private void EmitExpression(Expression expression)
    {
        switch (expression) {
            case LiteralExpression literal:
                EmitLiteral(literal.Value);
                break;
            case VariableExpression variable:
                _il.Emit(OpCodes.Ldloc, _locals[variable.Name]);
                break;
            case UnaryExpression unary:
                EmitUnary(unary);
                break;
            case BinaryExpression binary:
                EmitBinary(binary);
                break;
            case CallExpression call:
                EmitPureCall(call);
                break;
            default:
                EmitUnsupported("expression not supported");
                break;
        }
    }

    private void EmitLiteral(SandboxValue value)
    {
        switch (value) {
            case I32Value i32:
                EmitInt(i32.Value);
                _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.I32)));
                break;
            case BoolValue boolean:
                EmitInt(boolean.Value ? 1 : 0);
                _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.Bool)));
                break;
            case StringValue text:
                _il.Emit(OpCodes.Ldstr, text.Value);
                _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.String)));
                break;
            default:
                EmitUnsupported("literal not supported by compiler");
                break;
        }
    }

    private void EmitUnary(UnaryExpression unary)
    {
        EmitExpression(unary.Operand);
        _il.Emit(OpCodes.Call, Runtime(unary.Operator == "!" ? nameof(CompiledRuntime.NotBool) : nameof(CompiledRuntime.NegI32)));
    }

    private void EmitBinary(BinaryExpression binary)
    {
        EmitExpression(binary.Left);
        EmitExpression(binary.Right);
        var method = binary.Operator switch {
            "+" => nameof(CompiledRuntime.AddI32),
            "-" => nameof(CompiledRuntime.SubI32),
            "*" => nameof(CompiledRuntime.MulI32),
            "/" => nameof(CompiledRuntime.DivI32),
            "%" => nameof(CompiledRuntime.RemI32),
            "==" => nameof(CompiledRuntime.Eq),
            "!=" => nameof(CompiledRuntime.Ne),
            "<" => nameof(CompiledRuntime.LtI32),
            "<=" => nameof(CompiledRuntime.LteI32),
            ">" => nameof(CompiledRuntime.GtI32),
            ">=" => nameof(CompiledRuntime.GteI32),
            "&&" => nameof(CompiledRuntime.And),
            "||" => nameof(CompiledRuntime.Or),
            _ => throw Unsupported("operator not supported by compiler")
        };
        _il.Emit(OpCodes.Call, Runtime(method));
    }

    private void EmitPureCall(CallExpression call)
    {
        if (call.Name == "string.length") {
            EmitExpression(call.Arguments[0]);
            _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.StringLength)));
            return;
        }

        if (call.Name == "string.concatBudgeted") {
            _il.Emit(OpCodes.Ldarg_0);
            EmitExpression(call.Arguments[0]);
            EmitExpression(call.Arguments[1]);
            _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ConcatString)));
            return;
        }

        EmitUnsupported($"call '{call.Name}' is not supported by compiler");
    }

    private LocalBuilder Declare(string name)
    {
        if (_locals.TryGetValue(name, out var existing)) {
            return existing;
        }

        var local = _il.DeclareLocal(typeof(SandboxValue));
        _locals[name] = local;
        return local;
    }

    private void EmitFuel(int amount)
    {
        _il.Emit(OpCodes.Ldarg_0);
        EmitInt(amount);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeFuel)));
    }

    private void EmitUnsupported(string message) => throw Unsupported(message);

    private static Exception Unsupported(string message)
        => new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, message));

    private void EmitInt(int value)
    {
        switch (value) {
            case -1:
                _il.Emit(OpCodes.Ldc_I4_M1);
                break;
            case 0:
                _il.Emit(OpCodes.Ldc_I4_0);
                break;
            case 1:
                _il.Emit(OpCodes.Ldc_I4_1);
                break;
            case 2:
                _il.Emit(OpCodes.Ldc_I4_2);
                break;
            case 3:
                _il.Emit(OpCodes.Ldc_I4_3);
                break;
            case 4:
                _il.Emit(OpCodes.Ldc_I4_4);
                break;
            case 5:
                _il.Emit(OpCodes.Ldc_I4_5);
                break;
            case 6:
                _il.Emit(OpCodes.Ldc_I4_6);
                break;
            case 7:
                _il.Emit(OpCodes.Ldc_I4_7);
                break;
            case 8:
                _il.Emit(OpCodes.Ldc_I4_8);
                break;
            case >= sbyte.MinValue and <= sbyte.MaxValue:
                _il.Emit(OpCodes.Ldc_I4_S, (sbyte)value);
                break;
            default:
                _il.Emit(OpCodes.Ldc_I4, value);
                break;
        }
    }

    private static MethodInfo Runtime(string name)
        => typeof(CompiledRuntime).GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == name);
}
