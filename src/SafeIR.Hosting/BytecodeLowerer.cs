namespace SafeIR.Hosting;

using SafeIR;

internal static class BytecodeLowerer
{
    public static ExecutableBytecode Lower(SandboxModule module, BindingRegistry bindings)
    {
        var functionIds = module.Functions.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        var functions = module.Functions.ToDictionary(
            f => f.Id,
            f => LowerFunction(f, bindings, functionIds),
            StringComparer.Ordinal);
        return new ExecutableBytecode(functions);
    }

    private static BytecodeFunction LowerFunction(
        SandboxFunction function,
        BindingRegistry bindings,
        IReadOnlySet<string> functionIds)
    {
        var builder = new FunctionBytecodeBuilder(bindings, functionIds);
        foreach (var statement in function.Body) {
            builder.EmitStatement(statement);
        }

        return new BytecodeFunction(
            function.Id,
            function.IsEntrypoint,
            function.Parameters,
            function.ReturnType,
            builder.Instructions);
    }
}

internal sealed class FunctionBytecodeBuilder
{
    private readonly BindingRegistry _bindings;
    private readonly IReadOnlySet<string> _functionIds;
    private readonly List<BytecodeInstruction> _instructions = [];
    private int _hiddenCounter;

    public FunctionBytecodeBuilder(BindingRegistry bindings, IReadOnlySet<string> functionIds)
    {
        _bindings = bindings;
        _functionIds = functionIds;
    }

    public IReadOnlyList<BytecodeInstruction> Instructions => _instructions;

    public void EmitStatement(Statement statement)
    {
        switch (statement) {
            case AssignmentStatement assignment:
                EmitExpression(assignment.Value);
                Emit(BytecodeOp.StoreLocal, assignment.Name);
                break;
            case ReturnStatement ret:
                EmitExpression(ret.Value);
                Emit(BytecodeOp.Return);
                break;
            case ExpressionStatement expr:
                EmitExpression(expr.Value);
                Emit(BytecodeOp.Pop);
                break;
            case IfStatement branch:
                EmitIf(branch);
                break;
            case WhileStatement loop:
                EmitWhile(loop);
                break;
            case ForRangeStatement range:
                EmitForRange(range);
                break;
        }
    }

    public void EmitExpression(Expression expression)
    {
        switch (expression) {
            case LiteralExpression literal:
                Emit(BytecodeOp.LoadConst, literal.Value);
                break;
            case VariableExpression variable:
                Emit(BytecodeOp.LoadLocal, variable.Name);
                break;
            case UnaryExpression unary:
                EmitExpression(unary.Operand);
                Emit(BytecodeOp.Unary, unary.Operator);
                break;
            case BinaryExpression binary:
                EmitExpression(binary.Left);
                EmitExpression(binary.Right);
                Emit(BytecodeOp.Binary, binary.Operator);
                break;
            case CallExpression call:
                EmitCall(call);
                break;
        }
    }

    private void EmitIf(IfStatement branch)
    {
        EmitExpression(branch.Condition);
        var jumpToElse = Emit(BytecodeOp.JumpIfFalse, -1);
        branch.Then.ToList().ForEach(EmitStatement);
        var jumpToEnd = Emit(BytecodeOp.Jump, -1);
        Patch(jumpToElse, _instructions.Count);
        branch.Else.ToList().ForEach(EmitStatement);
        Patch(jumpToEnd, _instructions.Count);
    }

    private void EmitWhile(WhileStatement loop)
    {
        var start = _instructions.Count;
        EmitExpression(loop.Condition);
        var jumpToEnd = Emit(BytecodeOp.JumpIfFalse, -1);
        loop.Body.ToList().ForEach(EmitStatement);
        Emit(BytecodeOp.Jump, start);
        Patch(jumpToEnd, _instructions.Count);
    }

    private void EmitForRange(ForRangeStatement range)
    {
        var endLocal = $"$forEnd{_hiddenCounter++}_{range.LocalName}";
        EmitExpression(range.Start);
        Emit(BytecodeOp.StoreLocal, range.LocalName);
        EmitExpression(range.End);
        Emit(BytecodeOp.StoreLocal, endLocal);

        var start = _instructions.Count;
        Emit(BytecodeOp.LoadLocal, range.LocalName);
        Emit(BytecodeOp.LoadLocal, endLocal);
        Emit(BytecodeOp.Binary, "<");
        var jumpToEnd = Emit(BytecodeOp.JumpIfFalse, -1);
        range.Body.ToList().ForEach(EmitStatement);
        Emit(BytecodeOp.LoadLocal, range.LocalName);
        Emit(BytecodeOp.LoadConst, SandboxValue.FromInt32(1));
        Emit(BytecodeOp.Binary, "+");
        Emit(BytecodeOp.StoreLocal, range.LocalName);
        Emit(BytecodeOp.Jump, start);
        Patch(jumpToEnd, _instructions.Count);
    }

    private void EmitCall(CallExpression call)
    {
        foreach (var argument in call.Arguments) {
            EmitExpression(argument);
        }

        if (call.Name == "list.empty") {
            Emit(BytecodeOp.ListEmpty, call.GenericType ?? SandboxType.Unit);
        }
        else if (call.Name == "list.of") {
            Emit(BytecodeOp.ListOf, call.Arguments.Count);
        }
        else if (_bindings.TryGet(call.Name, out _)) {
            Emit(BytecodeOp.CallBinding, new BytecodeCall(call.Name, call.Arguments.Count));
        }
        else if (_functionIds.Contains(call.Name)) {
            Emit(BytecodeOp.CallFunction, new BytecodeCall(call.Name, call.Arguments.Count));
        }
    }

    private int Emit(BytecodeOp op, object? operand = null)
    {
        _instructions.Add(new BytecodeInstruction(op, operand));
        return _instructions.Count - 1;
    }

    private void Patch(int index, int target)
        => _instructions[index] = _instructions[index] with { Operand = target };
}
