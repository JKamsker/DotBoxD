using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Debugging;

internal sealed class InterpreterDebugRuntime
{
    public InterpreterDebugRuntime(
        ISandboxExecutionDebugHook hook,
        SandboxNodeMap nodeMap,
        SandboxContext context,
        StatementExecutor statements,
        Func<SandboxFunction, FunctionFrameLayout> getLayout)
    {
        State = new InterpreterDebugState(hook, nodeMap, context);
        Statements = new InterpreterDebugStatementExecutor(
            State,
            context,
            statements.ExecuteStatementCore,
            statements.ExecuteBlockAsync);
        Functions = new InterpreterDebugFunctionExecutor(context, statements, State, getLayout);
    }

    public InterpreterDebugState State { get; }

    public InterpreterDebugStatementExecutor Statements { get; }

    public InterpreterDebugFunctionExecutor Functions { get; }
}
