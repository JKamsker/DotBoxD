using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator;

internal sealed class RpcTypeValidationCache
{
    private readonly object _gate = new();
    private readonly Dictionary<ITypeSymbol, bool> _subServicePayloadResults =
        new(SymbolEqualityComparer.Default);

    public bool ContainsDotBoxDServiceInterface(ITypeSymbol type, CancellationToken ct) =>
        SubServicePayloadInspector.ContainsDotBoxDServiceInterface(
            type,
            ct,
            this);

    public bool TryGetSubServicePayloadResult(ITypeSymbol key, out bool result)
    {
        lock (_gate)
        {
            return _subServicePayloadResults.TryGetValue(key, out result);
        }
    }

    public void SetSubServicePayloadResult(ITypeSymbol key, bool result)
    {
        lock (_gate)
        {
            _subServicePayloadResults[key] = result;
        }
    }
}
