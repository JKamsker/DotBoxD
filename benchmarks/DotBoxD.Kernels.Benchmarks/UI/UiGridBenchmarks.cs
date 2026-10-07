using Avalonia.Headless;
using BenchmarkDotNet.Attributes;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.UI;
using DotBoxD.UI.Authoring;
using DotBoxD.UI.Avalonia;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Benchmarks.UI;

[MemoryDiagnoser]
public class UiGridBenchmarks
{
    private HeadlessUnitTestSession _dispatcher = null!;
    private SandboxHost _sandbox = null!;
    private UiSession _session = null!;
    private long _version;

    [Params(100, 1_000)]
    public int Nodes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _dispatcher = HeadlessUnitTestSession.StartNew(typeof(UiBenchmarkApplication));
        _sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings());
        var b = new UiBuilder();
        var row = b.State(0);
        var moving = b.Element(UiPrimitive.Text, [UiLiteral.Text("moving").Property(UiPropertyId.Text),
            ((UiBinding<int>)row).Property(UiPropertyId.Row)]);
        var package = b.Build(b.Grid(64, [moving, .. Enumerable.Range(0, Nodes - 2).Select(_ => b.Text("fixed"))]));
        var host = new UiHost(_sandbox, SandboxPolicyBuilder.Create().Build());
        _session = _dispatcher.Dispatch(async () => await host.InstallAsync(package, new AvaloniaUiRenderer()),
            CancellationToken.None).GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task UpdateSparseGridRow()
    {
        var snapshot = await _dispatcher.Dispatch(async () => await _session.ApplyPatchAsync(new UiStatePatch(_session.Id, _version,
            [new UiStateValue(1, UiValue.FromInt32(_version % 2 == 0 ? 31 : 0))])), CancellationToken.None);
        _version = snapshot.Version;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dispatcher.Dispatch(async () => { await _session.DisposeAsync(); return true; }, CancellationToken.None).GetAwaiter().GetResult();
        _sandbox.Dispose();
        _dispatcher.Dispose();
    }
}
