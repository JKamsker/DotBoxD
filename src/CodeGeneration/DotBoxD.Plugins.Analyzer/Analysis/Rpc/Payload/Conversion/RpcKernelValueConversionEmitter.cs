namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

/// <summary>
/// Emits the helper methods that marshal between a generated server-extension client's C# values and
/// <c>KernelRpcValue</c> wire values: scalars inline, <c>List&lt;T&gt;</c>/arrays as <c>List</c>, and DTOs
/// (records/structs/classes with public instance properties) as positional <c>Record</c>s. Field
/// expressions are computed before the owning helper is appended so a nested helper is never spliced into
/// the middle of another helper's body. Shared by the proxy and direct (graft) client emitters so both
/// support the same parameter and return shapes — DTO parameters and returns, nested DTOs, and
/// list-typed DTO fields — without divergence.
/// </summary>
internal sealed partial class RpcKernelValueConversionEmitter
{
    private readonly StringBuilder _helpers = new();
    private readonly Dictionary<string, string> _readers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _writers = new(StringComparer.Ordinal);
    private readonly Compilation? _compilation;
    private int _nextHelper;

    public RpcKernelValueConversionEmitter(Compilation? compilation = null)
    {
        _compilation = compilation;
    }

    /// <summary>The accumulated helper method definitions, appended after the emitter's own members.</summary>
    public string Helpers => _helpers.ToString();

    /// <summary>A C# expression that marshals <paramref name="expression"/> (of <paramref name="type"/>) into a <c>KernelRpcValue</c>.</summary>
    public string WriteExpression(ITypeSymbol type, string expression)
        => type.SpecialType switch
        {
            SpecialType.System_Boolean => $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Bool({expression})",
            SpecialType.System_Int32 => $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Int32({expression})",
            SpecialType.System_Int64 => $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Int64({expression})",
            SpecialType.System_Double => $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Double({expression})",
            // float widens losslessly to the wire's only floating kind (F64); read narrows it back.
            SpecialType.System_Single => $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.Double({expression})",
            SpecialType.System_String => $"{DotBoxDRpcValueNames.GlobalKernelRpcValue}.String({expression})",
            _ => WriteComplexExpression(type, expression)
        };

    /// <summary>A C# expression that reads a <c>KernelRpcValue</c> (<paramref name="expression"/>) back into <paramref name="type"/>.</summary>
    public string ReadExpression(ITypeSymbol type, string expression)
        => type.SpecialType switch
        {
            SpecialType.System_Boolean => $"{expression}.BoolValue",
            SpecialType.System_Int32 => $"{expression}.Int32Value",
            SpecialType.System_Int64 => $"{expression}.Int64Value",
            SpecialType.System_Double => $"{expression}.DoubleValue",
            SpecialType.System_Single => $"{EnsureSingleValueReader()}({expression})",
            SpecialType.System_String => $"{expression}.TextValue",
            _ => ReadComplexExpression(type, expression)
        };

    private string WriteComplexExpression(ITypeSymbol type, string expression)
    {
        foreach (var resolver in ComplexWriteResolvers)
        {
            if (resolver(this, type, expression, out var result))
            {
                return result;
            }
        }

        throw new NotSupportedException($"Server extension type '{type.ToDisplayString()}' is not supported.");
    }

    private string ReadComplexExpression(ITypeSymbol type, string expression)
    {
        foreach (var resolver in ComplexReadResolvers)
        {
            if (resolver(this, type, expression, out var result))
            {
                return result;
            }
        }

        throw new NotSupportedException($"Server extension type '{type.ToDisplayString()}' is not supported.");
    }

    private string NextHelperName(string prefix) => prefix + "KernelRpcValue" + _nextHelper++;

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string TypeKey(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Identifier(string name) => "@" + name;
}
