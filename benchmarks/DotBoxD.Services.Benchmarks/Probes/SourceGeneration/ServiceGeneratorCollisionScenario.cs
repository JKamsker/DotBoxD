using System.Collections.Immutable;
using System.Diagnostics;
using DotBoxD.Services.Attributes;
using DotBoxD.Services.SourceGenerator.EntryPoint;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.Benchmarks.Probes;

internal sealed class ServiceGeneratorCollisionScenario
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();

    private readonly CSharpCompilation _firstCompilation;
    private readonly CSharpCompilation _secondCompilation;
    private readonly GeneratorRunFingerprint _firstFingerprint;
    private readonly GeneratorRunFingerprint _secondFingerprint;
    private GeneratorDriver _driver;

    private ServiceGeneratorCollisionScenario(
        int serviceCount,
        ServiceGeneratorCollisionCaseDefinition definition,
        CSharpCompilation firstCompilation,
        CSharpCompilation secondCompilation,
        GeneratorDriver driver,
        GeneratorRunFingerprint firstFingerprint,
        GeneratorRunFingerprint secondFingerprint)
    {
        ServiceCount = serviceCount;
        Definition = definition;
        _firstCompilation = firstCompilation;
        _secondCompilation = secondCompilation;
        _driver = driver;
        _firstFingerprint = firstFingerprint;
        _secondFingerprint = secondFingerprint;
    }

    public int ServiceCount { get; }

    public ServiceGeneratorCollisionCaseDefinition Definition { get; }

    public GeneratorRunFingerprint FirstFingerprint => _firstFingerprint;

    public GeneratorRunFingerprint SecondFingerprint => _secondFingerprint;

    public static ServiceGeneratorCollisionScenario Create(
        int serviceCount,
        ServiceGeneratorCollisionCaseDefinition definition,
        bool trackSteps = false)
    {
        var firstServiceSource = ServiceGeneratorCollisionSources.Services(
            serviceCount,
            definition.ServiceEdit,
            first: true);
        var secondServiceSource = ServiceGeneratorCollisionSources.Services(
            serviceCount,
            definition.ServiceEdit,
            first: false);
        var firstServices = CSharpSyntaxTree.ParseText(
            firstServiceSource,
            ParseOptions,
            "Services.cs");
        var secondServices = CSharpSyntaxTree.ParseText(
            secondServiceSource,
            ParseOptions,
            "Services.cs");
        var firstExistingType = CSharpSyntaxTree.ParseText(
            definition.FirstSource,
            ParseOptions,
            "ExistingType.cs");
        var secondExistingType = CSharpSyntaxTree.ParseText(
            definition.SecondSource,
            ParseOptions,
            "ExistingType.cs");
        var firstCompilation = CSharpCompilation.Create(
            $"ServiceGeneratorCollision_{definition.Name}_{serviceCount}",
            [firstServices, firstExistingType],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var secondCompilation = definition.ReuseFirstCompilation
            ? firstCompilation
            : definition.EditsServices
                ? firstCompilation.ReplaceSyntaxTree(firstServices, secondServices)
                : firstCompilation.ReplaceSyntaxTree(firstExistingType, secondExistingType);

        var driver = CreateDriver(trackSteps).RunGenerators(firstCompilation);
        var firstFingerprint = GeneratorRunFingerprint.Create(driver.GetRunResult());
        ValidateShape(firstFingerprint, serviceCount, definition, first: true);
        driver = driver.RunGenerators(secondCompilation);
        var secondFingerprint = GeneratorRunFingerprint.Create(driver.GetRunResult());
        ValidateShape(secondFingerprint, serviceCount, definition, first: false);
        ValidateFingerprintExpectation(definition, firstFingerprint, secondFingerprint);

        return new ServiceGeneratorCollisionScenario(
            serviceCount,
            definition,
            firstCompilation,
            secondCompilation,
            driver,
            firstFingerprint,
            secondFingerprint);
    }

    public void Warm(int iterations)
    {
        for (var i = 0; i < iterations; i++)
        {
            Run(useFirst: (i & 1) == 0);
        }

        ValidateBothSnapshots();
    }

    public ServiceGeneratorProbeMeasurement Measure(int iterations)
    {
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            Run(useFirst: (i & 1) == 0);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        ValidateBothSnapshots();
        return new ServiceGeneratorProbeMeasurement(elapsed, allocated, iterations);
    }

    public ServiceGeneratorProbeMeasurement MeasureCold(int iterations)
    {
        for (var i = 0; i < 2; i++)
        {
            _ = CreateDriver(trackSteps: false).RunGenerators(_firstCompilation);
        }

        ForceGc();
        GeneratorDriver? lastDriver = null;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            lastDriver = CreateDriver(trackSteps: false).RunGenerators(_firstCompilation);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var fingerprint = GeneratorRunFingerprint.Create(lastDriver!.GetRunResult());
        RequireFingerprint(_firstFingerprint, fingerprint, "cold generation");
        return new ServiceGeneratorProbeMeasurement(elapsed, allocated, iterations);
    }

    public string CurrentTrackedReasons() =>
        GeneratorTrackedReasonFingerprint.Create(_driver.GetRunResult());

    public void Apply(bool useFirst)
    {
        Run(useFirst);
        var actual = GeneratorRunFingerprint.Create(_driver.GetRunResult());
        RequireFingerprint(useFirst ? _firstFingerprint : _secondFingerprint, actual, Definition.Name);
    }

    public string ManifestEntry()
    {
        var firstServiceInputHash = GeneratorRunFingerprint.HashText(
            "DotBoxD.Services.SourceGenerator.Input/v1",
            ServiceGeneratorCollisionSources.Services(ServiceCount, Definition.ServiceEdit, first: true));
        var secondServiceInputHash = GeneratorRunFingerprint.HashText(
            "DotBoxD.Services.SourceGenerator.Input/v1",
            ServiceGeneratorCollisionSources.Services(ServiceCount, Definition.ServiceEdit, first: false));
        var firstInputHash = GeneratorRunFingerprint.HashText(
            "DotBoxD.Services.SourceGenerator.Input/v1",
            Definition.FirstSource);
        var secondInputHash = GeneratorRunFingerprint.HashText(
            "DotBoxD.Services.SourceGenerator.Input/v1",
            Definition.SecondSource);
        return string.Join('|',
            ServiceCount,
            Definition.Name,
            firstServiceInputHash,
            secondServiceInputHash,
            firstInputHash,
            secondInputHash,
            _firstFingerprint.SourceCount,
            _firstFingerprint.SourceHash,
            _firstFingerprint.DiagnosticCount,
            _firstFingerprint.DiagnosticHash,
            _secondFingerprint.SourceCount,
            _secondFingerprint.SourceHash,
            _secondFingerprint.DiagnosticCount,
            _secondFingerprint.DiagnosticHash);
    }

    private static GeneratorDriver CreateDriver(bool trackSteps) =>
        CSharpGeneratorDriver.Create(
            generators: [new DotBoxDRpcGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(default, trackSteps));

    private static ImmutableArray<MetadataReference> CreateReferences() =>
        (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
            .ToImmutableArray();

    private static void ValidateShape(
        GeneratorRunFingerprint fingerprint,
        int serviceCount,
        ServiceGeneratorCollisionCaseDefinition definition,
        bool first)
    {
        var expectedDiagnostics = definition.ExpectedDiagnosticCount(serviceCount, first);
        var expectedSources = definition.ExpectedSourceCount(serviceCount, first);
        if (fingerprint.DiagnosticCount != expectedDiagnostics || fingerprint.SourceCount != expectedSources)
        {
            throw new InvalidOperationException(
                $"{definition.Name} produced {fingerprint.SourceCount} sources/{fingerprint.DiagnosticCount} diagnostics; " +
                $"expected {expectedSources}/{expectedDiagnostics}.");
        }
    }

    private static void ValidateFingerprintExpectation(
        ServiceGeneratorCollisionCaseDefinition definition,
        GeneratorRunFingerprint first,
        GeneratorRunFingerprint second)
    {
        switch (definition.FingerprintExpectation)
        {
            case FingerprintExpectation.Equivalent when first != second:
                throw new InvalidOperationException(
                    definition.Name + " must preserve exact generated sources and diagnostics.");
            case FingerprintExpectation.DiagnosticLocationOnly
                when first.SourceCount != second.SourceCount ||
                     first.SourceHash != second.SourceHash ||
                     first.DiagnosticCount != second.DiagnosticCount ||
                     first.DiagnosticHash == second.DiagnosticHash:
                throw new InvalidOperationException(
                    definition.Name + " must change only the canonical diagnostic location fingerprint.");
        }
    }

    private void ValidateBothSnapshots()
    {
        Apply(useFirst: true);
        Apply(useFirst: false);
    }

    private void Run(bool useFirst)
    {
        _driver = _driver.RunGenerators(useFirst ? _firstCompilation : _secondCompilation);
    }

    private static void RequireFingerprint(
        GeneratorRunFingerprint expected,
        GeneratorRunFingerprint actual,
        string operation)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException(operation + " changed generated sources or diagnostics.");
        }
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

internal readonly record struct ServiceGeneratorProbeMeasurement(
    TimeSpan Elapsed,
    long AllocatedBytes,
    int Iterations);
