using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private string? TryLowerDerivedCreation(
        ExpressionSyntax expression,
        IReadOnlyDictionary<ISymbol, string> memberBindings,
        INamedTypeSymbol named,
        RecordMember derived)
        => expression switch
        {
            ObjectCreationExpressionSyntax creation =>
                LowerDerivedCreation(creation, memberBindings, named, derived),
            ImplicitObjectCreationExpressionSyntax creation =>
                LowerDerivedCreation(creation, memberBindings, named, derived),
            _ => null
        };

    private string LowerDerivedCreation(
        BaseObjectCreationExpressionSyntax creation,
        IReadOnlyDictionary<ISymbol, string> memberBindings,
        INamedTypeSymbol named,
        RecordMember derived)
    {
        var previousOverride = _expressionOverride;
        _expressionOverride = part =>
            BoundDerivedMember(memberBindings, part) ?? previousOverride?.Invoke(part);
        try
        {
            return LowerRecordCreation(creation);
        }
        catch (NotSupportedException ex)
        {
            throw DerivedNotSupported(named, derived, ex);
        }
        finally
        {
            _expressionOverride = previousOverride;
        }
    }
}
