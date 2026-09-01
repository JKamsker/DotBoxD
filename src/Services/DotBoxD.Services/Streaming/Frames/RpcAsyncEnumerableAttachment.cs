using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Streaming.Frames;

internal sealed class RpcAsyncEnumerableAttachment<T> : RpcStreamAttachment
{
    private readonly IAsyncEnumerable<T> _source;

    public RpcAsyncEnumerableAttachment(RpcStreamHandle handle, IAsyncEnumerable<T> source)
        : base(handle) =>
        _source = source;

    internal override async Task PumpCoreAsync(
        RpcStreamManager streams,
        ISerializer serializer,
        CancellationToken ct)
    {
        var enumerator = _source.GetAsyncEnumerator(ct);
        Exception? pumpFailure = null;
        try
        {
            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                await streams.SendStreamItemAsync(Handle.StreamId, enumerator.Current, serializer, ct)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            pumpFailure = ex;
            throw;
        }
        finally
        {
            try
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (pumpFailure is not null)
            {
                RpcDiagnostics.Report("Outbound stream source cleanup failed", ex);
            }
        }
    }
}
