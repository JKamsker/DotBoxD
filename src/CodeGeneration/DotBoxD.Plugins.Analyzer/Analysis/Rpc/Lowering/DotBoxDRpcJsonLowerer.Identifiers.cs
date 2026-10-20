using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private string LowerIdentifier(IdentifierNameSyntax identifier)
    {
        var name = identifier.Identifier.ValueText;
        if (_inlinedBindings is not null &&
            _inlinedBindings.TryGetValue(name, out var binding))
        {
            return binding.Source;
        }

        var symbol = _model.GetSymbolInfo(identifier, _cancellationToken).Symbol;
        if (symbol is ILocalSymbol or IParameterSymbol)
        {
            return Var(name);
        }

        if (symbol is IPropertySymbol property && IsLiveSetting(property))
        {
            return LowerLiveSetting(name);
        }

        throw new NotSupportedException(
            $"Kernel RPC service identifier '{name}' is not a local or parameter.");
    }

    private string? TryLowerLiveSettingMemberAccess(MemberAccessExpressionSyntax member)
    {
        if (member.Expression is not ThisExpressionSyntax ||
            _model.GetSymbolInfo(member, _cancellationToken).Symbol is not IPropertySymbol property ||
            !IsLiveSetting(property))
        {
            return null;
        }

        return LowerLiveSetting(member.Name.Identifier.ValueText);
    }

    private string LowerLiveSetting(string name)
    {
        if (!_allowLiveSettings)
        {
            throw new NotSupportedException(
                "Local UI kernels cannot read live settings; pass the value through a UI state/input slot or use a remote handler.");
        }

        return Var(name);
    }

    private static bool IsLiveSetting(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.LiveSettingAttribute,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
