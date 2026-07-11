using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class PipelineSurfaceDiagnosticFactory
{
    public static PluginKernelDiagnostic? Create(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        foreach (var attribute in context.Attributes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not int value ||
                IsValid(value))
            {
                continue;
            }

            return new PluginKernelDiagnostic(
                $"[PipelineSurface] uses invalid PipelineTransport value '{value}'; " +
                "use PipelineTransport.Local or PipelineTransport.Remote.",
                LocationOf(attribute, context, cancellationToken));
        }

        return null;
    }

    private static bool IsValid(int value)
        => value is 0 or 1;

    private static PluginDiagnosticLocation LocationOf(
        AttributeData attribute,
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ??
            context.TargetNode.GetLocation();

        return PluginDiagnosticLocation.From(location);
    }
}
