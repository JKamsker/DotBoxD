namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

internal static class ForLoopFastPathRunner
{
    public static bool TryRun(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context,
        SandboxExecutionOptions options,
        I32CallEvaluator calls)
        => MapGetI32ForLoopRunner.TryRun(statement, start, end, frame, context, options) ||
           ListGetI32ForLoopRunner.TryRun(statement, start, end, frame, context, options) ||
           ListCountForLoopRunner.TryRun(statement, start, end, frame, context, options) ||
           StringLengthForLoopRunner.TryRun(statement, start, end, frame, context, options) ||
           I32RepeatedAddCallForLoopRunner.TryRun(statement, start, end, frame, context, options, calls) ||
           I32ForLoopRunner.TryRun(statement, start, end, frame, context, options, calls) ||
           BranchedI32ForLoopRunner.TryRun(statement, start, end, frame, context, options, calls) ||
           BranchedF64ForLoopRunner.TryRun(statement, start, end, frame, context, options, calls) ||
           F64ForLoopRunner.TryRun(statement, start, end, frame, context, options) ||
           I64ForLoopRunner.TryRun(statement, start, end, frame, context, options);
}
