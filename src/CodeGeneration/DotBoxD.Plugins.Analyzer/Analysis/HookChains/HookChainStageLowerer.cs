using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainStageLowerer
{
    public static DotBoxDStatementBodyModel CreateShouldHandle(
        IReadOnlyList<HookChainStage> stages,
        INamedTypeSymbol eventType,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
        => BuildShouldHandle(
            stages,
            index: 0,
            current: null,
            currentType: null,
            eventType,
            eventProperties,
            model,
            cancellationToken,
            capabilities,
            effects);

    public static DotBoxDHandleModel CreateHandle(
        IReadOnlyList<HookChainStage> stages,
        string terminalElementParam,
        string? terminalContextParam,
        ITypeSymbol? terminalContextType,
        InvocationExpressionSyntax sendInvocation,
        INamedTypeSymbol eventType,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        var projection = ApplySelects(
            stages, eventType, eventProperties, model, cancellationToken, capabilities, effects);

        var context = HookChainExpressionLoweringContextFactory.Create(
            terminalElementParam,
            terminalContextParam,
            terminalContextType,
            eventProperties,
            projection.Current,
            projection.CurrentType,
            eventType,
            model,
            cancellationToken,
            capabilities,
            effects);
        var handle = DotBoxDHandleModelFactory.CreateFromSend(sendInvocation, context);
        return new DotBoxDHandleModel(handle.Target, handle.Message, projection.Prefix);
    }

    public static HookChainProjection? CreateProjection(
        IReadOnlyList<HookChainStage> stages,
        INamedTypeSymbol eventType,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        var projection = ApplySelects(
            stages, eventType, eventProperties, model, cancellationToken, capabilities, effects);
        return projection.Current is null
            ? null
            : new HookChainProjection(projection.Prefix, projection.Current, projection.CurrentType);
    }

    private static ProjectionState ApplySelects(
        IReadOnlyList<HookChainStage> stages,
        INamedTypeSymbol eventType,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        DotBoxDExpressionModel? current = null;
        ITypeSymbol? currentType = null;
        DotBoxDStatementBodyModel? prefix = null;
        for (var i = 0; i < stages.Count; i++)
        {
            if (!stages[i].IsSelect)
            {
                continue;
            }

            var projection = LowerSelect(
                stages[i], current, currentType, eventType, eventProperties,
                model, cancellationToken, capabilities, effects);
            prefix = prefix is null
                ? projection.Assignment
                : DotBoxDStatementBodyModelFactory.Concat(prefix, projection.Assignment);
            current = projection.Current;
            currentType = projection.CurrentType;
        }

        return new ProjectionState(prefix, current, currentType);
    }

    private static DotBoxDStatementBodyModel BuildShouldHandle(
        IReadOnlyList<HookChainStage> stages,
        int index,
        DotBoxDExpressionModel? current,
        ITypeSymbol? currentType,
        INamedTypeSymbol eventType,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        if (index >= stages.Count)
        {
            return DotBoxDConditionBodyModelFactory.AlwaysTrue();
        }

        var stage = stages[index];
        if (stage.IsSelect)
        {
            if (!HasWhereAtOrAfter(stages, index + 1))
            {
                return DotBoxDConditionBodyModelFactory.AlwaysTrue();
            }

            var projection = LowerSelect(
                stage, current, currentType, eventType, eventProperties,
                model, cancellationToken, capabilities, effects);
            var next = BuildShouldHandle(
                stages,
                index + 1,
                projection.Current,
                projection.CurrentType,
                eventType,
                eventProperties,
                model,
                cancellationToken,
                capabilities,
                effects);
            return DotBoxDStatementBodyModelFactory.Concat(projection.Assignment, next);
        }

        var (elementParam, contextParam) = HookChainStageLambdaReader.Parameters(stage.Lambda);
        if (elementParam is null || stage.Lambda.ExpressionBody is not { } body)
        {
            throw new NotSupportedException();
        }

        var context = HookChainExpressionLoweringContextFactory.Create(
            elementParam,
            contextParam,
            HookChainStageLambdaReader.ContextType(stage.Lambda, contextParam, model, cancellationToken),
            eventProperties,
            current,
            currentType,
            eventType,
            model,
            cancellationToken,
            capabilities,
            effects);
        var whenTrue = BuildShouldHandle(
            stages,
            index + 1,
            current,
            currentType,
            eventType,
            eventProperties,
            model,
            cancellationToken,
            capabilities,
            effects);
        return DotBoxDConditionBodyModelFactory.CreateBranch(
            body,
            whenTrue,
            DotBoxDConditionBodyModelFactory.AlwaysFalse(),
            context);
    }

    private static Projection LowerSelect(
        HookChainStage stage,
        DotBoxDExpressionModel? current,
        ITypeSymbol? currentType,
        INamedTypeSymbol eventType,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        var (elementParam, contextParam) = HookChainStageLambdaReader.Parameters(stage.Lambda);
        if (elementParam is null || stage.Lambda.ExpressionBody is not { } body)
        {
            throw new NotSupportedException();
        }

        var context = HookChainExpressionLoweringContextFactory.Create(
            elementParam,
            contextParam,
            HookChainStageLambdaReader.ContextType(stage.Lambda, contextParam, model, cancellationToken),
            eventProperties,
            current,
            currentType,
            eventType,
            model,
            cancellationToken,
            capabilities,
            effects);
        var value = DotBoxDExpressionModelFactory.Create(body, context);
        var name = SelectTemp(stage.Lambda);

        // Carry the projection's CLR type so a downstream stage can read its fields by name (record.get).
        var bodyTypeInfo = model.GetTypeInfo(body, cancellationToken);
        var bodyType = bodyTypeInfo.ConvertedType ?? bodyTypeInfo.Type;

        return new Projection(
            DotBoxDStatementBodyModelFactory.Assign(name, value),
            DotBoxDStatementBodyModelFactory.Variable(name, value.Type),
            bodyType);
    }

    private static bool HasWhereAtOrAfter(IReadOnlyList<HookChainStage> stages, int index)
    {
        for (var i = index; i < stages.Count; i++)
        {
            if (!stages[i].IsSelect)
            {
                return true;
            }
        }

        return false;
    }

    internal static string SelectTemp(LambdaExpressionSyntax lambda)
        => "$dotboxd.select." + lambda.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private sealed record Projection(DotBoxDStatementBodyModel Assignment, DotBoxDExpressionModel Current, ITypeSymbol? CurrentType);
    private sealed record ProjectionState(DotBoxDStatementBodyModel? Prefix, DotBoxDExpressionModel? Current, ITypeSymbol? CurrentType);
}

internal sealed record HookChainProjection(DotBoxDStatementBodyModel? Prefix, DotBoxDExpressionModel Value, ITypeSymbol? ValueType);
