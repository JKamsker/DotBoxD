using System.Globalization;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution;

using DotBoxD.Kernels;

internal sealed class InterpreterDebugTraceValidator(
    ExecutionPlan plan,
    SandboxExecutionOptions options,
    SandboxRunId runId)
{
    public bool Matches(SandboxAuditEvent auditEvent)
    {
        if (!EnvelopeMatches(auditEvent, out var functionId, out var category, out var nodeKind))
        {
            return false;
        }

        return category == "binding"
            ? BindingTraceMatches(auditEvent, functionId, nodeKind)
            : NodeTraceMatches(auditEvent, category);
    }

    private bool EnvelopeMatches(
        SandboxAuditEvent auditEvent,
        out string functionId,
        out string category,
        out string nodeKind)
    {
        functionId = string.Empty;
        category = string.Empty;
        nodeKind = string.Empty;
        return options.EnableDebugTrace &&
            auditEvent.RunId == runId &&
            auditEvent.Success &&
            auditEvent.ResourceId is null &&
            WorkerAuditValidator.CommonEnvelopeMatches(plan, auditEvent) &&
            FieldsMatch(auditEvent, out functionId, out category, out nodeKind);
    }

    private bool BindingTraceMatches(
        SandboxAuditEvent auditEvent,
        string functionId,
        string nodeKind)
        => auditEvent.BindingId == nodeKind &&
           plan.BindingReferences.TryGetValue(functionId, out var functionBindings) &&
           functionBindings.Contains(nodeKind) &&
           plan.Bindings.TryGet(nodeKind, out var binding) &&
           auditEvent.CapabilityId == binding.RequiredCapability &&
           auditEvent.Effect == binding.Effects;

    private static bool NodeTraceMatches(SandboxAuditEvent auditEvent, string category)
        => category is "statement" or "expression" &&
           auditEvent.BindingId is null &&
           auditEvent.CapabilityId is null &&
           auditEvent.Effect == SandboxEffect.None;

    private bool FieldsMatch(
        SandboxAuditEvent auditEvent,
        out string functionId,
        out string category,
        out string nodeKind)
    {
        functionId = string.Empty;
        category = string.Empty;
        nodeKind = string.Empty;
        return auditEvent.Fields is { Count: 7 } fields &&
            IdentityFieldsMatch(fields, out functionId, out category, out nodeKind) &&
            SourceFieldsMatch(fields);
    }

    private bool IdentityFieldsMatch(
        IReadOnlyDictionary<string, string> fields,
        out string functionId,
        out string category,
        out string nodeKind)
    {
        functionId = string.Empty;
        category = string.Empty;
        nodeKind = string.Empty;
        return FieldEquals(fields, "moduleHash", plan.ModuleHash) &&
            RequiredSafeField(fields, "functionId", out functionId) &&
            plan.FunctionLookup.ContainsKey(functionId) &&
            RequiredSafeField(fields, "category", out category) &&
            RequiredSafeField(fields, "nodeKind", out nodeKind);
    }

    private bool SourceFieldsMatch(IReadOnlyDictionary<string, string> fields)
        => RequiredInteger(fields, "sourceLine", out var sourceLine) &&
           sourceLine >= 0 &&
           RequiredInteger(fields, "sourceColumn", out var sourceColumn) &&
           sourceColumn >= 0 &&
           RequiredLong(fields, "fuelRemaining", out var fuelRemaining) &&
           fuelRemaining >= 0 &&
           fuelRemaining <= plan.Budget.MaxFuel;

    private static bool RequiredSafeField(
        IReadOnlyDictionary<string, string> fields,
        string name,
        out string value)
    {
        if (fields.TryGetValue(name, out var candidate) &&
            !string.IsNullOrWhiteSpace(candidate) &&
            WorkerAuditTextSafety.TextIsSafe(candidate))
        {
            value = candidate;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool RequiredInteger(
        IReadOnlyDictionary<string, string> fields,
        string name,
        out int value)
    {
        value = 0;
        return fields.TryGetValue(name, out var raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool RequiredLong(
        IReadOnlyDictionary<string, string> fields,
        string name,
        out long value)
    {
        value = 0;
        return fields.TryGetValue(name, out var raw) &&
            long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool FieldEquals(
        IReadOnlyDictionary<string, string> fields,
        string name,
        string expected)
        => fields.TryGetValue(name, out var value) &&
           string.Equals(value, expected, StringComparison.Ordinal);
}
