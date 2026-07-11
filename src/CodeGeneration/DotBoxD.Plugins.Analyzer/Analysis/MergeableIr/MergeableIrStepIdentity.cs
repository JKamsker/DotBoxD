using System.Globalization;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.MergeableIr;

internal static class MergeableIrStepIdentity
{
    public static string Compute(InvocationExpressionSyntax invocation)
    {
        var span = invocation.GetLocation().GetLineSpan();
        var seed = span.Path + ":" +
                   invocation.ArgumentList.OpenParenToken.SpanStart.ToString(CultureInfo.InvariantCulture) + ":" +
                   invocation.Span.End.ToString(CultureInfo.InvariantCulture);
        return Fnv1a(seed).ToString("x16", CultureInfo.InvariantCulture);
    }

    private static ulong Fnv1a(string text)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash;
    }
}
