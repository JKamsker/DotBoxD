using System.Text;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class ServiceGeneratorCollisionSources
{
    public static IReadOnlyList<ServiceGeneratorCollisionCaseDefinition> Cases { get; } =
    [
        new(
            "unmatched-proxy",
            ExistingType("Probe.Noise", "NoiseAProxy"),
            ExistingType("Probe.Noise", "NoiseBProxy"),
            FingerprintExpectation: FingerprintExpectation.Equivalent),
        new(
            "unmatched-dispatcher",
            ExistingType("Probe.Noise", "NoiseADispatcher"),
            ExistingType("Probe.Noise", "NoiseBDispatcher"),
            FingerprintExpectation: FingerprintExpectation.Equivalent),
        new(
            "unmatched-async",
            ExistingType("Probe.Noise", "NoiseAAsync"),
            ExistingType("Probe.Noise", "NoiseBAsync"),
            FingerprintExpectation: FingerprintExpectation.Equivalent),
        new(
            "filtered",
            ExistingType("Probe.Noise", "NoiseA"),
            ExistingType("Probe.Noise", "NoiseB"),
            FingerprintExpectation: FingerprintExpectation.Equivalent),
        new(
            "same-key-location",
            ExistingType("Probe.Services", "Service0Proxy"),
            "namespace Probe.Services;\n// retained collision key, moved location\npublic sealed class Service0Proxy { }\n",
            FirstDiagnostics: CollisionDiagnosticCount.One,
            SecondDiagnostics: CollisionDiagnosticCount.One,
            FingerprintExpectation: FingerprintExpectation.DiagnosticLocationOnly),
        new(
            "real-proxy-collision",
            ExistingType("Probe.Noise", "NoiseAProxy"),
            ExistingType("Probe.Services", "Service0Proxy"),
            SecondDiagnostics: CollisionDiagnosticCount.One),
        new(
            "real-dispatcher-collision",
            ExistingType("Probe.Noise", "NoiseADispatcher"),
            ExistingType("Probe.Services", "Service0Dispatcher"),
            SecondDiagnostics: CollisionDiagnosticCount.One),
        new(
            "real-async-collision",
            ExistingType("Probe.Noise", "NoiseAAsync"),
            ExistingType("Probe.Services", "IService0Async"),
            SecondDiagnostics: CollisionDiagnosticCount.One),
        new(
            "global-extensions-collision",
            ExistingType("Probe.Noise", "NoiseAProxy"),
            ExistingType("DotBoxD.Services.Generated", "DotBoxDGeneratedExtensions"),
            SecondDiagnostics: CollisionDiagnosticCount.PerService),
        new(
            "global-factory-collision",
            ExistingType("Probe.Noise", "NoiseAProxy"),
            ExistingType("DotBoxD.Services.Generated", "DotBoxDGenerated"),
            SecondDiagnostics: CollisionDiagnosticCount.PerService),
        new(
            "service-method-edit",
            ExistingType("Probe.Noise", "NoiseAProxy"),
            ExistingType("Probe.Noise", "NoiseAProxy"),
            ServiceEdit: ServiceEditKind.Method),
        new(
            "service-identity-edit",
            ExistingType("Probe.Noise", "NoiseAProxy"),
            ExistingType("Probe.Noise", "NoiseAProxy"),
            ServiceEdit: ServiceEditKind.Identity),
        new(
            "service-namespace-edit",
            ExistingType("Probe.Noise", "NoiseAProxy"),
            ExistingType("Probe.Noise", "NoiseAProxy"),
            ServiceEdit: ServiceEditKind.Namespace),
        new(
            "service-add-remove",
            ExistingType("Probe.Noise", "NoiseAProxy"),
            ExistingType("Probe.Noise", "NoiseAProxy"),
            ServiceEdit: ServiceEditKind.AddRemove),
        new(
            "no-edit-warm",
            ExistingType("Probe.Noise", "NoiseA"),
            ExistingType("Probe.Noise", "NoiseA"),
            ReuseFirstCompilation: true,
            FingerprintExpectation: FingerprintExpectation.Equivalent),
    ];

    public static string Services(int serviceCount, ServiceEditKind edit, bool first)
    {
        var @namespace = edit == ServiceEditKind.Namespace && !first
            ? "Probe.RenamedServices"
            : "Probe.Services";
        var actualCount = edit == ServiceEditKind.AddRemove && !first
            ? serviceCount + 1
            : serviceCount;
        var source = new StringBuilder("using DotBoxD.Services.Attributes;\nnamespace ")
            .Append(@namespace)
            .AppendLine(";");
        for (var i = 0; i < actualCount; i++)
        {
            var interfaceName = edit == ServiceEditKind.Identity && !first && i == 0
                ? "IRenamedService0"
                : "IService" + i;
            var methodName = edit == ServiceEditKind.Method && !first && i == 0
                ? "Changed0"
                : "Call" + i;
            source.Append("[RpcService] public interface ")
                .Append(interfaceName)
                .Append(" { int ")
                .Append(methodName)
                .AppendLine("(int value); }");
        }

        return source.ToString();
    }

    private static string ExistingType(string @namespace, string name) =>
        $"namespace {@namespace};\npublic sealed class {name} {{ }}\n";
}

internal sealed record ServiceGeneratorCollisionCaseDefinition(
    string Name,
    string FirstSource,
    string SecondSource,
    CollisionDiagnosticCount FirstDiagnostics = CollisionDiagnosticCount.None,
    CollisionDiagnosticCount SecondDiagnostics = CollisionDiagnosticCount.None,
    bool ReuseFirstCompilation = false,
    ServiceEditKind ServiceEdit = ServiceEditKind.None,
    FingerprintExpectation FingerprintExpectation = FingerprintExpectation.None)
{
    public bool EditsServices => ServiceEdit != ServiceEditKind.None;

    public int SnapshotServiceCount(int serviceCount, bool first) =>
        ServiceEdit == ServiceEditKind.AddRemove && !first ? serviceCount + 1 : serviceCount;

    public int ExpectedDiagnosticCount(int serviceCount, bool first) =>
        (first ? FirstDiagnostics : SecondDiagnostics) switch
        {
            CollisionDiagnosticCount.None => 0,
            CollisionDiagnosticCount.One => 1,
            _ => SnapshotServiceCount(serviceCount, first),
        };

    public int ExpectedSourceCount(int serviceCount, bool first)
    {
        var activeServices = SnapshotServiceCount(serviceCount, first) - ExpectedDiagnosticCount(serviceCount, first);
        return activeServices == 0 ? 0 : (activeServices * 3) + 2;
    }
}

internal enum ServiceEditKind
{
    None,
    Method,
    Identity,
    Namespace,
    AddRemove,
}

internal enum FingerprintExpectation
{
    None,
    Equivalent,
    DiagnosticLocationOnly,
}

internal enum CollisionDiagnosticCount
{
    None,
    One,
    PerService,
}
