using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

/// <summary>
/// Resolves the reverse server-&gt;plugin event-callback contract for a generated plugin facade, discovered by
/// the same <c>{worldNs}.Ipc</c> convention the factory uses for the control service. Optional: a world with no
/// such contract keeps the original facade (no local handlers). Two guards keep the emitted facade compilable:
/// a shape guard requires the <c>[RpcService]</c> interface to carry the expected
/// <c>OnEventAsync(string, ReadOnlyMemory&lt;byte&gt;, CancellationToken) -&gt; ValueTask</c>/<c>ValueTask&lt;T&gt;</c> method, and a transport guard requires
/// referenced contracts to expose their assembly-specific generated <c>Provide{suffix}</c> extension
/// (local contracts are generated in the same pass; legacy extension containers remain supported).
/// </summary>
internal static class PluginServerEventCallbackResolver
{
    public static (INamedTypeSymbol Type, string ProvideSuffix, ITypeSymbol ReturnType, bool ReturnHasValue)? Resolve(
        Compilation compilation,
        INamedTypeSymbol worldType,
        CancellationToken cancellationToken)
    {
        var callback = ResolveContract(compilation, worldType);
        if (callback is null)
        {
            return null;
        }

        var suffix = PluginServerWorldExtensionSuffixResolver.Resolve(compilation, callback.Value.Type, cancellationToken);
        return HasProvideExtension(compilation, suffix, callback.Value.Type)
            ? (callback.Value.Type, suffix, callback.Value.Method.ReturnType, ReturnHasValue(callback.Value.Method.ReturnType))
            : null;
    }

    private static (INamedTypeSymbol Type, IMethodSymbol Method)? ResolveContract(
        Compilation compilation,
        INamedTypeSymbol worldType)
    {
        var worldNamespace = PluginServerFacadeNameFormatter.NamespaceMetadataName(worldType.ContainingNamespace);
        var callback = compilation.GetTypeByMetadataName(worldNamespace + ".Ipc.IPluginEventCallback");
        if (callback is null ||
            callback.TypeKind != TypeKind.Interface ||
            !HasRpcServiceAttribute(callback))
        {
            return null;
        }

        var method = ResolveEventCallbackMethod(callback);
        return method is null || !HasResultCallbackMethod(callback) ? null : (callback, method);
    }

    private static IMethodSymbol? ResolveEventCallbackMethod(INamedTypeSymbol callback)
    {
        foreach (var member in Members(callback, "OnEventAsync"))
        {
            if (member is IMethodSymbol { Parameters.Length: 3 } method &&
                IsValueTask(method.ReturnType) &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                IsReadOnlyByteMemory(method.Parameters[1].Type) &&
                string.Equals(
                    method.Parameters[2].Type.ToDisplayString(),
                    "System.Threading.CancellationToken",
                    StringComparison.Ordinal))
            {
                return method;
            }
        }

        return null;
    }

    private static bool HasResultCallbackMethod(INamedTypeSymbol callback)
    {
        foreach (var member in Members(callback, "OnResultAsync"))
        {
            if (member is IMethodSymbol { Parameters.Length: 3 } method &&
                IsByteArrayValueTask(method.ReturnType) &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                IsReadOnlyByteMemory(method.Parameters[1].Type) &&
                string.Equals(
                    method.Parameters[2].Type.ToDisplayString(),
                    "System.Threading.CancellationToken",
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<ISymbol> Members(INamedTypeSymbol callback, string name)
    {
        foreach (var member in callback.GetMembers(name))
        {
            yield return member;
        }

        foreach (var @interface in callback.AllInterfaces)
        {
            foreach (var member in @interface.GetMembers(name))
            {
                yield return member;
            }
        }
    }

    // The pushed payload crosses the wire as ReadOnlyMemory<byte> (the pooled encode hands its written span
    // straight to the transport copy-free). A contract still typed byte[] no longer matches: the facade would
    // silently fall back to the no-local-handler wiring, so RunLocal must declare ReadOnlyMemory<byte>.
    private static bool IsReadOnlyByteMemory(ITypeSymbol type)
        => type is INamedTypeSymbol
        {
            Name: "ReadOnlyMemory",
            IsGenericType: true,
            TypeArguments.Length: 1,
            ContainingNamespace: { } ns
        } named &&
        named.TypeArguments[0].SpecialType == SpecialType.System_Byte &&
        string.Equals(ns.ToDisplayString(), "System", StringComparison.Ordinal);

    private static bool IsValueTask(ITypeSymbol type)
        => type is INamedTypeSymbol
        {
            Name: "ValueTask",
            ContainingNamespace: { } ns
        } named &&
        string.Equals(ns.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal) &&
        (!named.IsGenericType || named.TypeArguments.Length == 1);

    private static bool IsByteArrayValueTask(ITypeSymbol type)
        => type is INamedTypeSymbol
        {
            Name: "ValueTask",
            IsGenericType: true,
            TypeArguments.Length: 1,
            ContainingNamespace: { } ns
        } named &&
        string.Equals(ns.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal) &&
        named.TypeArguments[0] is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte };

    private static bool ReturnHasValue(ITypeSymbol type)
        => type is INamedTypeSymbol { IsGenericType: true };

    // Look in the contract assembly so another generating assembly cannot hide its extensions.
    // Legacy contracts and handwritten extension providers remain supported.
    private static bool HasProvideExtension(
        Compilation compilation,
        string provideSuffix,
        INamedTypeSymbol callbackType)
    {
        var extensions = PluginServerExtensionTypeResolver.Resolve(callbackType);
        var rpcPeerType = compilation.GetTypeByMetadataName("DotBoxD.Services.Peer.RpcPeer");
        if (rpcPeerType is null)
        {
            return false;
        }

        if (extensions is null)
        {
            // Other generators run against the same input compilation, so local generated
            // extensions are not visible yet. Referenced contracts must expose the method.
            return SymbolEqualityComparer.Default.Equals(callbackType.ContainingAssembly, compilation.Assembly);
        }

        foreach (var member in extensions.GetMembers("Provide" + provideSuffix))
        {
            if (member is IMethodSymbol method &&
                IsProvideExtensionMethod(method, rpcPeerType, callbackType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsProvideExtensionMethod(
        IMethodSymbol method,
        INamedTypeSymbol rpcPeerType,
        INamedTypeSymbol callbackType)
        => method is { IsStatic: true, IsGenericMethod: false, Parameters.Length: 2 } &&
        SymbolEqualityComparer.Default.Equals(method.ReturnType, rpcPeerType) &&
        SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, rpcPeerType) &&
        SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, callbackType);

    private static bool HasRpcServiceAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (DotBoxDMetadataNames.IsRpcServiceAttribute(attribute.AttributeClass?.ToDisplayString()))
            {
                return true;
            }
        }

        return false;
    }
}
