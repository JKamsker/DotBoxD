using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
namespace DotBoxD.Kernels.Validation.Internal;

using DotBoxD.Kernels;
internal delegate SandboxType ExpressionAnalyzer(
    Expression expression,
    FunctionScope scope,
    ref SandboxEffect effects,
    ref bool canReorder);
internal sealed class CollectionCallAnalyzer
{
    private readonly List<SandboxDiagnostic> _diagnostics;
    private readonly ExpressionAnalyzer _analyzeExpression;
    private readonly IReadOnlySet<string> _declaredOpaqueIdTypes;
    private readonly CollectionMapCallAnalyzer _mapCalls;
    private readonly CollectionRecordCallAnalyzer _recordCalls;
    public CollectionCallAnalyzer(List<SandboxDiagnostic> diagnostics, ExpressionAnalyzer analyzeExpression, IReadOnlySet<string> declaredOpaqueIdTypes)
    {
        _diagnostics = diagnostics;
        _analyzeExpression = analyzeExpression;
        _declaredOpaqueIdTypes = declaredOpaqueIdTypes;
        _mapCalls = new CollectionMapCallAnalyzer(diagnostics, analyzeExpression, declaredOpaqueIdTypes);
        _recordCalls = new CollectionRecordCallAnalyzer(diagnostics, analyzeExpression, declaredOpaqueIdTypes);
    }
    public bool TryAnalyze(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder,
        out SandboxType type)
    {
        if (!IsCollectionCall(call.Name))
        {
            type = SandboxType.Unit;
            return false;
        }
        if (TryAnalyzeListCall(call, scope, ref effects, ref canReorder, out type) ||
            TryAnalyzeMapCall(call, scope, ref effects, ref canReorder, out type) ||
            TryAnalyzeRecordCall(call, scope, ref effects, ref canReorder, out type))
        {
            return true;
        }

        type = SandboxType.Unit;
        return true;
    }

    private bool TryAnalyzeListCall(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder,
        out SandboxType type)
    {
        type = call.Name switch
        {
            "list.empty" => AnalyzeListEmpty(call, ref effects),
            "list.of" => AnalyzeListOf(call, scope, ref effects, ref canReorder),
            "list.count" => AnalyzeListCount(call, scope, ref effects, ref canReorder),
            "list.get" => AnalyzeListGet(call, scope, ref effects, ref canReorder),
            "list.add" => AnalyzeListAdd(call, scope, ref effects, ref canReorder),
            _ => null!
        };
        return type is not null;
    }

    private bool TryAnalyzeMapCall(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder,
        out SandboxType type)
    {
        type = call.Name switch
        {
            "map.empty" => _mapCalls.AnalyzeMapEmpty(call, ref effects),
            "map.containsKey" => _mapCalls.AnalyzeMapContainsKey(call, scope, ref effects, ref canReorder),
            "map.get" => _mapCalls.AnalyzeMapGet(call, scope, ref effects, ref canReorder),
            "map.set" => _mapCalls.AnalyzeMapSet(call, scope, ref effects, ref canReorder),
            "map.remove" => _mapCalls.AnalyzeMapRemove(call, scope, ref effects, ref canReorder),
            _ => null!
        };
        return type is not null;
    }

    private bool TryAnalyzeRecordCall(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder,
        out SandboxType type)
    {
        type = call.Name switch
        {
            "record.new" => _recordCalls.AnalyzeRecordNew(call, scope, ref effects, ref canReorder),
            "record.get" => _recordCalls.AnalyzeRecordGet(call, scope, ref effects, ref canReorder),
            _ => null!
        };
        return type is not null;
    }
    private SandboxType AnalyzeListEmpty(CallExpression call, ref SandboxEffect effects)
    {
        effects |= SandboxEffect.Alloc;
        if (call.Arguments.Count != 0)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-CALL-ARITY", "list.empty expects 0 arguments", Span: call.Span));
        }
        if (call.GenericType is null)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-CALL-GENERIC", "list.empty requires genericType", Span: call.Span));
            return SandboxType.List(SandboxType.Unit);
        }
        CheckKnownType(call.GenericType, call.Span);
        return SandboxType.List(call.GenericType);
    }
    private SandboxType AnalyzeListOf(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        effects |= SandboxEffect.Alloc;
        SandboxType? itemType = null;
        foreach (var arg in call.Arguments)
        {
            var current = _analyzeExpression(arg, scope, ref effects, ref canReorder);
            itemType ??= current;
            Require(current, itemType, arg.Span);
        }
        return SandboxType.List(itemType ?? SandboxType.Unit);
    }
    private SandboxType AnalyzeListCount(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        if (call.Arguments.Count != 1)
        {
            Arity(call, 1);
            return SandboxType.I32;
        }
        RequireList(
            _analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder),
            call.Arguments[0].Span);
        return SandboxType.I32;
    }
    private SandboxType AnalyzeListGet(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        if (call.Arguments.Count != 2)
        {
            Arity(call, 2);
            return SandboxType.Unit;
        }
        var listType = RequireList(
            _analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder),
            call.Arguments[0].Span);
        Require(
            _analyzeExpression(call.Arguments[1], scope, ref effects, ref canReorder),
            SandboxType.I32,
            call.Arguments[1].Span);
        return listType?.Arguments[0] ?? SandboxType.Unit;
    }
    private SandboxType AnalyzeListAdd(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        effects |= SandboxEffect.Alloc;
        if (call.Arguments.Count != 2)
        {
            Arity(call, 2);
            return SandboxType.List(SandboxType.Unit);
        }
        var listType = RequireList(
            _analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder),
            call.Arguments[0].Span);
        var itemType = _analyzeExpression(call.Arguments[1], scope, ref effects, ref canReorder);
        if (listType is null)
        {
            return SandboxType.List(itemType);
        }
        Require(itemType, listType.Arguments[0], call.Arguments[1].Span);
        return listType;
    }
    private SandboxType? RequireList(SandboxType actual, SourceSpan span)
    {
        if (actual.Name == "List" && actual.Arguments.Count == 1)
        {
            return actual;
        }

        _diagnostics.Add(new SandboxDiagnostic("E-TYPE-MISMATCH", $"expected List<T>, got {actual}", Span: span));
        return null;
    }

    private void CheckKnownType(SandboxType type, SourceSpan span)
    {
        if (!type.IsKnown(_declaredOpaqueIdTypes))
        {
            _diagnostics.Add(new SandboxDiagnostic("E-TYPE-UNKNOWN", $"unknown or forbidden type '{type}'", Span: span));
        }
    }

    private void Require(SandboxType actual, SandboxType expected, SourceSpan span)
    {
        if (actual != expected)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-TYPE-MISMATCH", $"expected {expected}, got {actual}", Span: span));
        }
    }

    private void Arity(CallExpression call, int expected)
        => _diagnostics.Add(new SandboxDiagnostic(
            "E-CALL-ARITY",
            $"{call.Name} expects {expected} argument{(expected == 1 ? "" : "s")}",
            Span: call.Span));

    private static bool IsCollectionCall(string name)
        => SandboxCollectionFuel.IsCollectionIntrinsic(name) ||
           name is "record.new" or "record.get";
}
