using System.Text.Json;

namespace DotBoxD.Kernels.Compiler;

public sealed partial class PersistentCompiledArtifactCache
{
    private static async ValueTask<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ??
                       throw new JsonException("empty json file");
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                and not JsonException
                and not IOException
                and not UnauthorizedAccessException)
            {
                // A cached model can round-trip through System.Text.Json yet still fail to
                // construct because its defensive normalization (e.g. ArtifactManifest copying
                // OptimizationFlags, which rejects a null collection) throws while materializing
                // invalid persisted data. Convert any such materialization failure into a
                // JsonException so the cache read path fails closed and routes the entry to
                // quarantine + recompile, instead of surfacing an unhandled exception that aborts
                // execution. Cancellation and the already-handled IO/JSON failures propagate as-is.
                throw new JsonException(
                    $"cached '{typeof(T).Name}' metadata could not be materialized: {ex.Message}",
                    ex);
            }
        }
    }

    private static async ValueTask WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var stream = DurableCreate(path);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
    }

    private static async ValueTask WriteBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var stream = DurableCreate(path);
        await using (stream.ConfigureAwait(false))
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
    }

    private static FileStream DurableCreate(string path)
        => new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
}
