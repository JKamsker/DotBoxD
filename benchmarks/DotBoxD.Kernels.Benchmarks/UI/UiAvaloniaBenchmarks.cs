using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using BenchmarkDotNet.Attributes;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.UI;
using DotBoxD.UI.Avalonia;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Benchmarks.UI;

[MemoryDiagnoser]
public class UiAvaloniaBenchmarks
{
    private HeadlessUnitTestSession _dispatcher = null!;
    private SandboxHost _sandbox = null!;
    private UiHost _host = null!;
    private UiPackage _package = null!;
    private UiSession _session = null!;
    private TextBox _input = null!;
    private ImmutableArray<UiListItem> _forward;
    private ImmutableArray<UiListItem> _reverse;
    private long _version;

    [Params(100, 1_000)]
    public int Nodes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _dispatcher = HeadlessUnitTestSession.StartNew(typeof(UiBenchmarkApplication));
        _sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings());
        _host = new UiHost(_sandbox, SandboxPolicyBuilder.Create().Build(), new UiPolicy { MaxInputEventsPerSecond = int.MaxValue });
        _forward = [.. Enumerable.Range(0, 50).Select(i => new UiListItem(i.ToString(System.Globalization.CultureInfo.InvariantCulture), "row"))];
        _reverse = [.. _forward.Reverse()];
        _package = new UiPackage(1, 1,
            [new UiNode(1, UiPrimitive.Stack, [.. Enumerable.Range(2, Nodes - 1)], []),
             new UiNode(2, UiPrimitive.Items, [], [new UiProperty(UiPropertyId.Items, StateSlotId: 2)]),
             .. Enumerable.Range(3, Nodes - 2).Select(id => new UiNode(id, id == 3 ? UiPrimitive.TextBox : UiPrimitive.Text, [],
                 [new UiProperty(UiPropertyId.Text, StateSlotId: 1, TwoWay: id == 3)]))],
            [new UiStateSlot(1, UiValue.FromString("")), new UiStateSlot(2, UiValue.FromItems(_forward))], [], [], []);
        _session = _dispatcher.Dispatch(async () =>
        {
            var renderer = new AvaloniaUiRenderer();
            var session = await _host.InstallAsync(_package, renderer);
            _input = (TextBox)((StackPanel)renderer.Root!).Children[1];
            return session;
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task InstallMaterializeAvalonia()
    {
        await _dispatcher.Dispatch(async () =>
        {
            await using var session = await _host.InstallAsync(_package, new AvaloniaUiRenderer());
            return true;
        }, CancellationToken.None);
    }

    [Benchmark]
    public async Task RepeatedStateOnlyRendererUpdate()
    {
        var snapshot = await _dispatcher.Dispatch(async () => await _session.ApplyPatchAsync(new UiStatePatch(_session.Id, _version,
            [new UiStateValue(1, UiValue.FromString((_version % 2).ToString(System.Globalization.CultureInfo.InvariantCulture)))])), CancellationToken.None);
        _version = snapshot.Version;
    }

    [Benchmark]
    public async Task NativeTextInputRoundtrip()
    {
        _version = await _dispatcher.Dispatch(async () =>
        {
            _input.Text = (_version % 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
            UiSnapshot snapshot;
            do
            { await Task.Yield(); snapshot = await _session.SnapshotAsync(); }
            while (snapshot.Version == _version);
            return snapshot.Version;
        }, CancellationToken.None);
    }

    [Benchmark]
    public async Task KeyedListUpdate()
    {
        var snapshot = await _dispatcher.Dispatch(async () => await _session.ApplyPatchAsync(new UiStatePatch(_session.Id, _version,
            [new UiStateValue(2, UiValue.FromItems(_version % 2 == 0 ? _reverse : _forward))])), CancellationToken.None);
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

public sealed class UiBenchmarkApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<UiBenchmarkApplication>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
