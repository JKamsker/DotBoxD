using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcKernelGraftCollisionDetector
{
    public static EquatableArray<RpcKernelGraftCollision> FindDuplicates(
        ImmutableArray<RpcKernelModelResult> results)
    {
        var seen = new Dictionary<string, RpcKernelGraftSignature>(StringComparer.Ordinal);
        var duplicates = new List<RpcKernelGraftCollision>();
        foreach (var result in results)
        {
            foreach (var signature in result.Grafts)
            {
                if (seen.TryGetValue(signature.Key, out var first))
                {
                    duplicates.Add(new RpcKernelGraftCollision(signature, first.KernelType));
                    continue;
                }

                seen.Add(signature.Key, signature);
            }
        }

        return new EquatableArray<RpcKernelGraftCollision>(duplicates);
    }

    public static IEnumerable<Diagnostic> Diagnostics(EquatableArray<RpcKernelGraftCollision> collisions)
    {
        foreach (var collision in collisions)
        {
            var signature = collision.Signature;
            yield return Diagnostic.Create(
                PluginAnalyzerDiagnostics.ServerExtensionGraftCollisionRule,
                signature.Location?.ToLocation() ?? Location.None,
                signature.Display,
                signature.ReceiverType,
                signature.Namespace,
                collision.FirstKernelType);
        }
    }
}

internal sealed record RpcKernelGraftCollision(
    RpcKernelGraftSignature Signature,
    string FirstKernelType);
