using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainIndexEventPathResolver
{
    public static bool TryResolve(
        ExpressionSyntax expression,
        string elementParam,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        out HookChainIndexEventPath path)
    {
        path = default;
        if (expression is not MemberAccessExpressionSyntax member ||
            !TryReadSegments(member, elementParam, out var segments) ||
            !TryRoot(segments[0], eventProperties, out var root))
        {
            return false;
        }

        if (segments.Count == 1)
        {
            path = new HookChainIndexEventPath(root.Name, root.Type);
            return true;
        }

        if (!ReceiversAreRecordShaped(member, model, cancellationToken) ||
            model.GetSymbolInfo(member, cancellationToken).Symbol is not IPropertySymbol leaf)
        {
            return false;
        }

        var leafType = SandboxTypeSourceEmitter.ManifestTag(leaf.Type);
        path = new HookChainIndexEventPath(string.Join(".", segments), leafType);
        return true;
    }

    private static bool TryReadSegments(
        MemberAccessExpressionSyntax member,
        string elementParam,
        out List<string> segments)
    {
        var stack = new Stack<string>();
        ExpressionSyntax current = member;
        while (current is MemberAccessExpressionSyntax access)
        {
            stack.Push(access.Name.Identifier.ValueText);
            current = access.Expression;
        }

        if (current is IdentifierNameSyntax identifier &&
            string.Equals(identifier.Identifier.ValueText, elementParam, StringComparison.Ordinal) &&
            stack.Count > 0)
        {
            segments = [.. stack];
            return true;
        }

        segments = [];
        return false;
    }

    private static bool TryRoot(
        string name,
        EquatableArray<EventPropertyModel> eventProperties,
        out EventPropertyModel root)
    {
        for (var i = 0; i < eventProperties.Count; i++)
        {
            if (string.Equals(eventProperties[i].Name, name, StringComparison.Ordinal))
            {
                root = eventProperties[i];
                return true;
            }
        }

        root = null!;
        return false;
    }

    private static bool ReceiversAreRecordShaped(
        MemberAccessExpressionSyntax member,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var current = member;
        while (current.Expression is MemberAccessExpressionSyntax receiver)
        {
            var type = model.GetTypeInfo(current.Expression, cancellationToken).Type;
            if (type is null ||
                !string.Equals(
                    SandboxTypeSourceEmitter.ManifestTag(type),
                    DotBoxDGenerationNames.ManifestTypes.Record,
                    StringComparison.Ordinal))
            {
                return false;
            }

            current = receiver;
        }

        return true;
    }
}

internal readonly record struct HookChainIndexEventPath(string Path, string Type);
