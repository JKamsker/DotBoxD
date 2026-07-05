using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Loops;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal sealed partial class StatementExecutor
{
    private static readonly ForLoopFastPathRunner[] ForLoopFastPathRunners =
    [
        static (executor, statement, start, end, frame) =>
            MapGetI32ForLoopRunner.TryRun(statement, start, end, frame, executor._context, executor._options),
        static (executor, statement, start, end, frame) =>
            ListGetI32ForLoopRunner.TryRun(statement, start, end, frame, executor._context, executor._options),
        static (executor, statement, start, end, frame) =>
            ListCountForLoopRunner.TryRun(statement, start, end, frame, executor._context, executor._options),
        static (executor, statement, start, end, frame) =>
            StringLengthForLoopRunner.TryRun(statement, start, end, frame, executor._context, executor._options),
        static (executor, statement, start, end, frame) =>
            I32ForLoopRunner.TryRun(statement, start, end, frame, executor._context, executor._options, executor._calls),
        static (executor, statement, start, end, frame) =>
            BranchedI32ForLoopRunner.TryRun(statement, start, end, frame, executor._context, executor._options, executor._calls),
        static (executor, statement, start, end, frame) =>
            BranchedF64ForLoopRunner.TryRun(statement, start, end, frame, executor._context, executor._options, executor._calls),
        static (executor, statement, start, end, frame) =>
            F64ForLoopRunner.TryRun(statement, start, end, frame, executor._context, executor._options),
        static (executor, statement, start, end, frame) =>
            I64ForLoopRunner.TryRun(statement, start, end, frame, executor._context, executor._options)
    ];

    private bool TryRunForLoopFastPath(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame)
    {
        foreach (var runner in ForLoopFastPathRunners)
        {
            if (runner(this, statement, start, end, frame))
            {
                return true;
            }
        }

        return false;
    }

    private delegate bool ForLoopFastPathRunner(
        StatementExecutor executor,
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame);
}
