using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Loops;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal readonly partial struct StatementExecutor
{
    private bool TryRunForLoopFastPath(
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
               statement, start, end, frame, Context, Options);
}
