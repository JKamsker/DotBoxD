using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

/// <summary>
/// Lowers a <c>[ServerExtension]</c> batch method body to DotBoxD.Kernels JSON IR (statements + expressions),
/// the same JSON the host imports at install. Supports the canonical batch shape: local declarations, a
/// <c>foreach</c> over a list, <c>if</c>/<c>else</c>, host-binding calls via <c>ctx.Host&lt;T&gt;()</c> or
/// constructor-injected service fields, building DTOs (<c>new T(...)</c>/<c>new T{...}</c> →
/// <c>record.new</c>) and accumulating into a list (<c>list.Add</c> → <c>list.add</c>),
/// <c>return</c>, and the loop-control statements <c>continue</c>/<c>break</c> (lowered to the
/// kernel IR's structured loop control). Capabilities/effects from host bindings are collected. Unsupported shapes throw
/// <see cref="NotSupportedException"/> so the kernel fails safe. The
/// expression half lives in the partial <c>DotBoxDRpcJsonLowerer.Expressions.cs</c>.
/// </summary>
internal sealed partial class DotBoxDRpcJsonLowerer
{
    private readonly SemanticModel _model;
    private readonly ICollection<string> _capabilities;
    private readonly ICollection<string> _effects;
    private readonly CancellationToken _cancellationToken;
    private readonly IReadOnlyDictionary<string, RpcInlinedBinding>? _inlinedBindings;
    private readonly IReadOnlyCollection<string>? _inlineStack;
    private readonly Func<string, string>? _reserveGeneratedName;
    private readonly string? _serverContextParameterName;
    private readonly ITypeSymbol? _serverContextType;
    private readonly Dictionary<string, string> _serviceHandleLocals = new(StringComparer.Ordinal);
    private readonly Dictionary<ISymbol, string> _serviceHandleMembers = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ISymbol, ITypeSymbol> _fallbackLocalTypes = new(SymbolEqualityComparer.Default);
    private readonly HashSet<string> _reservedNames = new(StringComparer.Ordinal);
    private RpcAssignmentOverride? _assignmentOverride;
    private Func<ExpressionSyntax, string?>? _expressionOverride;
    private List<string>? _expressionPrelude;
    private IReadOnlyList<string> _returnRecordFields = [];
    private string? _returnRecordType;
    private ITypeSymbol? _returnValueType;
    private int _tempCounter;

    /// <summary>True once the body builds an allocating value, so the manifest declares the Alloc effect.</summary>
    public bool Allocates { get; private set; }
    internal SemanticModel Model => _model;
    internal CancellationToken CancellationToken => _cancellationToken;
    internal ITypeSymbol? ReturnValueType => _returnValueType;
    internal IReadOnlyList<string> ReturnRecordFields => _returnRecordFields;
    internal string? ReturnRecordType => _returnRecordType;
    internal RpcAssignmentOverride? AssignmentOverride => _assignmentOverride;

    public string LowerBody(BlockSyntax block) => LowerBody(block, [], [], returnRecordType: null, assignmentOverride: null);
    internal void AddServiceHandleLocal(string name, string handleIdJson)
        => _serviceHandleLocals[name] = handleIdJson;
    internal void AddServiceHandleMember(ISymbol member, string handleIdJson)
    {
        _serviceHandleLocals[member.Name] = handleIdJson;
        _serviceHandleMembers[member] = handleIdJson;
    }

    internal string LowerBody(
        BlockSyntax block,
        IReadOnlyList<(string Name, string Value)> leadingLocals,
        IReadOnlyList<string> returnRecordFields,
        string? returnRecordType,
        RpcAssignmentOverride? assignmentOverride,
        Func<ExpressionSyntax, string?>? expressionOverride = null,
        ITypeSymbol? returnValueType = null)
    {
        _assignmentOverride = assignmentOverride;
        _expressionOverride = null;
        _returnRecordFields = returnRecordFields;
        _returnRecordType = returnRecordType;
        _returnValueType = returnValueType;
        try
        {
            _fallbackLocalTypes.Clear();
            ReserveUserNames(block);
            var parts = new List<string>();
            for (var i = 0; i < leadingLocals.Count; i++)
            {
                parts.Add(SetStatement(leadingLocals[i].Name, leadingLocals[i].Value));
            }

            _expressionOverride = expressionOverride;
            RpcJsonStatementLowerer.LowerStatements(this, block.Statements, parts);
            return "[" + string.Join(",", parts) + "]";
        }
        finally
        {
            _assignmentOverride = null;
            _expressionOverride = null;
            _returnRecordFields = [];
            _returnRecordType = null;
            _returnValueType = null;
            _fallbackLocalTypes.Clear();
        }
    }
    internal static string SetGeneratedLocal(string name, string value) => SetStatement(name, value);

    internal void MarkAllocates() => Allocates = true;
}
