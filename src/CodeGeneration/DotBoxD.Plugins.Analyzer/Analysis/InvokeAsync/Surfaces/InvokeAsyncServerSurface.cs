using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncServerSurface
{
    private const string InvokeAsyncMethod = "InvokeAsync";
    private const string PluginServerInterfaceMetadataName = "DotBoxD.Abstractions.IPluginServer`1";

    public static bool TryResolveImplicitGeneratedFacade(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken,
        out string receiverType,
        out string? serverAccessType,
        out INamedTypeSymbol worldType)
    {
        receiverType = string.Empty;
        serverAccessType = null;
        worldType = null!;
        var containingType = model.GetEnclosingSymbol(invocation.SpanStart, cancellationToken)?.ContainingType;
        return containingType is not null &&
               InvokeAsyncReceiverResolver.TryResolveGeneratedFacadeType(
                   containingType,
                   out receiverType,
                   out serverAccessType,
                   out worldType);
    }

    public static bool IsDotBoxDInvokeAsync(
        IMethodSymbol? method,
        Compilation compilation)
    {
        if (method is null)
        {
            return false;
        }

        if (!string.Equals(method.Name, InvokeAsyncMethod, StringComparison.Ordinal))
        {
            return false;
        }

        if (!HasExplicitIrInvocationCompanion(method, compilation))
        {
            return false;
        }

        if (IsPluginServerType(method.ContainingType, compilation))
        {
            return true;
        }

        return IsGeneratedPluginServerFacadeType(method.ContainingType);
    }

    public static bool BindsToUserInvokeAsync(
        IMethodSymbol? method,
        Compilation compilation)
    {
        if (method is null || method.ContainingType.TypeKind == TypeKind.Error)
        {
            return false;
        }

        if (string.Equals(method.Name, InvokeAsyncMethod, StringComparison.Ordinal) &&
            IsGeneratedPluginServerFacadeType(method.ContainingType) &&
            HasExplicitIrInvocationCompanion(method, compilation))
        {
            return false;
        }

        return !LowerToIrMethodReader.IsAnonymousInvocation(method, compilation) &&
            !IsPluginServerType(method.ContainingType, compilation);
    }

    public static bool IsLoweringCandidate(
        IMethodSymbol? method,
        Compilation compilation,
        InvocationExpressionSyntax invocation)
    {
        if (method is not null)
        {
            return LowerToIrMethodReader.IsAnonymousInvocation(method, compilation) ||
                (string.Equals(method.Name, InvokeAsyncMethod, StringComparison.Ordinal) &&
                 HasExplicitIrInvocationCompanion(method, compilation));
        }

        return InvocationName(invocation) is { } name &&
            string.Equals(name.Identifier.ValueText, InvokeAsyncMethod, StringComparison.Ordinal);
    }

    public static bool TryCreateLowerToIrMethodDiagnostic(
        IMethodSymbol? method,
        Compilation compilation,
        InvocationExpressionSyntax invocation,
        out PluginKernelDiagnostic diagnostic)
    {
        diagnostic = null!;
        if (!LowerToIrMethodReader.TryReadUnsupportedKind(method, compilation, out var kind))
        {
            return false;
        }

        diagnostic = new PluginKernelDiagnostic(
            "[LowerToIrMethod] uses unsupported LoweredIrMethodKind value '" + kind +
            "'. Supported value: LoweredIrMethodKind.AnonymousInvocation.",
            PluginDiagnosticLocation.From(invocation.GetLocation()));
        return true;
    }

    public static IMethodSymbol? ResolvedMethod(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var info = model.GetSymbolInfo(invocation, cancellationToken);
        return info.Symbol as IMethodSymbol ??
            (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] as IMethodSymbol : null);
    }

    public static bool TryResolve(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out string receiverType,
        out string? serverAccessType,
        out INamedTypeSymbol worldType)
        => InvokeAsyncReceiverResolver.TryResolve(
            model,
            receiver,
            cancellationToken,
            out receiverType,
            out serverAccessType,
            out worldType);

    private static bool IsPluginServerType(ITypeSymbol? type, Compilation compilation)
    {
        if (type is not INamedTypeSymbol named ||
            compilation.GetTypeByMetadataName(PluginServerInterfaceMetadataName) is not { } pluginServerType)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, pluginServerType);
    }

    private static bool HasExplicitIrInvocationCompanion(IMethodSymbol method, Compilation compilation)
    {
        var lambdaIndex = LambdaParameterIndex(method);
        if (lambdaIndex < 0)
        {
            return false;
        }

        var irIndex = lambdaIndex + 1;
        if (method.Parameters.Length <= irIndex ||
            !IsOptionalNull(method.Parameters[irIndex]) ||
            !HasIRBodyOf(method.Parameters[irIndex], method.Parameters[lambdaIndex].Name, compilation))
        {
            return false;
        }

        return IsIRInvocation(method.Parameters[irIndex].Type);
    }

    private static int LambdaParameterIndex(IMethodSymbol method)
    {
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (method.Parameters[i].Type.TypeKind == TypeKind.Delegate)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsOptionalNull(IParameterSymbol parameter)
        => parameter.IsOptional && parameter.HasExplicitDefaultValue && parameter.ExplicitDefaultValue is null;

    private static bool HasIRBodyOf(IParameterSymbol parameter, string sourceParameterName, Compilation compilation)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (IsDotBoxDAttribute(attribute, compilation, DotBoxDGenerationNames.TypeNames.IRBodyOfAttribute) &&
                attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is string parameterName &&
                string.Equals(parameterName, sourceParameterName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsIRInvocation(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var definition = named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return string.Equals(
                definition,
                DotBoxDGenerationNames.TypeNames.GlobalPrefix +
                DotBoxDGenerationNames.TypeNames.IRInvocation2Original,
                StringComparison.Ordinal) ||
            string.Equals(
                definition,
                DotBoxDGenerationNames.TypeNames.GlobalPrefix +
                DotBoxDGenerationNames.TypeNames.IRInvocation3Original,
                StringComparison.Ordinal);
    }

    private static bool IsDotBoxDAttribute(AttributeData attribute, Compilation compilation, string metadataName)
        => compilation.GetTypeByMetadataName(metadataName) is { } expected &&
           SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected);

    private static bool IsGeneratedPluginServerFacadeType(ITypeSymbol? type)
        => type is INamedTypeSymbol named &&
           InvokeAsyncReceiverResolver.TryResolveGeneratedFacadeType(named, out _, out _, out _);

    private static SimpleNameSyntax? InvocationName(InvocationExpressionSyntax invocation)
        => invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier,
            GenericNameSyntax generic => generic,
            MemberAccessExpressionSyntax access => access.Name,
            MemberBindingExpressionSyntax binding => binding.Name,
            _ => null,
        };
}
