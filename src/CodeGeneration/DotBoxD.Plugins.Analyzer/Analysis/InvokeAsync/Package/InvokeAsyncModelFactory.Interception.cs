using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DotBoxD.Plugins.Analyzer.Analysis.Rpc.DotBoxDRpcJsonLowerer;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static partial class InvokeAsyncModelFactory
{
    private static InvokeAsyncInterception? Interception(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        string receiverType,
        string? serverAccessType,
        string ns,
        string packageName,
        string pluginId,
        InvokeAsyncCallShape shape,
        CancellationToken cancellationToken)
    {
        var location = model.GetInterceptableLocation(invocation, cancellationToken);
        if (location is null)
        {
            return null;
        }

        var reader = new InvokeAsyncResultReaderSource(
            "ReadInvokeAsyncResult_" + packageName + "_",
            model.Compilation);
        var resultExpression = reader.ReadExpression(
            shape.ReturnType,
            shape.SyncOuts.Count == 0 ? "__result" : "__result.GetItem(0)");
        var syncOutAssignments = new string[shape.SyncOuts.Count];
        for (var i = 0; i < shape.SyncOuts.Count; i++)
        {
            var syncOut = shape.SyncOuts[i];
            var value = reader.ReadExpression(syncOut.Type, "__result.GetItem(" + (i + 1) + ")");
            syncOutAssignments[i] = shape.UsesReflectionCaptures
                ? "__WriteCapture(lambda, " + Str(syncOut.TargetName) + ", " + value + ")"
                : "captures." + InvokeAsyncSourceIdentifier.Escape(syncOut.TargetName) + " = " + value;
        }

        var packageFullName = string.IsNullOrEmpty(ns)
            ? DotBoxDGenerationNames.TypeNames.GlobalPrefix + packageName
            : DotBoxDGenerationNames.TypeNames.GlobalPrefix + ns + "." + packageName;
        var captureType = shape.CaptureType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var captureDelegateType = shape.CaptureType is null
            ? null
            : DotBoxDGenerationNames.TypeNames.GlobalPrefix + DotBoxDMetadataNames.ServerInvocationDelegateType +
              "<" + shape.WorldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
              ", " + captureType +
              ", " + shape.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">";

        return new InvokeAsyncInterception(
            location.GetInterceptsLocationAttributeSyntax(),
            receiverType,
            serverAccessType,
            shape.WorldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            shape.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            captureType,
            captureDelegateType,
            pluginId,
            packageFullName,
            shape.ArgumentsExpression,
            resultExpression,
            new EquatableArray<string>(syncOutAssignments),
            shape.UsesReflectionCaptures,
            reader.Helpers);
    }
}
