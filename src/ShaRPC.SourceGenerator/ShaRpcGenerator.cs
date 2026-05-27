using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ShaRPC.SourceGenerator;

/// <summary>
/// Incremental source generator for ShaRPC client proxies and server dispatchers.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ShaRpcGenerator : IIncrementalGenerator
{
    private const string ShaRpcServiceAttributeName = "ShaRPC.Core.Attributes.ShaRpcServiceAttribute";
    private const string ShaRpcMethodAttributeName = "ShaRPC.Core.Attributes.ShaRpcMethodAttribute";
    private const string CancellationTokenFullName = "System.Threading.CancellationToken";

    private static readonly DiagnosticDescriptor s_generatorErrorRule = new(
        id: "SHARPC001",
        title: "ShaRPC source generator error",
        messageFormat: "ShaRPC failed to generate for '{0}': {1}",
        category: "ShaRPC.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal readonly record struct ServiceResult(ServiceModel? Model, GeneratorError? Error);

    internal readonly record struct GeneratorError(string Where, string Message);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ShaRpcServiceAttributeName,
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, ct) => GetServiceResult(ctx, ct))
            .WithTrackingName("ServiceResults");

        var errors = results
            .Where(static r => r.Error is not null)
            .Select(static (r, _) => r.Error!.Value)
            .WithTrackingName("ServiceErrors");

        context.RegisterSourceOutput(errors, static (spc, error) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_generatorErrorRule,
                Location.None,
                error.Where,
                error.Message)));

        var models = results
            .Where(static r => r.Model is not null)
            .Select(static (r, _) => r.Model!)
            .WithTrackingName("Services");

        context.RegisterSourceOutput(models, static (spc, model) =>
        {
            try
            {
                var proxySource = ProxyGenerator.Generate(model);
                spc.AddSource(
                    $"{model.InterfaceName}.ShaRpcProxy.g.cs",
                    SourceText.From(proxySource, Encoding.UTF8));

                var dispatcherSource = DispatcherGenerator.Generate(model);
                spc.AddSource(
                    $"{model.InterfaceName}.ShaRpcDispatcher.g.cs",
                    SourceText.From(dispatcherSource, Encoding.UTF8));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    s_generatorErrorRule,
                    Location.None,
                    model.InterfaceName,
                    ex.ToString()));
            }
        });

        var allServices = models
            .Collect()
            .Select(static (arr, _) => arr.ToEquatableArray())
            .WithTrackingName("AllServices");

        context.RegisterSourceOutput(allServices, static (spc, services) =>
        {
            if (services.IsEmpty)
            {
                return;
            }

            try
            {
                var extensionsSource = GenerateExtensions(services);
                spc.AddSource(
                    "ShaRpcExtensions.g.cs",
                    SourceText.From(extensionsSource, Encoding.UTF8));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    s_generatorErrorRule,
                    Location.None,
                    "ShaRpcExtensions",
                    ex.ToString()));
            }
        });
    }

    private static ServiceResult GetServiceResult(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        try
        {
            var model = BuildServiceModel(context, ct);
            return new ServiceResult(model, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var name = context.TargetSymbol?.ToDisplayString() ?? "<unknown>";
            return new ServiceResult(null, new GeneratorError(name, ex.ToString()));
        }
    }

    private static ServiceModel? BuildServiceModel(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol interfaceSymbol)
        {
            return null;
        }

        ct.ThrowIfCancellationRequested();

        // Pick the service attribute (already guaranteed to be present by ForAttributeWithMetadataName).
        string? customName = null;
        foreach (var attr in context.Attributes)
        {
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "Name" && namedArg.Value.Value is string s)
                {
                    customName = s;
                }
            }
        }

        var ns = interfaceSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : interfaceSymbol.ContainingNamespace.ToDisplayString();
        var interfaceName = interfaceSymbol.Name;
        var serviceName = customName ?? interfaceName;

        var methods = new List<MethodModel>();

        foreach (var member in interfaceSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (member is not IMethodSymbol methodSymbol || methodSymbol.MethodKind != MethodKind.Ordinary)
            {
                continue;
            }

            string? customMethodName = null;
            foreach (var attr in methodSymbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == ShaRpcMethodAttributeName)
                {
                    foreach (var namedArg in attr.NamedArguments)
                    {
                        if (namedArg.Key == "Name" && namedArg.Value.Value is string s)
                        {
                            customMethodName = s;
                        }
                    }
                }
            }

            var returnType = methodSymbol.ReturnType;
            var returnsTask = returnType.Name == "Task" ||
                (returnType is INamedTypeSymbol namedReturn && namedReturn.ConstructedFrom?.Name == "Task");

            string returnTypeStr;
            string? unwrappedReturnType;
            bool returnsVoid;

            if (returnType is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.Name == "Task")
            {
                unwrappedReturnType = namedType.TypeArguments[0].ToDisplayString();
                returnTypeStr = $"Task<{unwrappedReturnType}>";
                returnsVoid = false;
            }
            else if (returnType.Name == "Task")
            {
                returnTypeStr = "Task";
                unwrappedReturnType = null;
                returnsVoid = true;
            }
            else if (returnType.SpecialType == SpecialType.System_Void)
            {
                returnTypeStr = "Task";
                unwrappedReturnType = null;
                returnsVoid = true;
            }
            else
            {
                unwrappedReturnType = returnType.ToDisplayString();
                returnTypeStr = $"Task<{unwrappedReturnType}>";
                returnsVoid = false;
            }

            var parameters = new List<ParameterModel>();
            foreach (var param in methodSymbol.Parameters)
            {
                var paramTypeStr = param.Type.ToDisplayString();
                if (paramTypeStr == CancellationTokenFullName)
                {
                    continue;
                }

                parameters.Add(new ParameterModel(param.Name, paramTypeStr));
            }

            methods.Add(new MethodModel(
                Name: methodSymbol.Name,
                RpcName: customMethodName ?? methodSymbol.Name,
                ReturnType: returnTypeStr,
                UnwrappedReturnType: unwrappedReturnType,
                ReturnsTask: returnsTask,
                ReturnsVoid: returnsVoid,
                Parameters: parameters.ToEquatableArray()));
        }

        return new ServiceModel(
            Namespace: ns,
            InterfaceName: interfaceName,
            ServiceName: serviceName,
            Methods: methods.ToEquatableArray());
    }

    private static string GenerateExtensions(EquatableArray<ServiceModel> services)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using ShaRPC.Core.Client;");
        sb.AppendLine("using ShaRPC.Core.Server;");
        sb.AppendLine();
        sb.AppendLine("namespace ShaRPC.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Extension methods for registering generated ShaRPC services.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class ShaRpcGeneratedExtensions");
        sb.AppendLine("    {");

        foreach (var service in services)
        {
            var serviceName = service.InterfaceName.StartsWith("I", StringComparison.Ordinal)
                ? service.InterfaceName.Substring(1)
                : service.InterfaceName;
            var proxyName = serviceName + "Proxy";
            var dispatcherName = serviceName + "Dispatcher";
            var fullInterfaceName = string.IsNullOrEmpty(service.Namespace)
                ? service.InterfaceName
                : $"{service.Namespace}.{service.InterfaceName}";
            var fullProxyName = string.IsNullOrEmpty(service.Namespace)
                ? proxyName
                : $"{service.Namespace}.{proxyName}";
            var fullDispatcherName = string.IsNullOrEmpty(service.Namespace)
                ? dispatcherName
                : $"{service.Namespace}.{dispatcherName}";

            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine($"        /// Creates a proxy for {service.InterfaceName}.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public static {fullInterfaceName} Create{serviceName}Proxy(this IShaRpcClient client)");
            sb.AppendLine($"            => new {fullProxyName}(client);");

            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine($"        /// Registers {service.InterfaceName} with the server.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public static ShaRpcServerBuilder Add{serviceName}(this ShaRpcServerBuilder builder, {fullInterfaceName} implementation)");
            sb.AppendLine($"            => builder.AddDispatcher(new {fullDispatcherName}(implementation));");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
