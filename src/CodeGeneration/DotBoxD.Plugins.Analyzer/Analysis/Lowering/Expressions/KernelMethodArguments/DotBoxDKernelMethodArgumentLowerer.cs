using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class DotBoxDKernelMethodArgumentLowerer
{
    public static DotBoxDExpressionModel? TryLowerWholeEvent(
        IParameterSymbol parameter,
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context)
    {
        if (!TryGetWholeEventRecordType(parameter, expression, context, out var recordType, out var recordTypeSource))
        {
            return null;
        }

        var fields = DotBoxDRpcTypeMapper.RecordFields(recordType);
        var fieldSources = new string[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            if (!TryGetWholeEventFieldSource(recordType, fields[i], context, out fieldSources[i]))
            {
                return null;
            }
        }

        return new DotBoxDExpressionModel(
            DotBoxDRecordCreationExpressionLowerer.RecordNew(fieldSources, recordTypeSource),
            DotBoxDGenerationNames.ManifestTypes.Record,
            true);
    }

    private static bool TryGetWholeEventRecordType(
        IParameterSymbol parameter,
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        out INamedTypeSymbol recordType,
        out string recordTypeSource)
    {
        recordType = null!;
        recordTypeSource = string.Empty;
        if (context.EventParameterName.Length == 0 ||
            expression is not IdentifierNameSyntax identifier ||
            string.Equals(identifier.Identifier.ValueText, context.ProjectedElementName, StringComparison.Ordinal) ||
            !string.Equals(identifier.Identifier.ValueText, context.EventParameterName, StringComparison.Ordinal) ||
            parameter.Type is not INamedTypeSymbol candidate)
        {
            return false;
        }

        if (!string.Equals(
                SandboxTypeSourceEmitter.ManifestTag(candidate),
                DotBoxDGenerationNames.ManifestTypes.Record,
                StringComparison.Ordinal) ||
            SandboxTypeSourceEmitter.TryEmit(candidate) is not { } typeSource)
        {
            return false;
        }

        recordType = candidate;
        recordTypeSource = typeSource;
        return true;
    }

    private static bool TryGetWholeEventFieldSource(
        INamedTypeSymbol recordType,
        RecordMember field,
        DotBoxDExpressionLoweringContext context,
        out string source)
    {
        source = string.Empty;
        var property = EventProperty(field.Name, context);
        var fieldTag = SandboxTypeSourceEmitter.ManifestTag(field.Type);
        if (property is null ||
            !string.Equals(property.Type, fieldTag, StringComparison.Ordinal))
        {
            return false;
        }

        CollectPropertyCapability(recordType, field.Name, context);
        source = EventPropertySource(field.Name);
        return true;
    }

    private static EventPropertyModel? EventProperty(string name, DotBoxDExpressionLoweringContext context)
    {
        for (var i = 0; i < context.EventProperties.Count; i++)
        {
            var property = context.EventProperties[i];
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                return property;
            }
        }

        return null;
    }

    private static string EventPropertySource(string propertyName)
        => $"{DotBoxDGenerationNames.Helpers.Var}(" +
           $"{LiteralReader.StringLiteral(DotBoxDExpressionModelFactory.EventVariable(propertyName))})";

    private static void CollectPropertyCapability(
        INamedTypeSymbol recordType,
        string propertyName,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.Capabilities is null)
        {
            return;
        }

        foreach (var property in recordType.GetMembers(propertyName).OfType<IPropertySymbol>())
        {
            if (PluginSymbolReader.Capability(property) is { } capability)
            {
                context.Capabilities.Add(capability);
            }
        }
    }
}
