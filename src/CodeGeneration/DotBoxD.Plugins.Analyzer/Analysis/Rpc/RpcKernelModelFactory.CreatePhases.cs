using DotBoxD.Plugins.Analyzer.Analysis.Debugging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static partial class RpcKernelModelFactory
{
    private static bool TryGetTarget(
        GeneratorAttributeSyntaxContext context,
        out INamedTypeSymbol type,
        out ClassDeclarationSyntax declaration)
    {
        if (context.TargetSymbol is INamedTypeSymbol targetType &&
            context.TargetNode is ClassDeclarationSyntax targetDeclaration)
        {
            type = targetType;
            declaration = targetDeclaration;
            return true;
        }

        type = null!;
        declaration = null!;
        return false;
    }

    private static RpcKernelModelResult? ValidateTarget(
        INamedTypeSymbol type,
        ClassDeclarationSyntax declaration,
        IReadOnlyList<AttributeData> attributes)
    {
        var pluginId = PluginId(attributes, type.Name);
        if (PluginIdValidation.ErrorMessage(pluginId) is { } pluginIdError)
        {
            return Fail(declaration, pluginIdError);
        }

        if (type.IsGenericType || type.TypeParameters.Length > 0)
        {
            return Fail(declaration, $"Generated server extension '{type.Name}' cannot be generic.");
        }

        return type.ContainingType is not null
            ? Fail(declaration, $"Server extension kernels must be top-level types; '{type.ToDisplayString()}' is nested.")
            : null;
    }

    private static RpcKernelModelResult CreateValidated(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken,
        INamedTypeSymbol type,
        string pluginId,
        INamedTypeSymbol? serviceType,
        INamedTypeSymbol? graftType)
    {
        var method = ResolveValidatedBatchMethod(type, context, cancellationToken, graftType, out var liveSettings, out var graft);
        var client = ResolveClientGeneration(type, method, serviceType, graft, context);
        var body = LowerRpcBody(context, cancellationToken, type, method, graft);
        var methodBody = MethodBody(method, cancellationToken);
        var sourceNode = (SyntaxNode?)methodBody.Block ?? methodBody.Expression!;
        var debugSource = KernelSourceLocationModel.CreateWithKernelMethods(
            pluginId + ":" + method.Name,
            sourceNode,
            context.SemanticModel,
            cancellationToken);
        var debugBindings = DebugBindings(method, liveSettings, body.HasReceiverId);
        var package = EmitPackage(
            type,
            pluginId,
            method,
            body.BodyJson,
            body.Effects,
            body.Capabilities,
            liveSettings,
            serviceType,
            client.ServiceMethod,
            client.ClientExtensions,
            client.DirectClientMethod,
            graft,
            body.HasReceiverId,
            context.SemanticModel.Compilation,
            debugSource,
            debugBindings);
        var grafts = RpcKernelGraftSignatureFactory.Create(
            type,
            method,
            client.ServiceMethod,
            client.ClientExtensions,
            client.DirectClientMethod,
            graft);
        return new RpcKernelModelResult(package, null, grafts);
    }

    private static IReadOnlyList<(string SlotName, string SourceName)> DebugBindings(
        IMethodSymbol method,
        EquatableArray<LiveSettingModel> liveSettings,
        bool hasReceiverId)
    {
        var bindings = new List<(string, string)>();
        if (hasReceiverId)
        {
            bindings.Add((RpcKernelReceiverHandleSeeder.ReceiverIdParameter, "this"));
        }

        bindings.AddRange(method.Parameters.Take(method.Parameters.Length - 1)
            .Select(parameter => (parameter.Name, parameter.Name)));
        bindings.AddRange(liveSettings.Select(setting => (setting.Name, setting.Name)));
        return bindings;
    }

    private static IMethodSymbol ResolveValidatedBatchMethod(
        INamedTypeSymbol type,
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken,
        INamedTypeSymbol? graftType,
        out EquatableArray<LiveSettingModel> liveSettings,
        out RpcServerExtensionGraft? graft)
    {
        graft = RpcServerExtensionGraft.Create(type, graftType);
        var method = ResolveBatchMethod(type, context.SemanticModel.Compilation);
        ValidateBatchMethodParameters(method);
        liveSettings = PluginSymbolReader.LiveSettings(type, context.SemanticModel, cancellationToken);
        if (ContainsUnsupported(liveSettings))
        {
            throw new NotSupportedException("Live settings must use supported scalar types.");
        }

        ValidateGeneratedParameterNames(method, liveSettings, graft);
        return method;
    }

    private static RpcClientGeneration ResolveClientGeneration(
        INamedTypeSymbol type,
        IMethodSymbol method,
        INamedTypeSymbol? serviceType,
        RpcServerExtensionGraft? graft,
        GeneratorAttributeSyntaxContext context)
    {
        if (serviceType is not null)
        {
            return ResolveServiceClientGeneration(type, method, serviceType, context);
        }

        if (graft is not null)
        {
            return ResolveGraftedClientGeneration(type, method, graft);
        }

        RejectUnsupportedClientGeneration(type, method);
        return default;
    }

    private static RpcClientGeneration ResolveServiceClientGeneration(
        INamedTypeSymbol type,
        IMethodSymbol method,
        INamedTypeSymbol serviceType,
        GeneratorAttributeSyntaxContext context)
    {
        var serviceMethod = RpcKernelClientProxyEmitter.ResolveServiceMethod(
            serviceType,
            method,
            context.SemanticModel.Compilation);
        var clientExtensions = RpcKernelClientExtensionModelFactory.Resolve(type, method);
        RpcKernelClientExtensionModelFactory.ValidateLanguageVersion(
            clientExtensions,
            context.SemanticModel.SyntaxTree.Options);
        ValidateGeneratedClientTypeCollisions(type, clientExtensions);
        return new RpcClientGeneration(serviceMethod, clientExtensions, null);
    }

    private static RpcClientGeneration ResolveGraftedClientGeneration(
        INamedTypeSymbol type,
        IMethodSymbol method,
        RpcServerExtensionGraft graft)
    {
        var directClientMethod = RpcKernelClientExtensionModelFactory.ResolveClientMethod(method, graft.ReceiverType);
        ValidateDirectClientReceiver(directClientMethod, graft);
        if (directClientMethod is not null)
        {
            ValidateGeneratedTypeCollision(type, type.Name + "DirectServerExtensionClientExtensions");
        }

        return new RpcClientGeneration(null, null, directClientMethod);
    }

    private static void ValidateDirectClientReceiver(
        RpcKernelClientMethodExtension? directClientMethod,
        RpcServerExtensionGraft graft)
    {
        if (directClientMethod is not null &&
            !SymbolEqualityComparer.Default.Equals(directClientMethod.ReceiverType, graft.ReceiverType))
        {
            throw new NotSupportedException(
                $"Server extension client method receiver '{directClientMethod.ReceiverType.ToDisplayString()}' " +
                $"must match the class receiver '{graft.ReceiverType.ToDisplayString()}'.");
        }
    }

    private static void RejectUnsupportedClientGeneration(INamedTypeSymbol type, IMethodSymbol method)
    {
        if (RpcKernelClientExtensionModelFactory.HasClientPropertyAttribute(type))
        {
            RejectClientPropertyWithoutService(type);
            return;
        }

        if (RpcKernelClientExtensionModelFactory.HasReceiverExtensionAttribute(method))
        {
            throw new NotSupportedException(
                "[ServerExtensionMethod] requires a service-backed or receiver-grafted [ServerExtension] class.");
        }
    }

    private static RpcBodyLowering LowerRpcBody(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken,
        INamedTypeSymbol type,
        IMethodSymbol method,
        RpcServerExtensionGraft? graft)
    {
        var body = MethodBody(method, cancellationToken);
        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        var lowerer = CreateLowerer(context, cancellationToken, method, capabilities, effects);
        var hasReceiverId = RpcKernelReceiverHandleSeeder.TrySeed(lowerer, type, graft);
        var returnValueType = DotBoxDRpcReturnType.PayloadType(
            method.ReturnType,
            context.SemanticModel.Compilation);
        var bodyJson = body.Block is { } block
            ? lowerer.LowerBody(block, returnValueType)
            : lowerer.LowerExpressionBody(body.Expression!, method.ReturnsVoid, returnValueType);
        AddBodyEffects(lowerer, effects);
        return new RpcBodyLowering(bodyJson, effects, capabilities, hasReceiverId);
    }

    private static DotBoxDRpcJsonLowerer CreateLowerer(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken,
        IMethodSymbol method,
        SortedSet<string> capabilities,
        SortedSet<string> effects)
    {
        var contextParameter = method.Parameters[method.Parameters.Length - 1];
        return new DotBoxDRpcJsonLowerer(
            context.SemanticModel,
            capabilities,
            effects,
            cancellationToken,
            serverContextParameterName: contextParameter.Name,
            serverContextType: contextParameter.Type);
    }

    private static void AddBodyEffects(DotBoxDRpcJsonLowerer lowerer, SortedSet<string> effects)
    {
        effects.Add("Cpu");
        if (lowerer.Allocates)
        {
            effects.Add("Alloc");
        }
    }

    private readonly record struct RpcClientGeneration(
        IMethodSymbol? ServiceMethod,
        RpcKernelClientExtensions? ClientExtensions,
        RpcKernelClientMethodExtension? DirectClientMethod);

    private sealed record RpcBodyLowering(
        string BodyJson,
        SortedSet<string> Effects,
        SortedSet<string> Capabilities,
        bool HasReceiverId);
}
