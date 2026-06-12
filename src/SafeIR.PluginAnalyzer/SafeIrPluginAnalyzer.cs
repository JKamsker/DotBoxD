namespace SafeIR.PluginAnalyzer;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SafeIrPluginAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor ForbiddenHostApiRule = new(
        "SGP001",
        "Forbidden host API is not allowed in plugin kernels",
        "Forbidden host API '{0}' is not allowed in this plugin contract",
        "SafeIR.Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Hook filters and kernel handlers must use approved safe facades instead of host APIs.");

    public static readonly DiagnosticDescriptor LiveSettingTypeRule = new(
        "SGP020",
        "Live setting type is not supported",
        "Live setting type '{0}' is not supported",
        "SafeIR.Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Live settings must use supported scalar types.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(ForbiddenHostApiRule, LiveSettingTypeRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
        context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
        context.RegisterOperationAction(AnalyzeFieldReference, OperationKind.FieldReference);
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
        if (context.ContainingSymbol is not IMethodSymbol method || !IsEventKernel(method.ContainingType)) {
            return;
        }

        ReportIfForbidden(context, invocation.TargetMethod.ContainingType);
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        if (context.ContainingSymbol is not IMethodSymbol method || !IsEventKernel(method.ContainingType)) {
            return;
        }

        ReportIfForbidden(context, ((IObjectCreationOperation)context.Operation).Type);
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context)
    {
        if (context.ContainingSymbol is not IMethodSymbol method || !IsEventKernel(method.ContainingType)) {
            return;
        }

        ReportIfForbidden(context, ((IPropertyReferenceOperation)context.Operation).Property.ContainingType);
    }

    private static void AnalyzeFieldReference(OperationAnalysisContext context)
    {
        if (context.ContainingSymbol is not IMethodSymbol method || !IsEventKernel(method.ContainingType)) {
            return;
        }

        ReportIfForbidden(context, ((IFieldReferenceOperation)context.Operation).Field.ContainingType);
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName)
        => symbol.GetAttributes().Any(a => string.Equals(
            a.AttributeClass?.ToDisplayString(),
            metadataName,
            StringComparison.Ordinal));

    private static void ReportIfForbidden(OperationAnalysisContext context, ITypeSymbol? type)
    {
        if (!IsForbiddenHostApi(type)) {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            context.Operation.Syntax.GetLocation(),
            type!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    private static bool IsForbiddenHostApi(ITypeSymbol? type)
    {
        var name = type?.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (string.IsNullOrWhiteSpace(name)) {
            return false;
        }

        return IsForbiddenExactType(name!) || IsForbiddenNamespace(name!);
    }

    private static bool IsForbiddenExactType(string typeName)
        => typeName is "System.Activator" or "System.Environment" or "System.GC"
            or "System.Delegate" or "System.IServiceProvider";

    private static bool IsForbiddenNamespace(string typeName)
    {
        ReadOnlySpan<string> prefixes = [
            "System.IO.",
            "System.Net.",
            "System.Reflection.",
            "System.Runtime.InteropServices.",
            "System.Runtime.Loader.",
            "System.Diagnostics.",
            "System.Threading.",
            "System.Threading.Tasks.",
            "System.Linq.Expressions.",
            "Microsoft.CSharp."
        ];
        foreach (var prefix in prefixes) {
            if (typeName.StartsWith(prefix, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

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
