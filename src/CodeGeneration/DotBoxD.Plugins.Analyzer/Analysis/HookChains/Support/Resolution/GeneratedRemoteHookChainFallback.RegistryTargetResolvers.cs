using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    private static readonly RegistryTargetResolver[] RegistryTargetResolvers =
    [
        TargetFromRegistryType,
        TargetFromMemberAccess,
        TargetFromConditionalAccess,
        TargetFromTupleElementAccess,
        TargetFromAnonymousObjectPropertyAccess,
        TargetFromConditionalExpression,
        TargetFromCoalesceExpression,
        TargetFromSwitchExpression,
        TargetFromAssignmentExpression,
        TargetFromDeclaredOrLocalExpression,
    ];

    private delegate GeneratedRemoteHookChainTarget? RegistryTargetResolver(RegistryTargetContext context);

    private static GeneratedRemoteHookChainTarget? TargetFromRegistryType(RegistryTargetContext context)
        => context.Model.GetTypeInfo(context.Expression, context.CancellationToken).Type is INamedTypeSymbol registryType
            ? TargetFromRegistryMarker(registryType, context.Model.Compilation)
            : null;

    private static GeneratedRemoteHookChainTarget? TargetFromMemberAccess(RegistryTargetContext context)
        => context.Expression is MemberAccessExpressionSyntax registryAccess
            ? TargetFromGeneratedServerMember(registryAccess, context.Model, context.CancellationToken)
            : null;

    private static GeneratedRemoteHookChainTarget? TargetFromConditionalAccess(RegistryTargetContext context)
        => context.Expression is ConditionalAccessExpressionSyntax conditionalAccess
            ? TargetFromConditionalAccessGeneratedServerMember(conditionalAccess, context.Model, context.CancellationToken)
            : null;

    private static GeneratedRemoteHookChainTarget? TargetFromTupleElementAccess(RegistryTargetContext context)
        => context.Expression is MemberAccessExpressionSyntax tupleElementAccess
            ? TargetFromTupleElementAccess(tupleElementAccess, context.Model, context.CancellationToken, context.Depth)
            : null;

    private static GeneratedRemoteHookChainTarget? TargetFromAnonymousObjectPropertyAccess(RegistryTargetContext context)
        => context.Expression is MemberAccessExpressionSyntax anonymousObjectAccess
            ? TargetFromAnonymousObjectPropertyAccess(anonymousObjectAccess, context.Model, context.CancellationToken, context.Depth)
            : null;

    private static GeneratedRemoteHookChainTarget? TargetFromConditionalExpression(RegistryTargetContext context)
        => context.Expression is ConditionalExpressionSyntax conditional
            ? TargetFromConditionalRegistryExpression(conditional, context.Model, context.CancellationToken, context.Depth)
            : null;

    private static GeneratedRemoteHookChainTarget? TargetFromCoalesceExpression(RegistryTargetContext context)
        => context.Expression is BinaryExpressionSyntax coalesce
            ? TargetFromCoalesceRegistryExpression(coalesce, context.Model, context.CancellationToken, context.Depth)
            : null;

    private static GeneratedRemoteHookChainTarget? TargetFromSwitchExpression(RegistryTargetContext context)
        => context.Expression is SwitchExpressionSyntax switchExpression
            ? TargetFromSwitchRegistryExpression(switchExpression, context.Model, context.CancellationToken, context.Depth)
            : null;

    private static GeneratedRemoteHookChainTarget? TargetFromAssignmentExpression(RegistryTargetContext context)
        => context.Expression is AssignmentExpressionSyntax assignment
            ? TargetFromAssignmentRegistryExpression(assignment, context.Model, context.CancellationToken, context.Depth)
            : null;

    private static GeneratedRemoteHookChainTarget? TargetFromDeclaredOrLocalExpression(RegistryTargetContext context)
        => TargetFromDeclaredRegistryExpression(context.Expression, context.Model, context.CancellationToken) ??
           TargetFromLocalAlias(context.Expression, context.Model, context.CancellationToken, context.Depth);

    private readonly record struct RegistryTargetContext(
        ExpressionSyntax Expression,
        SemanticModel Model,
        CancellationToken CancellationToken,
        int Depth);
}
