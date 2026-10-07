using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Benchmarks.UI;

[MemoryDiagnoser]
public class UiBenchmarks
{
    private readonly UiPolicy _policy = new();
    private SandboxHost _sandbox = null!;
    private UiHost _host = null!;
    private UiPackage _package = null!;
    private UiSession _session = null!;
    private ImmutableArray<UiStateValue> _batch;
    private string _json = null!;
    private long _version;

    [Params(100, 1_000)]
    public int Nodes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings());
        _host = new UiHost(_sandbox, SandboxPolicyBuilder.Create().Build(), _policy);
        _package = new UiPackage(1, 1,
            [new UiNode(1, UiPrimitive.Stack, [.. Enumerable.Range(2, Nodes - 1)], []),
             new UiNode(2, UiPrimitive.TextBox, [], [new UiProperty(UiPropertyId.Text, StateSlotId: 1, TwoWay: true)]),
             new UiNode(3, UiPrimitive.Button, [], [new UiProperty(UiPropertyId.Text, UiValue.FromString("Increment"))]),
             .. Enumerable.Range(4, Nodes - 3).Select(id => new UiNode(id, UiPrimitive.Text, [],
                 [new UiProperty(UiPropertyId.Text, StateSlotId: 1)]))],
            [new UiStateSlot(1, UiValue.FromString("")), .. Enumerable.Range(2, 100).Select(id => new UiStateSlot(id, UiValue.FromInt32(0)))],
            [new UiKernel(1, """
            {"id":"bench","version":"1.0.0","capabilityRequests":[],"functions":[{"id":"main","visibility":"entrypoint",
            "parameters":[{"name":"value","type":"I32"}],"returnType":"I32",
            "body":[{"op":"return","value":{"op":"add","left":{"var":"value"},"right":{"i32":1}}}]}]}
            """, "main", 2)],
            [new UiEvent(1, 3, UiEventKind.Click, UiEventTarget.LocalKernel, KernelId: 1, OutputSlotId: 2)], []);
        _json = UiPackageJson.Export(_package, _policy);
        _batch = [.. Enumerable.Range(2, 100).Select(id => new UiStateValue(id, UiValue.FromInt32(1)))];
        _session = _host.InstallAsync(_package, new NullRenderer()).AsTask().GetAwaiter().GetResult();
    }

    [Benchmark]
    public void Validate() => UiPackageValidator.Validate(_package, _policy);

    [Benchmark]
    public UiPackage Import() => UiPackageJson.Import(_json, _policy);

    [Benchmark]
    public async Task InstallMaterialize()
    {
        await using var session = await _host.InstallAsync(_package, new NullRenderer());
    }

    [Benchmark]
    public async Task LocalButton()
    {
        var snapshot = await _session.DispatchAsync(1);
        _version = snapshot.Version;
    }

    [Benchmark]
    public async Task TwoWayText()
    {
        var snapshot = await _session.SetInputAsync(2, UiPropertyId.Text, UiValue.FromString((_version % 2).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _version = snapshot.Version;
    }

    [Benchmark]
    public async Task Batch100Slots()
    {
        var snapshot = await _session.ApplyPatchAsync(new UiStatePatch(_session.Id, _version, _batch));
        _version = snapshot.Version;
    }

    [Benchmark]
    public void RejectOverLimit()
    {
        try
        { UiPackageValidator.Validate(_package, _policy with { MaxNodes = 1 }); }
        catch (UiValidationException) { }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _sandbox.Dispose();
    }

    private sealed class NullRenderer : IUiRenderer
    {
        public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
