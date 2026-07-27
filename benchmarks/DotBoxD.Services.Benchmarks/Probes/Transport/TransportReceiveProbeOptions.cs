namespace DotBoxD.Services.Benchmarks.Probes;

internal static class TransportReceiveProbeOptions
{
    private const string BatchSizeVariable = "DOTBOXD_TRANSPORT_RECEIVE_BATCH_SIZE";

    public static int BatchSize { get; } = ReadBatchSize();

    private static int ReadBatchSize()
    {
        var configured = Environment.GetEnvironmentVariable(BatchSizeVariable);
        if (configured is null)
        {
            return 16;
        }

        if (int.TryParse(configured, out var batchSize) && batchSize is 1 or 2 or 4 or 16)
        {
            return batchSize;
        }

        throw new InvalidOperationException(
            $"{BatchSizeVariable} must be one of: 1, 2, 4, 16.");
    }
}
