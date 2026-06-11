namespace SafeIR.PluginAnalyzer;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SafeIrPluginAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor FileIoRule = new(
        "SGP001",
        "File IO is not allowed in plugin kernels",
        "File IO is not allowed in this plugin contract",
        "SafeIR.Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Hook filters and kernel handlers must use approved safe facades instead of System.IO.File.");

    public static readonly DiagnosticDescriptor LiveSettingTypeRule = new(
        "SGP020",
        "Live setting type is not supported",
        "Live setting type '{0}' is not supported",
        "SafeIR.Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Live settings must use supported scalar types.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(FileIoRule, LiveSettingTypeRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (!HasAttribute(property, "SafeIR.Plugins.LiveSettingAttribute")) {
            return;
        }

        if (!IsAllowedLiveSettingType(property.Type)) {
            context.ReportDiagnostic(Diagnostic.Create(
                LiveSettingTypeRule,
                property.Locations.FirstOrDefault(),
                property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsSystemFileCall(invocation.TargetMethod) || context.ContainingSymbol is not IMethodSymbol method) {
            return;
        }

        if (!IsEventKernel(method.ContainingType)) {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(FileIoRule, invocation.Syntax.GetLocation()));
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName)
        => symbol.GetAttributes().Any(a => string.Equals(
            a.AttributeClass?.ToDisplayString(),
            metadataName,
            StringComparison.Ordinal));

    private static bool IsSystemFileCall(IMethodSymbol method)
        => string.Equals(method.ContainingType.ToDisplayString(), "System.IO.File", StringComparison.Ordinal);

    private static bool IsEventKernel(INamedTypeSymbol? type)
        => type?.AllInterfaces.Any(i => string.Equals(
            i.OriginalDefinition.ToDisplayString(),
            "SafeIR.Plugins.IEventKernel<TEvent>",
            StringComparison.Ordinal)) == true;

    private static bool IsAllowedLiveSettingType(ITypeSymbol type)
    {
        return type.SpecialType is SpecialType.System_Boolean
            or SpecialType.System_Int32
            or SpecialType.System_Int64
            or SpecialType.System_Double
            or SpecialType.System_String;
    }
}
