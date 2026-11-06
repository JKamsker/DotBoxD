using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Loops;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal readonly partial struct StatementExecutor
{
    private bool TryRunForLoopFastPath(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame)
        => IsRawInt32LoopSlot(statement, frame) &&
           TryRunRawInt32LoopFastPath(statement, start, end, frame);

    private bool TryRunRawInt32LoopFastPath(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame)
        => MapGetI32ForLoopRunner.TryRun(
               statement, start, end, frame, Context, Options) ||
           ListGetI32ForLoopRunner.TryRun(
               statement, start, end, frame, Context, Options) ||
           ListCountForLoopRunner.TryRun(
               statement, start, end, frame, Context, Options) ||
           StringLengthForLoopRunner.TryRun(
               statement, start, end, frame, Context, Options) ||
           I32ForLoopRunner.TryRun(
               statement, start, end, frame, Context, Options, _interpreter) ||
           BranchedI32ForLoopRunner.TryRun(
               statement, start, end, frame, Context, Options, _interpreter) ||
           BranchedF64ForLoopRunner.TryRun(
               statement, start, end, frame, Context, Options, _interpreter) ||
           F64ForLoopRunner.TryRun(
               statement, start, end, frame, Context, Options) ||
           I64ForLoopRunner.TryRun(
               statement, start, end, frame, Context, Options) ||
           TryRunNestedForLoopFastPath(
               statement, start, end, frame, Context, Options);

    // Every fast path writes the loop variable through the raw i32 slot array. A loop variable
    // whose name is also assigned a non-i32 value elsewhere in the function lives in a boxed slot,
    // so only the generic loop may write it.
    private static bool IsRawInt32LoopSlot(ForRangeStatement statement, InterpreterFrame frame)
        => frame.IsInt32Slot(frame.GetSlot(statement.LocalName));

    private static bool TryRunNestedForLoopFastPath(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context,
        SandboxExecutionOptions options)
        => NestedI32ForLoopRunner.TryRun(
               statement, start, end, frame, context, options) ||
           NestedF64ForLoopRunner.TryRun(
               statement, start, end, frame, context, options);
}
