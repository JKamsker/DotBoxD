using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncCallShape
{
    private InvokeAsyncCallShape(
        BlockSyntax block,
        ITypeSymbol worldType,
        string worldParameterName,
        ITypeSymbol returnType,
        ITypeSymbol? captureType,
        bool usesReflectionCaptures,
        string parametersJson,
        string returnTypeJson,
        string argumentsExpression,
        IReadOnlyList<ITypeSymbol> argumentTypes,
        EquatableArray<InvokeAsyncSyncOut> syncOuts,
        IReadOnlyList<(string Name, string Value)> leadingLocals,
        Func<AssignmentExpressionSyntax, Func<ExpressionSyntax, string>, string?>? assignmentOverride,
        Func<ExpressionSyntax, string?>? expressionOverride)
    {
        Block = block;
        WorldType = worldType;
        WorldParameterName = worldParameterName;
        ReturnType = returnType;
        CaptureType = captureType;
        UsesReflectionCaptures = usesReflectionCaptures;
        ParametersJson = parametersJson;
        ReturnTypeJson = returnTypeJson;
        ArgumentsExpression = argumentsExpression;
        ArgumentTypes = argumentTypes;
        SyncOuts = syncOuts;
        LeadingLocals = leadingLocals;
        AssignmentOverride = assignmentOverride;
        ExpressionOverride = expressionOverride;
    }

    public BlockSyntax Block { get; }

    public ITypeSymbol WorldType { get; }

    public string WorldParameterName { get; }

    public ITypeSymbol ReturnType { get; }

    public ITypeSymbol? CaptureType { get; }

    public bool UsesReflectionCaptures { get; }

    public string ParametersJson { get; }

    public string ReturnTypeJson { get; }

    public string ArgumentsExpression { get; }

    public IReadOnlyList<ITypeSymbol> ArgumentTypes { get; }

    public EquatableArray<InvokeAsyncSyncOut> SyncOuts { get; }

    private IReadOnlyList<(string Name, string Value)> LeadingLocals { get; }

    private Func<AssignmentExpressionSyntax, Func<ExpressionSyntax, string>, string?>? AssignmentOverride { get; }

    private Func<ExpressionSyntax, string?>? ExpressionOverride { get; }

    public string LowerBody(DotBoxDRpcJsonLowerer lowerer, BlockSyntax block)
        => lowerer.LowerBody(
            block,
            LeadingLocals,
            ReturnLocalNames(),
            ReturnTypeJsonForBody(),
            AssignmentOverride,
            ExpressionOverride,
            ReturnType);

    private static InvokeAsyncCallShape NoCapture(
        BlockSyntax block,
        ITypeSymbol worldType,
        string worldParameterName,
        ITypeSymbol returnType,
        Compilation compilation)
        => new(
            block,
            worldType,
            worldParameterName,
            returnType,
            captureType: null,
            usesReflectionCaptures: false,
            parametersJson: "[]",
            returnTypeJson: DotBoxDRpcReturnType.JsonType(returnType, compilation),
            argumentsExpression: $"global::System.Array.Empty<{DotBoxDRpcValueNames.GlobalKernelRpcValue}>()",
            argumentTypes: [],
            default,
            [],
            assignmentOverride: null,
            expressionOverride: null);

    private static InvokeAsyncCallShape CaptureBag(
        ITypeSymbol returnType,
        InvokeAsyncCaptureParameter captureParameter,
        BlockSyntax block,
        SemanticModel model,
        ITypeSymbol worldType,
        string worldParameterName)
    {
        var captureAliases = CaptureBagAliases(block, captureParameter.Name, model);
        var syncOuts = FindSyncOuts(block, captureParameter, model, captureAliases);
        var returnTypeJson = BuildReturnTypeJson(returnType, syncOuts, model.Compilation);
        return new InvokeAsyncCallShape(
            block,
            worldType,
            worldParameterName,
            returnType,
            captureParameter.Type,
            usesReflectionCaptures: false,
            CaptureParametersJson(captureParameter, model.Compilation),
            returnTypeJson,
            CaptureArgumentsExpression(captureParameter.Type),
            [captureParameter.Type],
            new EquatableArray<InvokeAsyncSyncOut>(syncOuts),
            CreateLeadingLocals(syncOuts),
            (assignment, lower) => LowerCaptureAssignment(
                assignment,
                captureParameter,
                syncOuts,
                captureAliases,
                model,
                lower),
            expression => LowerCaptureRead(expression, captureParameter, syncOuts, captureAliases, model));
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

    private static bool HasExternalCaptures(LambdaExpressionSyntax lambda, SemanticModel model)
        => ImplicitCaptureSet.Create(lambda, model) is { HasExternalCaptures: true };

}
