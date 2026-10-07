using System.Collections.Immutable;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;

namespace Examples.SandboxedUi.Host;

internal sealed class HeadlessRenderer : IUiRenderer
{
    public int Materializations { get; private set; }
    public int Updates { get; private set; }
    public bool Disposed { get; private set; }

    public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
    {
        Materializations++;
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
    {
        Updates++;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
