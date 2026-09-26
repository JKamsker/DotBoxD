using DotBoxD.Cli.Infrastructure;
using DotBoxD.Kernels;
using DotBoxD.Plugins.Replay;

namespace DotBoxD.Cli.Commands;

internal static class ReplayCommand
{
    public static async Task<CommandResult> RunAsync(string path, ExecutionMode mode, CancellationToken cancellationToken)
    {
        var trace = ExecutionTrace.Deserialize(await InputFile.ReadAsync(path, cancellationToken).ConfigureAwait(false));
        var replay = await ExecutionReplay.RunAsync(trace, mode, cancellationToken).ConfigureAwait(false);
        var text = replay.Matches
            ? $"Replay matched: {trace.Entrypoint}; backend {replay.Execution.ActualMode}; {trace.Calls.Count} recorded binding calls."
            : $"Replay diverged: {replay.Difference}";
        return new CommandResult(replay.Matches, new
        {
            replay.Matches,
            backend = replay.Execution.ActualMode.ToString(),
            replay.Difference,
            bindingCalls = trace.Calls.Count,
            errorCode = replay.Execution.Error?.Code.ToString()
        }, text);
    }
}
