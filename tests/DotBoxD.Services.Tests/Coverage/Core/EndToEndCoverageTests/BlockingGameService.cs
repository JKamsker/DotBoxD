using Shared;

namespace DotBoxD.Services.Tests.Coverage.Core;

internal sealed class BlockingGameService : IGameService
{
    public TaskCompletionSource Entered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Canceled { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<ServerStatus> GetServerStatusAsync(CancellationToken ct = default)
    {
        Entered.TrySetResult();
        try
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Canceled.TrySetResult();
            throw;
        }

        return new ServerStatus { Version = "unreachable" };
    }

    public Task<PlayerState> GetPlayerStateAsync(PlayerId playerId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<ActionResult> MovePlayerAsync(MoveRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<ActionResult> PerformActionAsync(ActionRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<PlayerState> RegisterPlayerAsync(string playerName, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
