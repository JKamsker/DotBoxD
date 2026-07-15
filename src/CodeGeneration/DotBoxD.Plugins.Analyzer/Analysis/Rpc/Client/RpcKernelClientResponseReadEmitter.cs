namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

/// <summary>
/// Emits synchronous root helpers that project response bytes directly into a generated client's CLR return type.
/// Keeping the ref-struct payload reader inside these helpers lets async client methods remain valid on language
/// versions that do not allow ref-struct locals in async methods.
/// </summary>
internal sealed class RpcKernelClientResponseReadEmitter
{
    private readonly StringBuilder _helpers = new();
    private readonly Dictionary<string, string> _readers = new(StringComparer.Ordinal);
    private readonly RpcKernelPayloadReadEmitter _payload;
    private readonly string _containerName;
    private readonly bool _supportsDirectPayloadReads;
    private int _nextHelper;

    public RpcKernelClientResponseReadEmitter(
        Compilation compilation,
        INamedTypeSymbol? containingContract = null,
        string? reservedMemberName = null)
    {
        _payload = new RpcKernelPayloadReadEmitter(
            compilation,
            skipAbsentNullablePayload: true);
        _containerName = ContainerName(containingContract, reservedMemberName);
        _supportsDirectPayloadReads = HasSkipValue(compilation);
    }

    public string Helpers
    {
        get
        {
            if (_helpers.Length == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.Append("    private static class ").Append(_containerName).AppendLine();
            builder.AppendLine("    {");
            AppendIndented(builder, _helpers.ToString());
            AppendIndented(builder, _payload.Helpers);
            builder.AppendLine("    }");
            return builder.ToString();
        }
    }

    public bool TryReadExpression(ITypeSymbol type, string response, out string expression)
    {
        if (!_supportsDirectPayloadReads)
        {
            expression = string.Empty;
            return false;
        }

        var key = TypeName(type);
        if (!_readers.TryGetValue(key, out var method))
        {
            method = "Read" + _nextHelper++;
            _readers.Add(key, method);
            AppendReader(method, type);
        }

        expression = $"{_containerName}.{method}({response})";
        return true;
    }

    private void AppendReader(string method, ITypeSymbol type)
    {
        var readExpression = _payload.ReadExpression(type, "reader");
        _helpers.Append("    public static ").Append(TypeName(type)).Append(' ').Append(method)
            .AppendLine("(byte[] payload)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        var validator = new global::DotBoxD.Plugins.KernelRpcPayloadReader(payload);");
        _helpers.AppendLine("        validator.SkipValue();");
        _helpers.AppendLine("        validator.EnsureConsumed();");
        _helpers.AppendLine("        var reader = new global::DotBoxD.Plugins.KernelRpcPayloadReader(payload);");
        _helpers.Append("        var result = ").Append(readExpression).AppendLine(";");
        _helpers.AppendLine("        reader.EnsureConsumed();");
        _helpers.AppendLine("        return result;");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
    }

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static bool HasSkipValue(Compilation compilation)
    {
        var readerType = compilation.GetTypeByMetadataName("DotBoxD.Plugins.KernelRpcPayloadReader");
        if (readerType is null)
        {
            return false;
        }

        foreach (var member in readerType.GetMembers("SkipValue"))
        {
            if (member is IMethodSymbol
                {
                    IsStatic: false,
                    DeclaredAccessibility: Accessibility.Public,
                    Parameters.Length: 0,
                    TypeParameters.Length: 0,
                    ReturnType.SpecialType: SpecialType.System_Void
                })
            {
                return true;
            }
        }

        return false;
    }

    private static string ContainerName(
        INamedTypeSymbol? containingContract,
        string? reservedMemberName)
    {
        const string prefix = "KernelRpcResponseReader";
        for (var suffix = 0; ; suffix++)
        {
            var candidate = suffix == 0 ? prefix : prefix + suffix;
            if (!string.Equals(candidate, reservedMemberName, StringComparison.Ordinal) &&
                (containingContract is null || containingContract.GetMembers(candidate).Length == 0))
            {
                return candidate;
            }
        }
    }

    private static void AppendIndented(StringBuilder builder, string source)
    {
        var start = 0;
        while (start < source.Length)
        {
            var newline = source.IndexOf('\n', start);
            var length = newline < 0 ? source.Length - start : newline - start + 1;
            builder.Append("    ").Append(source, start, length);
            start += length;
        }
    }
}
