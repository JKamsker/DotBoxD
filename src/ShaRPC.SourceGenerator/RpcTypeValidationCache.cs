using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal sealed class RpcTypeValidationCache
{
    private readonly Dictionary<string, bool> _subServicePayloadResults =
        new(System.StringComparer.Ordinal);

    public bool ContainsShaRpcServiceInterface(ITypeSymbol type, CancellationToken ct) =>
        SubServicePayloadInspector.ContainsShaRpcServiceInterface(
            type,
            ct,
            _subServicePayloadResults);
}
