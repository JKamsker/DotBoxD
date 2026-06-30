namespace DotBoxD.Plugins.Analyzer.Analysis.Registration;

using System.Collections.Immutable;
using DotBoxD.Plugins.Analyzer.Analysis;

internal static class RegistrationAccumulatorBatchFactory
{
    public static RegistrationGenerationBatch CreateTargets(
        ImmutableArray<RegistrationAccumulatorTargetModel> targets)
    {
        var accumulatorDuplicates = DuplicateKeys(
            targets.Select(static target => AccumulatorKey(target.Namespace, target.AccumulatorName)));
        var receiverDuplicates = DuplicateKeys(targets.Select(static target => target.ReceiverTypeName));
        var sources = new List<RegistrationGeneratedSource>();
        var diagnostics = new List<PluginKernelDiagnostic>();

        foreach (var target in targets)
        {
            if (accumulatorDuplicates.Contains(AccumulatorKey(target.Namespace, target.AccumulatorName)))
            {
                diagnostics.Add(Diagnostic(
                    target.Location,
                    $"Registration accumulator '{target.AccumulatorName}' is generated more than once in namespace '{NamespaceDisplay(target.Namespace)}'."));
                continue;
            }

            if (receiverDuplicates.Contains(target.ReceiverTypeName))
            {
                diagnostics.Add(Diagnostic(
                    target.Location,
                    $"Registration control '{target.ReceiverTypeName}' has more than one generated accumulator."));
                continue;
            }

            sources.Add(RegistrationAccumulatorEmitter.EmitTarget(target));
        }

        return Batch(sources, diagnostics);
    }

    public static RegistrationGenerationBatch CreateRoots(
        ImmutableArray<RegistrationRootAccumulatorModel> roots,
        ImmutableArray<RegistrationAccumulatorTargetModel> targets)
    {
        var targetByReceiver = UniqueTargets(targets);
        var rootDuplicates = DuplicateKeys(
            roots.Select(static root => AccumulatorKey(root.Namespace, root.AccumulatorName)));
        var targetAccumulatorKeys = new HashSet<string>(
            targets.Select(static target => AccumulatorKey(target.Namespace, target.AccumulatorName)),
            StringComparer.Ordinal);
        var sources = new List<RegistrationGeneratedSource>();
        var diagnostics = new List<PluginKernelDiagnostic>();

        foreach (var root in roots)
        {
            var key = AccumulatorKey(root.Namespace, root.AccumulatorName);
            if (rootDuplicates.Contains(key) || targetAccumulatorKeys.Contains(key))
            {
                diagnostics.Add(Diagnostic(
                    root.Location,
                    $"Registration root accumulator '{root.AccumulatorName}' is generated more than once in namespace '{NamespaceDisplay(root.Namespace)}'."));
                continue;
            }

            var children = Children(root, targetByReceiver);
            if (children.Count == 0)
            {
                diagnostics.Add(Diagnostic(
                    root.Location,
                    $"Registration root '{root.ReceiverTypeName}' has no public child control property with a generated accumulator."));
                continue;
            }

            sources.Add(RegistrationAccumulatorEmitter.EmitRoot(root, children));
        }

        return Batch(sources, diagnostics);
    }

    private static Dictionary<string, RegistrationAccumulatorTargetModel> UniqueTargets(
        ImmutableArray<RegistrationAccumulatorTargetModel> targets)
    {
        var duplicateReceivers = DuplicateKeys(targets.Select(static target => target.ReceiverTypeName));
        var result = new Dictionary<string, RegistrationAccumulatorTargetModel>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (!duplicateReceivers.Contains(target.ReceiverTypeName))
            {
                result[target.ReceiverTypeName] = target;
            }
        }

        return result;
    }

    private static EquatableArray<RegistrationChildAccumulatorModel> Children(
        RegistrationRootAccumulatorModel root,
        Dictionary<string, RegistrationAccumulatorTargetModel> targetByReceiver)
    {
        var children = new List<RegistrationChildAccumulatorModel>();
        foreach (var property in root.Properties)
        {
            if (targetByReceiver.TryGetValue(property.TypeName, out var target))
            {
                children.Add(new RegistrationChildAccumulatorModel(
                    property.Name,
                    property.DeclaringTypeName,
                    target.AccumulatorName));
            }
        }

        return new EquatableArray<RegistrationChildAccumulatorModel>(children);
    }

    private static HashSet<string> DuplicateKeys(IEnumerable<string> keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (!seen.Add(key))
            {
                duplicates.Add(key);
            }
        }

        return duplicates;
    }

    private static RegistrationGenerationBatch Batch(
        List<RegistrationGeneratedSource> sources,
        List<PluginKernelDiagnostic> diagnostics)
        => new(
            new EquatableArray<RegistrationGeneratedSource>(sources),
            new EquatableArray<PluginKernelDiagnostic>(diagnostics));

    private static PluginKernelDiagnostic Diagnostic(PluginDiagnosticLocation location, string message)
        => new(message, location);

    private static string AccumulatorKey(string @namespace, string accumulatorName)
        => @namespace + "." + accumulatorName;

    private static string NamespaceDisplay(string @namespace)
        => string.IsNullOrWhiteSpace(@namespace) ? "<global>" : @namespace;
}
