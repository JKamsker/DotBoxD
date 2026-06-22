using DotBoxD.Services.Tests.Support;
using Shared;
using Xunit;
namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed partial class EndToEndCoverageTests
{
    [Fact]
    public async Task InvokeAfterClientDisposed_ThrowsObjectDisposed()
    {
        var (server, client, game) = StartInMemoryPair(
            peer => peer.ProvideGameService(new TestGameService()));
        try
        {
            Assert.Equal("1.0.0-test", (await game.GetServerStatusAsync().WaitAsync(Timeout)).Version);

            await client.DisposeAsync();

            // The cached proxy now points at a disposed peer: its next call must fail fast. RpcPeer
            // guards the start path with an ObjectDisposedException (see EnsureStarted) rather than
            // attempting a doomed send, so the disposed object is the surfaced fault.
            await Assert.ThrowsAnyAsync<ObjectDisposedException>(
                () => game.GetServerStatusAsync().WaitAsync(Timeout));
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    /// <summary>
    /// Drives calls until one fails. After a remote teardown the next call may briefly succeed if it
    /// was already queued; loop a bounded number of times so the test asserts the eventual failure
    /// without depending on exact timing.
    /// </summary>
    private static async Task InvokeUntilFailsAsync(IGameService game)
    {
        for (var i = 0; i < 50; i++)
        {
            await game.GetServerStatusAsync();
            await Task.Yield();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Test-only blocking game service: parks GetServerStatusAsync until cancelled so the remote
    // cancellation path can be observed end-to-end through the generated proxy.
    // ---------------------------------------------------------------------------------------------

    private sealed class BlockingGameService : IGameService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ServerStatus> GetServerStatusAsync(CancellationToken ct = default)
        {
            Entered.TrySetResult();
            try
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
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

}
