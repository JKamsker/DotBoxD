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
        ITypeSymbol worldType,
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
        WorldType = worldType;
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

    public ITypeSymbol WorldType { get; }

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
            TryWorldParameter(lambda, model, cancellationToken, out var worldType))
        {
            return LambdaOnly(lambda, block, worldType, method.TypeArguments[0], model);
        }

        if (arguments.Count == 2 && method.TypeArguments.Length == 2 &&
            TryLambda(arguments[1].Expression, out lambda) &&
            lambda.Body is BlockSyntax captureBlock &&
            TryCaptureParameter(
                lambda,
                model,
                arguments[0].Expression,
                cancellationToken,
                out var captureParameter,
                out worldType) &&
            !HasExternalCaptures(lambda, model))
        {
            return CaptureBag(method.TypeArguments[1], captureParameter, captureBlock, model, worldType);
        }

        return null;
    }

    public static InvokeAsyncCallShape? Create(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol generatedWorldType,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 1 &&
            TryLambda(arguments[0].Expression, out var lambda) &&
            lambda.Body is BlockSyntax block &&
            TryWorldParameter(lambda, model, cancellationToken, generatedWorldType, out var worldType) &&
            TryReturnType(block, model, cancellationToken, out var returnType))
        {
            return LambdaOnly(lambda, block, worldType, returnType, model);
        }

        if (arguments.Count == 2 &&
            TryLambda(arguments[1].Expression, out lambda) &&
            lambda.Body is BlockSyntax captureBlock &&
            TryCaptureParameter(
                lambda,
                model,
                arguments[0].Expression,
                cancellationToken,
                generatedWorldType,
                out var captureParameter,
                out worldType) &&
            TryReturnType(captureBlock, model, cancellationToken, out returnType) &&
            !HasExternalCaptures(lambda, model))
        {
            return CaptureBag(returnType, captureParameter, captureBlock, model, worldType);
        }

        return null;
    }

    public string LowerBody(DotBoxDRpcJsonLowerer lowerer, BlockSyntax block)
        => lowerer.LowerBody(block, LeadingLocals, ReturnLocalNames(), ReturnTypeJsonForBody(), AssignmentOverride);

    private static InvokeAsyncCallShape NoCapture(BlockSyntax block, ITypeSymbol worldType, ITypeSymbol returnType)
        => new(
            block,
            worldType,
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
        SemanticModel model,
        ITypeSymbol worldType)
    {
        var syncOuts = FindSyncOuts(block, captureParameter, model);
        var returnTypeJson = BuildReturnTypeJson(returnType, syncOuts);
        return new InvokeAsyncCallShape(
            block,
            worldType,
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

    private static bool TryWorldParameter(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ITypeSymbol worldType)
        => TryWorldParameter(lambda, model, cancellationToken, expectedWorldType: null, out worldType);

    private static bool TryWorldParameter(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken,
        ITypeSymbol? expectedWorldType,
        out ITypeSymbol worldType)
    {
        worldType = null!;
        if (LambdaParameterTypes(lambda, model, cancellationToken) is not { Count: 1 } parameterTypes)
        {
            return false;
        }

        worldType = parameterTypes[0];
        return MatchesExpectedWorld(worldType, expectedWorldType);
    }

    private static bool TryCaptureParameter(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        ExpressionSyntax captureArgument,
        CancellationToken cancellationToken,
        out InvokeAsyncCaptureParameter parameter,
        out ITypeSymbol worldType)
        => TryCaptureParameter(
            lambda,
            model,
            captureArgument,
            cancellationToken,
            expectedWorldType: null,
            out parameter,
            out worldType);

    private static bool TryCaptureParameter(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        ExpressionSyntax captureArgument,
        CancellationToken cancellationToken,
        ITypeSymbol? expectedWorldType,
        out InvokeAsyncCaptureParameter parameter,
        out ITypeSymbol worldType)
    {
        parameter = null!;
        worldType = null!;
        if (lambda is not ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 2 } parenthesized ||
            LambdaParameterTypes(lambda, model, cancellationToken) is not { Count: 2 } parameterTypes ||
            model.GetTypeInfo(captureArgument, cancellationToken).Type is not INamedTypeSymbol captureType ||
            DotBoxDRpcTypeMapper.RecordFields(captureType).Count == 0)
        {
            return false;
        }

        worldType = parameterTypes[0];
        if (!MatchesExpectedWorld(worldType, expectedWorldType))
        {
            return false;
        }

        var captureSyntax = parenthesized.ParameterList.Parameters[1];
        var declaredType = captureSyntax.Type is null
            ? null
            : model.GetTypeInfo(captureSyntax.Type, cancellationToken).Type;
        if (declaredType is not null &&
            !SymbolEqualityComparer.Default.Equals(declaredType, captureType))
        {
            return false;
        }

        if (!SymbolEqualityComparer.Default.Equals(parameterTypes[1], captureType))
        {
            return false;
        }

        parameter = new InvokeAsyncCaptureParameter(captureSyntax.Identifier.ValueText, captureType);
        return true;
    }

    private static IReadOnlyList<ITypeSymbol>? LambdaParameterTypes(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetTypeInfo(lambda, cancellationToken).ConvertedType is INamedTypeSymbol
            {
                DelegateInvokeMethod: { } invoke
            })
        {
            return invoke.Parameters.Select(parameter => parameter.Type).ToArray();
        }

        return ExplicitLambdaParameterTypes(lambda, model, cancellationToken);
    }

    private static IReadOnlyList<ITypeSymbol>? ExplicitLambdaParameterTypes(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var parameterTypes = lambda switch
        {
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters
                .Select(parameter => parameter.Type is null ? null : model.GetTypeInfo(parameter.Type, cancellationToken).Type)
                .ToArray(),
            SimpleLambdaExpressionSyntax { Parameter.Type: { } type } => [model.GetTypeInfo(type, cancellationToken).Type],
            _ => []
        };
        if (parameterTypes.Length == 0)
        {
            return null;
        }

        var resolved = new ITypeSymbol[parameterTypes.Length];
        for (var i = 0; i < parameterTypes.Length; i++)
        {
            if (parameterTypes[i] is not { } parameterType)
            {
                return null;
            }

            resolved[i] = parameterType;
        }

        return resolved;
    }

    private static bool MatchesExpectedWorld(ITypeSymbol worldType, ITypeSymbol? expectedWorldType)
        => expectedWorldType is null ||
           SymbolEqualityComparer.Default.Equals(worldType, expectedWorldType);

    private static bool TryReturnType(
        BlockSyntax block,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ITypeSymbol returnType)
    {
        returnType = null!;
        foreach (var statement in block.DescendantNodes().OfType<ReturnStatementSyntax>())
        {
            if (statement.Expression is null ||
                model.GetTypeInfo(statement.Expression, cancellationToken).Type is not { } current)
            {
                return false;
            }

            if (returnType is null)
            {
                returnType = current;
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(returnType, current))
            {
                return false;
            }
        }

        return returnType is not null;
    }

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
