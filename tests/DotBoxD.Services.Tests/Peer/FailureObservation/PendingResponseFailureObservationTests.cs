using System.Runtime.CompilerServices;
using DotBoxD.Services.Client;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.FailureObservation;

public sealed class PendingResponseFailureObservationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task Completed_and_late_faults_are_observed(bool unary, bool completeFirst)
        => ResponseFailureObservation.AssertObservedAsync(marker => Exercise(unary, completeFirst, marker));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Exercise(bool unary, bool completeFirst, string marker)
    {
        using var requests = new PendingRequests();
        IPendingResponse pending = unary
            ? new PendingUnaryResponse<int>(1)
            : new PendingReceivedResponse(requests, 1);
        var error = new InvalidOperationException(marker);
        if (completeFirst)
        {
            pending.SetError(error);
        }

        pending.DisposeResultWhenAvailable();
        if (!completeFirst)
        {
            pending.SetError(error);
        }

        return new WeakReference(error);
    }
}
