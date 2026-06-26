using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    // Lowers a statement-form `target++`/`target--`/`++target`/`--target` to `target = target +/- 1`. The
    // literal 1 is widened to the local's converted type via the same numeric-conversion path a real
    // `target += 1` would take, so a long/double local gets a matching operand (e.g. add(var[I64],
    // numeric.toI64(i32 1))) instead of a hardcoded i32 that the sandbox rejects as a type mismatch.
    private string LowerIncrementDecrement(IdentifierNameSyntax target, bool increment)
    {
        var name = target.Identifier.ValueText;
        var op = increment ? "add" : "sub";
        return SetStatement(name, BinaryJson(op, Var(name), WidenedOne(target)));
    }

    private string WidenedOne(IdentifierNameSyntax target)
    {
        var one = I32(1);
        if (_model.GetTypeInfo(target, _cancellationToken).Type is not { } targetType)
        {
            return one;
        }

        return ApplyNumericConversion(
            _model.Compilation.GetSpecialType(SpecialType.System_Int32),
            targetType,
            one);
    }
}
