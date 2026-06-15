using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncCallShape
{
    private InvokeAsyncCallShape(
        BlockSyntax block,
        ITypeSymbol returnType,
        ITypeSymbol? captureType,
        bool usesReflectionCaptures,
        string parametersJson,
        string returnTypeJson,
        string argumentsExpression,
        EquatableArray<InvokeAsyncSyncOut> syncOuts,
        IReadOnlyList<(string Name, ExpressionSyntax Value)> leadingLocals,
        Func<AssignmentExpressionSyntax, Func<ExpressionSyntax, string>, string?>? assignmentOverride)
    {
        Block = block;
        ReturnType = returnType;
        CaptureType = captureType;
        UsesReflectionCaptures = usesReflectionCaptures;
        ParametersJson = parametersJson;
        ReturnTypeJson = returnTypeJson;
        ArgumentsExpression = argumentsExpression;
        SyncOuts = syncOuts;
        LeadingLocals = leadingLocals;
        AssignmentOverride = assignmentOverride;
    }

    public BlockSyntax Block { get; }

    public ITypeSymbol ReturnType { get; }

    public ITypeSymbol? CaptureType { get; }

    public bool UsesReflectionCaptures { get; }

    public string ParametersJson { get; }

    public string ReturnTypeJson { get; }

    public string ArgumentsExpression { get; }

    public EquatableArray<InvokeAsyncSyncOut> SyncOuts { get; }

    private IReadOnlyList<(string Name, ExpressionSyntax Value)> LeadingLocals { get; }

    private Func<AssignmentExpressionSyntax, Func<ExpressionSyntax, string>, string?>? AssignmentOverride { get; }

    public static InvokeAsyncCallShape? Create(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 1 && method.TypeArguments.Length == 1 &&
            TryLambda(arguments[0].Expression, out var lambda) &&
            lambda.Body is BlockSyntax block &&
            HasWorldParameter(lambda, model, cancellationToken))
        {
            return LambdaOnly(lambda, block, method.TypeArguments[0], model);
        }

        if (arguments.Count == 2 && method.TypeArguments.Length == 2 &&
            TryLambda(arguments[1].Expression, out lambda) &&
            lambda.Body is BlockSyntax captureBlock &&
            TryCaptureParameter(lambda, model, arguments[0].Expression, cancellationToken, out var captureParameter) &&
            !HasExternalCaptures(lambda, model))
        {
            return CaptureBag(method.TypeArguments[1], captureParameter, captureBlock, model);
        }

        return null;
    }

    public string LowerBody(DotBoxDRpcJsonLowerer lowerer, BlockSyntax block)
        => lowerer.LowerBody(block, LeadingLocals, ReturnLocalNames(), ReturnTypeJsonForBody(), AssignmentOverride);

    private static InvokeAsyncCallShape NoCapture(BlockSyntax block, ITypeSymbol returnType)
        => new(
            block,
            returnType,
            captureType: null,
            usesReflectionCaptures: false,
            parametersJson: "[]",
            returnTypeJson: DotBoxDRpcTypeMapper.JsonType(returnType),
            argumentsExpression: "global::System.Array.Empty<global::DotBoxD.Plugins.KernelRpcValue>()",
            default,
            [],
            assignmentOverride: null);

    private static InvokeAsyncCallShape CaptureBag(
        ITypeSymbol returnType,
        InvokeAsyncCaptureParameter captureParameter,
        BlockSyntax block,
        SemanticModel model)
    {
        var syncOuts = FindSyncOuts(block, captureParameter, model);
        var returnTypeJson = BuildReturnTypeJson(returnType, syncOuts);
        return new InvokeAsyncCallShape(
            block,
            returnType,
            captureParameter.Type,
            usesReflectionCaptures: false,
            CaptureParametersJson(captureParameter),
            returnTypeJson,
            CaptureArgumentsExpression(captureParameter.Type),
            new EquatableArray<InvokeAsyncSyncOut>(syncOuts),
            CreateLeadingLocals(syncOuts),
            (assignment, lower) => LowerCaptureAssignment(assignment, captureParameter, syncOuts, lower));
    }

    private IReadOnlyList<string> ReturnLocalNames()
    {
        var names = new string[SyncOuts.Count];
        for (var i = 0; i < SyncOuts.Count; i++)
        {
            names[i] = SyncOuts[i].LocalName;
        }

        return names;
    }

    private string? ReturnTypeJsonForBody()
        => SyncOuts.Count == 0 ? null : ReturnTypeJson;

    private static bool TryLambda(ExpressionSyntax expression, out LambdaExpressionSyntax lambda)
    {
        lambda = null!;
        if (expression is not LambdaExpressionSyntax found)
        {
            return false;
        }

        lambda = found;
        return true;
    }

    private static bool HasWorldParameter(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken)
        => lambda is ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized &&
           IsWorldParameter(parenthesized.ParameterList.Parameters[0], model, cancellationToken);

    private static bool TryCaptureParameter(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        ExpressionSyntax captureArgument,
        CancellationToken cancellationToken,
        out InvokeAsyncCaptureParameter parameter)
    {
        parameter = null!;
        if (lambda is not ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 2 } parenthesized ||
            !IsWorldParameter(parenthesized.ParameterList.Parameters[0], model, cancellationToken) ||
            model.GetTypeInfo(captureArgument, cancellationToken).Type is not INamedTypeSymbol captureType ||
            DotBoxDRpcTypeMapper.RecordFields(captureType).Count == 0)
        {
            return false;
        }

        var captureSyntax = parenthesized.ParameterList.Parameters[1];
        var declaredType = captureSyntax.Type is null
            ? null
            : model.GetTypeInfo(captureSyntax.Type, cancellationToken).Type;
        if (!SymbolEqualityComparer.Default.Equals(declaredType, captureType))
        {
            return false;
        }

        parameter = new InvokeAsyncCaptureParameter(captureSyntax.Identifier.ValueText, captureType);
        return true;
    }

    private static bool IsWorldParameter(
        ParameterSyntax parameter,
        SemanticModel model,
        CancellationToken cancellationToken)
        => parameter.Type is not null &&
           string.Equals(
               model.GetTypeInfo(parameter.Type, cancellationToken).Type?.ToDisplayString(),
               DotBoxDGenerationNames.Metadata.GameWorldAccessType,
               StringComparison.Ordinal);

    private static bool HasExternalCaptures(LambdaExpressionSyntax lambda, SemanticModel model)
        => ImplicitCaptureSet.Create(lambda, model) is { HasExternalCaptures: true };

    private static string BuildReturnTypeJson(ITypeSymbol returnType, IReadOnlyList<InvokeAsyncSyncOut> syncOuts)
    {
        if (syncOuts.Count == 0)
        {
            return DotBoxDRpcTypeMapper.JsonType(returnType);
        }

        var fields = new string[1 + syncOuts.Count];
        fields[0] = DotBoxDRpcTypeMapper.JsonType(returnType);
        for (var i = 0; i < syncOuts.Count; i++)
        {
            fields[i + 1] = DotBoxDRpcTypeMapper.JsonType(syncOuts[i].Type);
        }

        return "{\"name\":\"Record\",\"arguments\":[" + string.Join(",", fields) + "]}";
    }
}

internal sealed record InvokeAsyncCaptureParameter(string Name, INamedTypeSymbol Type);

internal sealed record InvokeAsyncSyncOut(
    string TargetName,
    ITypeSymbol Type,
    string LocalName,
    ExpressionSyntax? Initializer);
