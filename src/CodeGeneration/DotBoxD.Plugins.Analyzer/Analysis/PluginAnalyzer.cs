using System.Collections.Immutable;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class PluginAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor ForbiddenHostApiRule = new(
        "DBXK001",
        "Forbidden host API is not allowed in plugin kernels",
        "Forbidden host API '{0}' is not allowed in this plugin contract",
        "DotBoxD.Kernels.Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Hook filters and kernel handlers must use approved safe facades instead of host APIs.",
        helpLinkUri: PluginAnalyzerDiagnostics.ShippedRulesHelpLinkBase + "dbxk001",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    public static readonly DiagnosticDescriptor LiveSettingTypeRule = new(
        "DBXK020",
        "Live setting type is not supported",
        "Live setting type '{0}' is not supported",
        "DotBoxD.Kernels.Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Live settings must use supported scalar types.",
        helpLinkUri: PluginAnalyzerDiagnostics.ShippedRulesHelpLinkBase + "dbxk020");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(
            ForbiddenHostApiRule,
            LiveSettingTypeRule,
            PluginAnalyzerDiagnostics.LocalContextMemberRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        context.RegisterCompilationStartAction(startContext =>
        {
            var helperGraph = new ForbiddenHelperCallGraph();
            startContext.RegisterSymbolAction(c => AnalyzeMethod(c, helperGraph), SymbolKind.Method);
            startContext.RegisterOperationAction(c => AnalyzeInvocation(c, helperGraph), OperationKind.Invocation);
            startContext.RegisterOperationAction(c => AnalyzeDynamicInvocation(c, helperGraph), OperationKind.DynamicInvocation);
            startContext.RegisterOperationAction(c => AnalyzeObjectCreation(c, helperGraph), OperationKind.ObjectCreation);
            startContext.RegisterOperationAction(c => AnalyzeWithExpression(c, helperGraph), OperationKind.With);
            startContext.RegisterOperationAction(c => AnalyzePropertyReference(c, helperGraph), OperationKind.PropertyReference);
            startContext.RegisterOperationAction(c => AnalyzeFieldReference(c, helperGraph), OperationKind.FieldReference);
            startContext.RegisterOperationAction(c => AnalyzeTypeOf(c, helperGraph), OperationKind.TypeOf);
            startContext.RegisterOperationAction(c => AnalyzeMethodReference(c, helperGraph), OperationKind.MethodReference);
            startContext.RegisterOperationAction(c => AnalyzeAnonymousFunction(c, helperGraph), OperationKind.AnonymousFunction);
            startContext.RegisterOperationAction(c => AnalyzeEventReference(c, helperGraph), OperationKind.EventReference);
            startContext.RegisterOperationAction(c => AnalyzeVariableDeclaration(c, helperGraph), OperationKind.VariableDeclaration);
            startContext.RegisterOperationAction(c => AnalyzeUsing(c, helperGraph), OperationKind.Using);
            startContext.RegisterOperationAction(c => AnalyzeUsingDeclaration(c, helperGraph), OperationKind.UsingDeclaration);
            startContext.RegisterOperationAction(c => AnalyzeDeconstruction(c, helperGraph), OperationKind.DeconstructionAssignment);
            startContext.RegisterOperationAction(c => AnalyzeLock(c, helperGraph), OperationKind.Lock);
            startContext.RegisterOperationAction(
                c => AnalyzeCollectionExpression(c, helperGraph),
                OperationKind.CollectionExpression);
            startContext.RegisterOperationAction(c => AnalyzeListPattern(c, helperGraph), OperationKind.ListPattern);
            startContext.RegisterOperationAction(c => AnalyzeSlicePattern(c, helperGraph), OperationKind.SlicePattern);
            startContext.RegisterOperationAction(c => AnalyzeRecursivePattern(c, helperGraph), OperationKind.RecursivePattern);
            startContext.RegisterOperationAction(c => AnalyzeSpread(c, helperGraph), OperationKind.Spread);
            startContext.RegisterOperationAction(
                c => AnalyzeOperator(c, helperGraph),
                OperationKind.UnaryOperator,
                OperationKind.BinaryOperator,
                OperationKind.Conversion);
            startContext.RegisterOperationAction(
                c => AnalyzeImplicitStringFormatting(c, helperGraph),
                OperationKind.BinaryOperator,
                OperationKind.Interpolation);
            RegisterAwaitReachabilityAnalysis(startContext, helperGraph);
            RegisterForbiddenTypeSyntaxAnalysis(startContext, helperGraph);
            RegisterEnumerationSyntaxAnalysis(startContext, helperGraph);
            RegisterFixedReachabilityAnalysis(startContext, helperGraph);
            startContext.RegisterCompilationEndAction(helperGraph.ReportDiagnostics);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, invocation.TargetMethod.ContainingType);
            RecordForbiddenInitializerReference(context, helperGraph, invocation.TargetMethod.ContainingType);
            RecordForbiddenDelegateInitializer(context, helperGraph, invocation.TargetMethod.ContainingType);
            RecordStaticConstructorReachability(context, helperGraph, invocation.TargetMethod);
            RecordForbiddenHelperPropertyInitializer(context, helperGraph, invocation.TargetMethod.ContainingType);
            RecordInitializerRootCall(context, helperGraph, invocation.TargetMethod);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, invocation.TargetMethod.ContainingType);
        RecordStaticConstructorReachability(context, helperGraph, invocation.TargetMethod);
        ReportForbiddenReferencedMethodSignature(context, helperGraph, invocation.TargetMethod);
        helperGraph.RecordCall(method, invocation.TargetMethod, context.Operation.Syntax.GetLocation());
        ReportLocalUseIfInvalid(context, invocation.TargetMethod);
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, creation.Type);
            RecordForbiddenInitializerReference(context, helperGraph, creation.Type);
            RecordForbiddenDelegateInitializer(context, helperGraph, creation.Type);
            RecordStaticConstructorReachability(context, helperGraph, creation.Type);
            RecordFinalizerReachability(context, helperGraph, creation.Type);
            RecordForbiddenHelperPropertyInitializer(context, helperGraph, creation.Type);
            if (creation.Constructor is { } initializerConstructor)
            {
                helperGraph.RecordConstructorInitializers(initializerConstructor);
                RecordInitializerRootCall(context, helperGraph, initializerConstructor);
            }

            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, creation.Type);
        RecordStaticConstructorReachability(context, helperGraph, creation.Type);
        RecordFinalizerReachability(context, helperGraph, creation.Type);
        if (creation.Constructor is { } constructor)
        {
            helperGraph.RecordConstructorInitializers(constructor);
            helperGraph.RecordCall(method, constructor, context.Operation.Syntax.GetLocation());
        }
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var property = ((IPropertyReferenceOperation)context.Operation).Property;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, property.ContainingType);
            RecordForbiddenInitializerReference(context, helperGraph, property.ContainingType);
            RecordForbiddenDelegateInitializer(context, helperGraph, property.ContainingType);
            RecordStaticConstructorReachability(context, helperGraph, property);
            RecordForbiddenHelperPropertyInitializer(context, helperGraph, property.ContainingType);
            RecordInitializerPropertyRootCall(context, helperGraph, property);
            RecordInitializerMemberReference(context, helperGraph, property);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, property.ContainingType);
        RecordStaticConstructorReachability(context, helperGraph, property);
        ReportForbiddenReferencedType(context, helperGraph, property.ContainingType, property.Type);
        ReportLocalUseIfInvalid(context, property);

        // A forbidden API reached through a helper property's accessor body is only linked to the kernel
        // if we record an edge to the accessor it actually uses: the getter for a read, the setter for a
        // write, both for a compound/increment. Without this the accessor taints but never reaches a root.
        var (usesGetter, usesSetter) = AccessorUsage(context.Operation);
        var location = context.Operation.Syntax.GetLocation();
        if (usesGetter && property.GetMethod is { } getter)
        {
            helperGraph.RecordCall(method, getter, location);
        }

        if (usesSetter && property.SetMethod is { } setter)
        {
            helperGraph.RecordCall(method, setter, location);
        }

    }

    private static void AnalyzeFieldReference(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var field = ((IFieldReferenceOperation)context.Operation).Field;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, field.ContainingType);
            RecordForbiddenInitializerReference(context, helperGraph, field.ContainingType);
            RecordInitializerMemberReference(context, helperGraph, field);
            RecordForbiddenDelegateInitializer(context, helperGraph, field.ContainingType);
            RecordStaticConstructorReachability(context, helperGraph, field);
            RecordForbiddenHelperPropertyInitializer(context, helperGraph, field.ContainingType);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, field.ContainingType);
        ReportForbiddenReferencedType(context, helperGraph, field.ContainingType, field.Type);
        if (IsDelegateType(field.Type))
        {
            RecordDelegateFieldReference(context, helperGraph, method, field);
        }
        else
        {
            helperGraph.RecordRootMemberReference(method, field, context.Operation.Syntax.GetLocation());
        }
        RecordStaticConstructorReachability(context, helperGraph, field);
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName)
        => symbol.GetAttributes().Any(a => string.Equals(
            a.AttributeClass?.ToDisplayString(),
            metadataName,
            StringComparison.Ordinal));

    private static void ReportAndRecordIfForbidden(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        ITypeSymbol? type)
    {
        if (!IsForbiddenHostApi(type))
        {
            return;
        }

        helperGraph.RecordForbidden(method, type!);
        if (!IsEventKernel(method.ContainingType) ||
            !helperGraph.TryRecordDirectDiagnostic(method))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            context.Operation.Syntax.GetLocation(),
            type!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    private static bool IsForbiddenHostApi(ITypeSymbol? type)
        => TryGetForbiddenHostApi(type, out _);

    private static bool IsForbiddenExactType(string typeName)
        => typeName is DotBoxDGenerationNames.TypeNames.SystemActivator
            or DotBoxDGenerationNames.TypeNames.SystemEnvironment
            or DotBoxDGenerationNames.TypeNames.SystemGc
            or DotBoxDGenerationNames.TypeNames.SystemDelegate
            or DotBoxDGenerationNames.TypeNames.SystemServiceProvider
            or DotBoxDGenerationNames.TypeNames.SystemType;

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
            "System.Security.Cryptography.X509Certificates.",
            "System.Security.Principal.",
            "System.Data.",
            "Microsoft.CSharp.",
            "Microsoft.EntityFrameworkCore.",
            "Microsoft.Win32."
        ];
        foreach (var prefix in prefixes)
        {
            if (typeName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsEventKernel(INamedTypeSymbol? type)
        => type?.AllInterfaces.Any(i => string.Equals(
            i.OriginalDefinition.ToDisplayString(),
            DotBoxDMetadataNames.EventKernelInterface,
            StringComparison.Ordinal)) == true;

    private static bool IsAllowedLiveSettingType(ITypeSymbol type)
        => DotBoxDTypeNameReader.IsSupportedLiveSettingType(type);

}
