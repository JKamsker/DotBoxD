using System;
using System.Text;
using DotBoxD.Services.SourceGenerator.Generation;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;
using DotBoxD.Services.SourceGenerator.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotBoxD.Services.SourceGenerator.EntryPoint;

[Generator(LanguageNames.CSharp)]
public sealed class DotBoxDRpcGenerator : IIncrementalGenerator
{
    private const string DiagnosticHelpBase = "https://dotboxd.kamsker.at/reference/diagnostics/#";

    private static readonly DiagnosticDescriptor s_generatorErrorRule = new(
        id: "DBXS001",
        title: "DotBoxD source generator error",
        messageFormat: "DotBoxD failed to generate for '{0}': {1}",
        category: "DotBoxD.Services.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: DiagnosticHelpBase + "dbxs001");

    private static readonly DiagnosticDescriptor s_unsupportedMethodRule = new(
        id: "DBXS002",
        title: "Unsupported DotBoxD method shape",
        messageFormat: "DotBoxD cannot generate code for '{0}.{1}': {2}",
        category: "DotBoxD.Services.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: DiagnosticHelpBase + "dbxs002");

    private static readonly DiagnosticDescriptor s_unsupportedServiceRule = new(
        id: "DBXS003",
        title: "Unsupported DotBoxD service shape",
        messageFormat: "DotBoxD cannot generate code for service '{0}': {1}",
        category: "DotBoxD.Services.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: DiagnosticHelpBase + "dbxs003");

    private static readonly DiagnosticDescriptor s_asyncSiblingCollisionRule = new(
        id: "DBXS004",
        title: "DotBoxD async sibling method name collides",
        messageFormat: "DotBoxD cannot project '{0}.{1}' onto its async sibling: {2}",
        category: "DotBoxD.Services.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: DiagnosticHelpBase + "dbxs004");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ServicesGeneratorTypeNames.RpcServiceAttribute,
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, ct) => ServiceModelFactory.GetServiceResult(ctx, ct))
            .WithTrackingName("RawServiceResults");

        var existingTypeDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    ExistingTypeIndex.IsPotentialGeneratedTypeDeclaration(node),
                transform: static (ctx, _) => ExistingTypeIndex.FromDeclaration(ctx.Node))
            .Where(static declaration => declaration is not null)
            .Select(static (declaration, _) => declaration!.Value)
            .WithTrackingName("ExistingTypeDeclarations");

        var existingTypeKeys = existingTypeDeclarations
            .Select(static (declaration, _) => declaration.Key)
            .WithTrackingName("ExistingTypeKeys");

        var existingTypes = existingTypeKeys
            .Collect()
            .Select(static (types, ct) => ExistingTypeIndex.Create(types, ct))
            .WithTrackingName("ExistingTypes");

        var existingTypeLocationIndex = existingTypeDeclarations
            .Collect()
            .Select(static (types, ct) => ExistingTypeLocationIndex.Create(types, ct))
            .WithTrackingName("ExistingTypeLocations");

        results = ServiceResultValidationPipeline.Apply(results, existingTypes);

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

        var methodDiagnostics = results
            .SelectMany(static (r, _) => r.MethodDiagnostics.Array)
            .WithTrackingName("MethodDiagnostics");

        context.RegisterSourceOutput(methodDiagnostics, static (spc, d) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_unsupportedMethodRule,
                d.Location.ToLocation(),
                d.InterfaceName,
                d.MethodName,
                d.Reason)));

        var serviceDiagnostics = results
            .Where(static r => r.ServiceDiagnostic is not null)
            .Select(static (r, _) => r.ServiceDiagnostic!.Value)
            .WithTrackingName("ServiceDiagnostics");

        context.RegisterSourceOutput(serviceDiagnostics, static (spc, d) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_unsupportedServiceRule,
                d.Location.ToLocation(),
                d.InterfaceName,
                d.Reason)));

        var existingTypeDiagnostics = results
            .Where(static r => r.ExistingTypeCollision is not null)
            .Select(static (r, _) => r.ExistingTypeCollision!.Value)
            .Combine(existingTypeLocationIndex)
            .Select(static (pair, ct) => new ServiceDiagnostic(
                pair.Left.InterfaceName,
                pair.Left.Reason,
                pair.Right.Find(pair.Left.ExistingType, ct)))
            .WithTrackingName("ExistingTypeDiagnostics");

        context.RegisterSourceOutput(existingTypeDiagnostics, static (spc, d) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_unsupportedServiceRule,
                d.Location.ToLocation(),
                d.InterfaceName,
                d.Reason)));

        var models = results
            .Where(static r => r.Model is not null)
            .Select(static (r, _) => r.Model!)
            .WithTrackingName("Services");

        var projections = results
            .Where(static r => r.Model is not null)
            .Select(static (r, ct) =>
            {
                var model = r.Model!;
                if (!NamingHelpers.CanGenerateAsyncSiblingInterface(model.InterfaceName))
                {
                    return new ServiceProjection(
                        ServiceBundle.Empty(model),
                        EquatableArray<MethodDiagnostic>.Empty);
                }

                var (siblings, collisions) = AsyncSiblingProjector.Compute(model, r.MethodLocations, ct);
                return new ServiceProjection(new ServiceBundle(model, siblings), collisions);
            })
            .WithTrackingName("ServiceProjections");

        var bundles = projections
            .Select(static (projection, _) => projection.Bundle)
            .WithTrackingName("ServiceBundles");

        var emitClsNonCompliantAttributes = context.CompilationProvider
            .Select(static (compilation, _) => AssemblyClsCompliance.IsEnabled(compilation))
            .WithTrackingName("EmitClsNonCompliantAttributes");

        var siblingCollisions = projections
            .SelectMany(static (projection, _) => projection.SiblingCollisions.Array)
            .WithTrackingName("SiblingCollisions");

        context.RegisterSourceOutput(siblingCollisions, static (spc, d) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_asyncSiblingCollisionRule,
                d.Location.ToLocation(),
                d.InterfaceName,
                d.MethodName,
                d.Reason)));

        context.RegisterSourceOutput(bundles.Combine(emitClsNonCompliantAttributes), static (spc, pair) =>
        {
            var bundle = pair.Left;
            var emitClsNonCompliantAttribute = pair.Right;
            try
            {
                var hintPrefix = HintNameBuilder.Prefix(bundle.Model);
                var ct = spc.CancellationToken;
                var proxySource = ProxyGenerator.Generate(bundle.Model, bundle.SiblingMethods, emitClsNonCompliantAttribute, ct);
                spc.AddSource(
                    $"{hintPrefix}.DotBoxDRpcProxy.g.cs",
                    SourceText.From(proxySource, Encoding.UTF8));

                var dispatcherSource = DispatcherGenerator.Generate(bundle.Model, emitClsNonCompliantAttribute, ct);
                spc.AddSource(
                    $"{hintPrefix}.DotBoxDRpcDispatcher.g.cs",
                    SourceText.From(dispatcherSource, Encoding.UTF8));

                if (!bundle.SiblingMethods.IsEmpty)
                {
                    var asyncSource = AsyncInterfaceGenerator.Generate(bundle.Model, bundle.SiblingMethods, emitClsNonCompliantAttribute, ct);
                    spc.AddSource(
                        $"{hintPrefix}.DotBoxDRpcAsync.g.cs",
                        SourceText.From(asyncSource, Encoding.UTF8));
                }
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
                    bundle.Model.InterfaceName,
                    ex.ToString()));
            }
        });

        var serviceExtensions = models
            .Select(static (model, _) => ServiceExtensionModel.From(model))
            .WithTrackingName("ServiceExtensions");

        var allServices = serviceExtensions
            .Collect()
            .Select(static (arr, ct) => ServiceModelOrdering.SortExtensions(arr, ct))
            .WithTrackingName("AllServices");

        var allServiceMetadata = models
            .Collect()
            .Select(static (arr, ct) => ServiceModelOrdering.Sort(arr, ct))
            .WithTrackingName("AllServiceMetadata");

        context.RegisterSourceOutput(allServices.Combine(emitClsNonCompliantAttributes), static (spc, pair) =>
        {
            var services = pair.Left;
            if (services.IsEmpty)
            {
                return;
            }

            try
            {
                var extensionsSource = ExtensionsGenerator.Generate(services, pair.Right, spc.CancellationToken);
                spc.AddSource(
                    "DotBoxDRpcExtensions.g.cs",
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
                    "DotBoxDRpcExtensions",
                    ex.ToString()));
            }
        });

        context.RegisterSourceOutput(allServiceMetadata.Combine(emitClsNonCompliantAttributes), static (spc, pair) =>
        {
            var services = pair.Left;
            if (services.IsEmpty)
            {
                return;
            }

            try
            {
                var factorySource = GeneratedFactoryGenerator.Generate(services, pair.Right, spc.CancellationToken);
                spc.AddSource(
                    "DotBoxDGenerated.g.cs",
                    SourceText.From(factorySource, Encoding.UTF8));
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
                    "DotBoxDGenerated",
                    ex.ToString()));
            }
        });
    }
}
