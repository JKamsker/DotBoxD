using System.Collections.Immutable;

namespace DotBoxD.UI.Runtime;

internal sealed class UiRendererOwner(IUiRenderer renderer) : IAsyncDisposable
{
    private IUiRenderer? _renderer = renderer;

    public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken token)
        => _renderer!.MaterializeAsync(package, values, token);

    public async ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken token)
    {
        try
        {
            await _renderer!.UpdateAsync(changes, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new UiRendererException(ex);
        }
    }

    public ValueTask DisposeAsync()
        => Interlocked.Exchange(ref _renderer, null)?.DisposeAsync() ?? ValueTask.CompletedTask;

    public static async ValueTask AcknowledgeAsync(IUiInputSource source, UiInput input, CancellationToken token)
    {
        try
        {
            await source.AcknowledgeAsync(input, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new UiRendererException(ex);
        }
    }
}

internal sealed class UiRendererException(Exception inner) : Exception("Trusted UI renderer failed; session disconnected.", inner);
