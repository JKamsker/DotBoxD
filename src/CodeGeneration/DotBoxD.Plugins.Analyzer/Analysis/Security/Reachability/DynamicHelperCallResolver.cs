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
            _calls.Add(new DynamicHelperCall(
                caller,
                receiver,
                DynamicHelperCallKind.Invocation,
                memberName,
                argumentCount,
                location));
        }
    }

    public void RecordPropertyReference(
        ISymbol? caller,
        ILocalSymbol receiver,
        string memberName,
        Location location)
    {
        if (caller is IMethodSymbol or IFieldSymbol or IPropertySymbol)
        {
            _calls.Add(new DynamicHelperCall(
                caller,
                receiver,
                DynamicHelperCallKind.PropertyReference,
                memberName,
                ArgumentCount: 0,
                location));
        }
    }

    public void RecordIndexerAccess(
        ISymbol? caller,
        ILocalSymbol receiver,
        int argumentCount,
        Location location)
    {
        if (caller is IMethodSymbol or IFieldSymbol or IPropertySymbol)
        {
            _calls.Add(new DynamicHelperCall(
                caller,
                receiver,
                DynamicHelperCallKind.IndexerAccess,
                MemberName: string.Empty,
                argumentCount,
                location));
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

            foreach (var target in DynamicTargets(receiverType, call))
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

    private static IEnumerable<IMethodSymbol> DynamicTargets(ITypeSymbol receiverType, DynamicHelperCall call)
        => call.Kind switch
        {
            DynamicHelperCallKind.Invocation => PluginAnalyzer.DynamicInvocationCandidates(
                receiverType,
                call.MemberName,
                call.ArgumentCount),
            DynamicHelperCallKind.PropertyReference => PluginAnalyzer.DynamicPropertyGetterCandidates(
                receiverType,
                call.MemberName),
            DynamicHelperCallKind.IndexerAccess => PluginAnalyzer.DynamicIndexerGetterCandidates(
                receiverType,
                call.ArgumentCount),
            _ => []
        };

    private readonly record struct DynamicHelperCall(
        ISymbol Caller,
        ILocalSymbol Receiver,
        DynamicHelperCallKind Kind,
        string MemberName,
        int ArgumentCount,
        Location Location);

    private enum DynamicHelperCallKind
    {
        Invocation,
        PropertyReference,
        IndexerAccess
    }
}
