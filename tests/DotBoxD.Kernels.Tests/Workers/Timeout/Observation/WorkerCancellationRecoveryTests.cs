namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerCancellationRecoveryTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public Task Late_success_and_cancellation_are_consumed_once_and_released(bool timeout, bool customSource, bool cancel)
        => WorkerFailureObservationTests.AssertObservedAsync(marker =>
            WorkerCancellationFixture.CompleteLate(timeout, customSource, cancel, marker));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task Client_remains_usable_while_previous_work_completes(bool timeout, bool fail)
        => WorkerFailureObservationTests.AssertObservedAsync(marker => WorkerCancellationFixture.Reuse(timeout, fail, marker));
}
