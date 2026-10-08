using System.Collections.Immutable;

namespace DotBoxD.UI.Runtime;

/// <summary>
/// Trusted host adapter, owned by one session. Marshal calls to the toolkit's UI thread as needed.
/// Materialize is called once after validation; updates contain changed property values or
/// authoritative input corrections after rejected native edits.
/// Queue semantic input to the session; do not reenter it synchronously from these methods.
/// </summary>
public interface IUiRenderer : IAsyncDisposable
{
    /// <summary>V1 adapters support the closed core schema unless they advertise a smaller set.</summary>
    UiRendererCapabilities Capabilities => UiRendererCapabilities.Core;

    /// <summary>Checks trusted adapter policy before initial kernels run.</summary>
    void ValidatePackage(UiPackage package, UiPolicy policy) => Capabilities.Validate(package);

    ValueTask MaterializeAsync(
        UiPackage package,
        ImmutableArray<UiPropertyValue> values,
        CancellationToken cancellationToken);

    ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken);
}
