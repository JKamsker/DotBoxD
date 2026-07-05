using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Loops;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal sealed partial class StatementExecutor
{
    private bool TryRunForLoopFastPath(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame)
        => MapGetI32ForLoopRunner.TryRun(statement, start, end, frame, _context, _options) ||
           ListGetI32ForLoopRunner.TryRun(statement, start, end, frame, _context, _options) ||
           ListCountForLoopRunner.TryRun(statement, start, end, frame, _context, _options) ||
           StringLengthForLoopRunner.TryRun(statement, start, end, frame, _context, _options) ||
           I32ForLoopRunner.TryRun(statement, start, end, frame, _context, _options, _calls) ||
           BranchedI32ForLoopRunner.TryRun(statement, start, end, frame, _context, _options, _calls) ||
           BranchedF64ForLoopRunner.TryRun(statement, start, end, frame, _context, _options, _calls) ||
           F64ForLoopRunner.TryRun(statement, start, end, frame, _context, _options) ||
           I64ForLoopRunner.TryRun(statement, start, end, frame, _context, _options);
}
