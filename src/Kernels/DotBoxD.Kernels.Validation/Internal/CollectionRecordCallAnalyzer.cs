using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Validation.Internal;

using DotBoxD.Kernels;

internal sealed class CollectionRecordCallAnalyzer(
    List<SandboxDiagnostic> diagnostics,
    ExpressionAnalyzer analyzeExpression,
    IReadOnlySet<string> declaredOpaqueIdTypes)
{
    public SandboxType AnalyzeRecordNew(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        effects |= SandboxEffect.Alloc;
        if (call.GenericType is not { } recordType || !recordType.IsRecord)
        {
            diagnostics.Add(new SandboxDiagnostic("E-CALL-GENERIC", "record.new requires a Record genericType", Span: call.Span));
            return AnalyzeRecordFieldsFallback(call, scope, ref effects, ref canReorder);
        }

        CheckKnownType(recordType, call.Span);
        if (call.Arguments.Count != recordType.Arguments.Count)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-CALL-ARITY",
                $"record.new expects {recordType.Arguments.Count} field argument(s)",
                Span: call.Span));
            return recordType;
        }

        for (var i = 0; i < call.Arguments.Count; i++)
        {
            var fieldType = analyzeExpression(call.Arguments[i], scope, ref effects, ref canReorder);
            Require(fieldType, recordType.Arguments[i], call.Arguments[i].Span);
        }

        return recordType;
    }

    public SandboxType AnalyzeRecordGet(
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

        var recordType = analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder);
        _ = analyzeExpression(call.Arguments[1], scope, ref effects, ref canReorder);
        if (!recordType.IsRecord)
        {
            diagnostics.Add(new SandboxDiagnostic("E-TYPE-MISMATCH", $"expected Record, got {recordType}", Span: call.Arguments[0].Span));
            return SandboxType.Unit;
        }

        if (call.Arguments[1] is not LiteralExpression { Value: I32Value index })
        {
            diagnostics.Add(new SandboxDiagnostic("E-CALL-RECORD-INDEX", "record.get field index must be a constant I32", Span: call.Arguments[1].Span));
            return SandboxType.Unit;
        }

        if (index.Value < 0 || index.Value >= recordType.Arguments.Count)
        {
            diagnostics.Add(new SandboxDiagnostic("E-CALL-RECORD-INDEX", $"record.get field index {index.Value} is out of range", Span: call.Arguments[1].Span));
            return SandboxType.Unit;
        }

        return recordType.Arguments[index.Value];
    }

    private SandboxType AnalyzeRecordFieldsFallback(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        foreach (var argument in call.Arguments)
        {
            _ = analyzeExpression(argument, scope, ref effects, ref canReorder);
        }

        return SandboxType.Unit;
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
