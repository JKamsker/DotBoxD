using System.Collections.Immutable;

namespace DotBoxD.UI.Runtime;

/// <summary>
/// Trusted host adapter, owned by one session. Marshal calls to the toolkit's UI thread as needed.
/// Materialize is called once after validation; updates contain only changed property values.
/// Queue semantic input to the session; do not reenter it synchronously from these methods.
/// </summary>
public interface IUiRenderer : IAsyncDisposable
{
    ValueTask MaterializeAsync(
        UiPackage package,
        ImmutableArray<UiPropertyValue> values,
        CancellationToken cancellationToken);

    ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken);
}
