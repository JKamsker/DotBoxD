using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpLiterals = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.CSharpLiterals;
using Helpers = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.Helpers;
using ManifestTypes = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.ManifestTypes;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

/// <summary>
/// Lowers a fluent hook-result builder chain — <c>Result.Ok().With&lt;Field&gt;(value)…</c> or
/// <c>Result.Reject(reason)</c> — directly to a single <c>record.new</c>. The generated <c>Ok</c>/<c>Reject</c>/
/// <c>With&lt;Field&gt;</c> members are emitted by the same generator pass, so their method symbols are NOT visible
/// while this chain is being lowered; recognition is therefore <b>syntactic</b> (by member name and arity),
/// resolving only the result <i>type</i> at the chain seed (which already exists). The chain is walked once and the field-source
/// array tracked structurally — the <c>Ok</c>/<c>Reject</c> seed sets <c>Success</c>/<c>Reason</c>, each
/// <c>With&lt;Field&gt;</c> overrides one slot, omitted slots take their manifest-tag zero — so the record is
/// materialised once with no quadratic <c>record.get</c> copying. Only a chain whose seed is a marshaller-eligible
/// <c>[HookResult]</c> record lowers; anything else returns null so the caller can try the next handler.
/// </summary>
internal static partial class DotBoxDResultBuilderExpressionLowerer
{
    private const string OkMethod = "Ok";
    private const string RejectMethod = "Reject";
    private const string WithPrefix = "With";
    private const string SuccessField = "Success";
    private const string ReasonField = "Reason";

    public static DotBoxDExpressionModel? TryLower(
        InvocationExpressionSyntax invocation,
        DotBoxDExpressionLoweringContext context,
        System.Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (!IsBuilderInvocation(invocation) ||
            ResolveSeedResultType(invocation, context.SemanticModel, context.CancellationToken) is not { } resultType ||
            SandboxTypeSourceEmitter.TryEmit(resultType) is not { } recordTypeSource)
        {
            return null;
        }

        var fields = DotBoxDRpcTypeMapper.RecordFields(resultType);
        var sources = new string?[fields.Count];
        if (!TryApply(invocation, fields, sources, context, lowerExpression))
        {
            return null;
        }

        // Omitted fields take their manifest-tag zero. Deliberate, documented divergence for an omitted string
        // `Reason` (e.g. `Ok()` / `Reject()` with no argument): kernel IR strings are non-null, so the zero is the
        // empty string "", whereas the in-process generated builder (run by RegisterLocal / direct host calls)
        // leaves `Reason` at its C# default of null. `Reason` is only meaningful paired with `Success == false`,
        // which dispatch drops before returning, so the gap is unobservable in normal dispatch; a test pins the
        // convention so a future change that surfaces `Reason` cannot let the two transports silently diverge.
        for (var i = 0; i < fields.Count; i++)
        {
            sources[i] ??= DotBoxDRecordCreationExpressionLowerer.ZeroSource(fields[i].Type);
        }

        context.Effects?.Add(DotBoxDGenerationNames.Effects.Alloc);
        return new DotBoxDExpressionModel(
            DotBoxDRecordCreationExpressionLowerer.RecordNew(System.Array.ConvertAll(sources, static s => s!), recordTypeSource),
            ManifestTypes.Record,
            true);
    }

    private static bool IsBuilderInvocation(InvocationExpressionSyntax invocation)
        => invocation.Expression is MemberAccessExpressionSyntax member && IsBuilderName(member.Name.Identifier.ValueText);

    private static bool IsBuilderName(string name)
        => string.Equals(name, OkMethod, StringComparison.Ordinal)
            || string.Equals(name, RejectMethod, StringComparison.Ordinal)
            || IsWithName(name);

    private static bool IsWithName(string name)
        => name.Length > WithPrefix.Length && name.StartsWith(WithPrefix, StringComparison.Ordinal);

    // Applies one builder call to the field-source array, recursing into the receiver for a With<Field> hop.
    private static bool TryApply(
        InvocationExpressionSyntax invocation,
        IReadOnlyList<RecordMember> fields,
        string?[] sources,
        DotBoxDExpressionLoweringContext context,
        System.Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return false;
        }

        var name = member.Name.Identifier.ValueText;
        var arguments = invocation.ArgumentList.Arguments;

        if (string.Equals(name, OkMethod, StringComparison.Ordinal))
        {
            return arguments.Count == 0 &&
                SetByName(fields, sources, SuccessField, BoolSource(value: true));
        }

        if (string.Equals(name, RejectMethod, StringComparison.Ordinal))
        {
            return TryApplyReject(fields, sources, arguments, lowerExpression);
        }

        return IsWithName(name) &&
            TryApplyWith(member, name, arguments, fields, sources, context, lowerExpression);
    }

    private static bool TryApplyReject(
        IReadOnlyList<RecordMember> fields,
        string?[] sources,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        System.Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (arguments.Count > 1 ||
            !SetByName(fields, sources, SuccessField, BoolSource(value: false)))
        {
            return false;
        }

        if (arguments.Count != 1)
        {
            return true;
        }

        var reason = lowerExpression(arguments[0].Expression);
        return string.Equals(reason.Type, ManifestTypes.String, StringComparison.Ordinal)
            && SetByName(fields, sources, ReasonField, reason.Source);
    }

    private static bool TryApplyWith(
        MemberAccessExpressionSyntax member,
        string name,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IReadOnlyList<RecordMember> fields,
        string?[] sources,
        DotBoxDExpressionLoweringContext context,
        System.Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (!TryGetWritableFieldIndex(name, arguments, fields, out var index))
        {
            return false;
        }

        if (member.Expression is not InvocationExpressionSyntax receiver ||
            !TryApply(receiver, fields, sources, context, lowerExpression))
        {
            return false;
        }

        if (!TryLowerBuilderFieldArgument(arguments[0].Expression, fields[index], context, lowerExpression, out var argument))
        {
            return false;
        }

        sources[index] = argument.Source;
        return true;
    }

    private static bool TryGetWritableFieldIndex(
        string name,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IReadOnlyList<RecordMember> fields,
        out int index)
    {
        index = -1;
        if (arguments.Count != 1)
        {
            return false;
        }

        var fieldName = name.Substring(WithPrefix.Length);
        if (string.Equals(fieldName, SuccessField, StringComparison.Ordinal) ||
            string.Equals(fieldName, ReasonField, StringComparison.Ordinal))
        {
            return false;
        }

        index = FieldIndex(fields, fieldName);
        return index >= 0;
    }

    private static bool TryLowerBuilderFieldArgument(
        ExpressionSyntax expression,
        RecordMember field,
        DotBoxDExpressionLoweringContext context,
        System.Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression,
        out DotBoxDExpressionModel argument)
    {
        var expectedType = SandboxTypeSourceEmitter.ManifestTag(field.Type);
        argument = DotBoxDNullableScalarExpressionLowerer.TryLower(
            expression,
            field.Type,
            context,
            lowerExpression,
            out var nullable)
            ? nullable
            : lowerExpression(expression);
        return string.Equals(argument.Type, expectedType, StringComparison.Ordinal) ||
            TryPromoteBuilderFieldArgument(expression, context, expectedType, ref argument);
    }

    private static bool TryPromoteBuilderFieldArgument(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        string expectedType,
        ref DotBoxDExpressionModel argument)
    {
        if (!DotBoxDGenerationNames.ManifestTypes.IsNumeric(expectedType) ||
            DotBoxDNumericConstantPromoter.TryPromoteConstant(expression, context, expectedType) is not { } promoted)
        {
            return false;
        }

        argument = promoted;
        return true;
    }

    private static bool SetByName(IReadOnlyList<RecordMember> fields, string?[] sources, string name, string source)
    {
        var index = FieldIndex(fields, name);
        if (index < 0)
        {
            return false;
        }

        sources[index] = source;
        return true;
    }

    private static string BoolSource(bool value)
        => $"{Helpers.Bool}({(value ? CSharpLiterals.True : CSharpLiterals.False)})";

    private static int FieldIndex(IReadOnlyList<RecordMember> fields, string name)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

}
