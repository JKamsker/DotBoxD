using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal sealed class DynamicHelperCallResolver
{
    private readonly ConcurrentDictionary<ILocalSymbol, ITypeSymbol> _localTypes =
        new(SymbolEqualityComparer.Default);
    private readonly ConcurrentBag<DynamicHelperCall> _calls = [];

    public void RecordLocalType(ILocalSymbol local, ITypeSymbol? type)
    {
        if (type is not null && type.TypeKind != TypeKind.Dynamic)
        {
            _localTypes.TryAdd(local, type);
        }
    }

    public void RecordInvocation(
        ISymbol? caller,
        ILocalSymbol receiver,
        string memberName,
        int argumentCount,
        Location location)
    {
        if (caller is IMethodSymbol or IFieldSymbol or IPropertySymbol)
        {
            _calls.Add(new DynamicHelperCall(caller, receiver, memberName, argumentCount, location));
        }
    }

    public void Resolve(
        Action<IMethodSymbol, IMethodSymbol, Location> recordCall,
        Action<ISymbol, IMethodSymbol, Location> recordInitializerRootCall)
    {
        foreach (var call in _calls)
        {
            if (!_localTypes.TryGetValue(call.Receiver, out var receiverType))
            {
                continue;
            }

            foreach (var target in PluginAnalyzer.DynamicInvocationCandidates(
                         receiverType,
                         call.MemberName,
                         call.ArgumentCount))
            {
                if (call.Caller is IMethodSymbol method)
                {
                    recordCall(method, target, call.Location);
                }
                else
                {
                    recordInitializerRootCall(call.Caller, target, call.Location);
                }
            }
        }
    }

    private readonly record struct DynamicHelperCall(
        ISymbol Caller,
        ILocalSymbol Receiver,
        string MemberName,
        int ArgumentCount,
        Location Location);
}
