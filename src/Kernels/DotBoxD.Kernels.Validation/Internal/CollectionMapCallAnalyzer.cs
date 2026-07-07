using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Validation.Internal;

using DotBoxD.Kernels;

internal sealed class CollectionMapCallAnalyzer(
    List<SandboxDiagnostic> diagnostics,
    ExpressionAnalyzer analyzeExpression,
    IReadOnlySet<string> declaredOpaqueIdTypes)
{
    public SandboxType AnalyzeMapEmpty(CallExpression call, ref SandboxEffect effects)
    {
        effects |= SandboxEffect.Alloc;
        if (call.Arguments.Count != 0)
        {
            Arity(call, 0);
        }

        var mapType = RequireMapGeneric(call);
        return mapType ?? SandboxType.Map(SandboxType.Unit, SandboxType.Unit);
    }

    public SandboxType AnalyzeMapContainsKey(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        if (call.Arguments.Count != 2)
        {
            Arity(call, 2);
            return SandboxType.Bool;
        }

        var mapType = RequireMap(
            analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder),
            call.Arguments[0].Span);
        var keyType = analyzeExpression(call.Arguments[1], scope, ref effects, ref canReorder);
        if (mapType is not null)
        {
            Require(keyType, mapType.Arguments[0], call.Arguments[1].Span);
        }

        return SandboxType.Bool;
    }

    public SandboxType AnalyzeMapGet(
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

        var mapType = RequireMap(
            analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder),
            call.Arguments[0].Span);
        var keyType = analyzeExpression(call.Arguments[1], scope, ref effects, ref canReorder);
        if (mapType is null)
        {
            return SandboxType.Unit;
        }

        Require(keyType, mapType.Arguments[0], call.Arguments[1].Span);
        return mapType.Arguments[1];
    }

    public SandboxType AnalyzeMapSet(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        effects |= SandboxEffect.Alloc;
        if (call.Arguments.Count != 3)
        {
            Arity(call, 3);
            return SandboxType.Map(SandboxType.Unit, SandboxType.Unit);
        }

        var mapType = RequireMap(
            analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder),
            call.Arguments[0].Span);
        var keyType = analyzeExpression(call.Arguments[1], scope, ref effects, ref canReorder);
        var valueType = analyzeExpression(call.Arguments[2], scope, ref effects, ref canReorder);
        if (mapType is null)
        {
            return SandboxType.Map(keyType, valueType);
        }

        Require(keyType, mapType.Arguments[0], call.Arguments[1].Span);
        Require(valueType, mapType.Arguments[1], call.Arguments[2].Span);
        return mapType;
    }

    public SandboxType AnalyzeMapRemove(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        effects |= SandboxEffect.Alloc;
        if (call.Arguments.Count != 2)
        {
            Arity(call, 2);
            return SandboxType.Map(SandboxType.Unit, SandboxType.Unit);
        }

        var mapType = RequireMap(
            analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder),
            call.Arguments[0].Span);
        var keyType = analyzeExpression(call.Arguments[1], scope, ref effects, ref canReorder);
        if (mapType is not null)
        {
            Require(keyType, mapType.Arguments[0], call.Arguments[1].Span);
        }

        return mapType ?? SandboxType.Map(keyType, SandboxType.Unit);
    }

    private SandboxType? RequireMap(SandboxType actual, SourceSpan span)
    {
        if (actual.Name == "Map" && actual.Arguments.Count == 2)
        {
            RequireMapKey(actual.Arguments[0], span);
            return actual;
        }

        diagnostics.Add(new SandboxDiagnostic("E-TYPE-MISMATCH", $"expected Map<K,V>, got {actual}", Span: span));
        return null;
    }

    private SandboxType? RequireMapGeneric(CallExpression call)
    {
        if (call.GenericType is null)
        {
            diagnostics.Add(new SandboxDiagnostic("E-CALL-GENERIC", "map.empty requires Map<K,V> genericType", Span: call.Span));
            return null;
        }

        CheckKnownType(call.GenericType, call.Span);
        return RequireMap(call.GenericType, call.Span);
    }

    private void RequireMapKey(SandboxType keyType, SourceSpan span)
    {
        if (keyType.IsValidMapKey(declaredOpaqueIdTypes))
        {
            return;
        }

        diagnostics.Add(new SandboxDiagnostic("E-TYPE-MAP-KEY", $"map key type '{keyType}' is not supported", Span: span));
    }

    private void CheckKnownType(SandboxType type, SourceSpan span)
    {
        if (!type.IsKnown(declaredOpaqueIdTypes))
        {
            diagnostics.Add(new SandboxDiagnostic("E-TYPE-UNKNOWN", $"unknown or forbidden type '{type}'", Span: span));
        }
    }

    private void Require(SandboxType actual, SandboxType expected, SourceSpan span)
    {
        if (actual != expected)
        {
            diagnostics.Add(new SandboxDiagnostic("E-TYPE-MISMATCH", $"expected {expected}, got {actual}", Span: span));
        }
    }

    private void Arity(CallExpression call, int expected)
        => diagnostics.Add(new SandboxDiagnostic(
            "E-CALL-ARITY",
            $"{call.Name} expects {expected} argument{(expected == 1 ? "" : "s")}",
            Span: call.Span));
}
