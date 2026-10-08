using System.Collections.Immutable;
using System.Security.Claims;
using BenchmarkDotNet.Attributes;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.UI;
using DotBoxD.UI.Blazor;
using DotBoxD.UI.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotBoxD.Kernels.Benchmarks.UI;

[MemoryDiagnoser]
public class UiBlazorBenchmarks
{
    private SandboxHost _sandbox = null!;
    private UiHost _host = null!;
    private UiPackage _package = null!;
    private UiSession _session = null!;
    private BlazorUiRenderer _renderer = null!;
    private ServiceProvider _services = null!;
    private HtmlRenderer _html = null!;
    private ImmutableArray<UiListItem> _rows;
    private ImmutableArray<UiListItem> _reverse;
    private ImmutableArray<UiStateValue> _batch;
    private long _version;
    private readonly ClaimsPrincipal _user = new();
    private readonly AllowInput _authorizer = new();

    [Params(10, 100, 1_000)] public int Nodes { get; set; }
    [Params(100, 1_000)] public int Items { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings());
        _host = new UiHost(_sandbox, SandboxPolicyBuilder.Create().Build(), new UiPolicy { MaxNodes = 4_000 });
        _rows = [.. Enumerable.Range(0, Items).Select(i => new UiListItem(i.ToString(System.Globalization.CultureInfo.InvariantCulture), "row"))];
        _reverse = [.. _rows.Reverse()];
        _batch = [.. Enumerable.Range(3, 100).Select(i => new UiStateValue(i, UiValue.FromInt32(1)))];
        _package = new UiPackage(1, 1,
            [new(1, UiPrimitive.Stack, [.. Enumerable.Range(2, Nodes - 1)], []),
             new(2, UiPrimitive.Items, [], [new(UiPropertyId.Items, StateSlotId: 2)]),
             .. Enumerable.Range(3, Nodes - 2).Select(i => new UiNode(i, i == 3 ? UiPrimitive.TextBox : i == 4 ? UiPrimitive.Button : UiPrimitive.Text, [],
                 [new(UiPropertyId.Text, StateSlotId: 1, TwoWay: i == 3)]))],
            [new(1, UiValue.FromString("")), new(2, UiValue.FromItems(_rows)),
             .. Enumerable.Range(3, 100).Select(i => new UiStateSlot(i, UiValue.FromInt32(0)))],
            [new(1, """
            {"id":"ui-blazor-bench","version":"1.0.0","functions":[{"id":"main","visibility":"entrypoint",
            "parameters":[{"name":"value","type":"I32"}],"returnType":"I32",
            "body":[{"op":"return","value":{"op":"add","left":{"var":"value"},"right":{"i32":1}}}]}]}
            """, "main", 3)],
            [new(1, 4, UiEventKind.Click, UiEventTarget.LocalKernel, KernelId: 1, OutputSlotId: 3)], []);
        _renderer = new BlazorUiRenderer(new UiPolicy { MaxInputEventsPerSecond = int.MaxValue });
        _session = _host.InstallAsync(_package, _renderer).AsTask().GetAwaiter().GetResult();
        _services = new ServiceCollection().AddLogging().BuildServiceProvider();
        _html = new HtmlRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
        _html.Dispatcher.InvokeAsync(() => _html.RenderComponentAsync<DotBoxDUi>(Parameters(_session))).GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task InstallAttachDetach()
    {
        var session = await _host.InstallAsync(_package, new BlazorUiRenderer());
        await using var html = new HtmlRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
        await html.Dispatcher.InvokeAsync(() => html.RenderComponentAsync<DotBoxDUi>(Parameters(session)));
    }

    [Benchmark]
    public async Task DisconnectReconnect()
    {
        var disconnected = await _host.InstallAsync(_package, new BlazorUiRenderer());
        var previousId = disconnected.Id;
        await disconnected.DisposeAsync();
        await using var reconnected = await _host.InstallAsync(_package, new BlazorUiRenderer());
        if (reconnected.Id == previousId)
        { throw new InvalidOperationException("Reconnection reused a session incarnation."); }
        await using var html = new HtmlRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
        await html.Dispatcher.InvokeAsync(() => html.RenderComponentAsync<DotBoxDUi>(Parameters(reconnected)));
    }

    [Benchmark]
    public async Task LocalButton()
    {
        var snapshot = await _session.DispatchAsync(1);
        _version = snapshot.Version;
    }

    [Benchmark]
    public async Task StateOnlyRerender()
    {
        var snapshot = await _session.SetInputAsync(3, UiPropertyId.Text, UiValue.FromString((_version % 2).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _version = snapshot.Version;
    }

    [Benchmark]
    public async Task BrowserTextAdmission()
    {
        await _renderer.SubmitAsync(_session, new UiInput(NodeId: 3, PropertyId: UiPropertyId.Text,
            Value: UiValue.FromString((_version % 2).ToString(System.Globalization.CultureInfo.InvariantCulture))), _user, _authorizer);
        UiSnapshot snapshot;
        do
        { await Task.Yield(); snapshot = await _session.SnapshotAsync(); } while (snapshot.Version == _version);
        _version = snapshot.Version;
    }

    [Benchmark]
    public async Task Batch100Slots()
    {
        var snapshot = await _session.ApplyPatchAsync(new UiStatePatch(_session.Id, _version, _batch));
        _version = snapshot.Version;
    }

    [Benchmark]
    public async Task KeyedReorder()
    {
        var snapshot = await _session.ApplyPatchAsync(new UiStatePatch(_session.Id, _version,
            [new(2, UiValue.FromItems(_version % 2 == 0 ? _reverse : _rows))]));
        _version = snapshot.Version;
    }

    private static ParameterView Parameters(UiSession session) => ParameterView.FromDictionary(new Dictionary<string, object?>
    { [nameof(DotBoxDUi.Session)] = session });

    [GlobalCleanup]
    public void Cleanup()
    {
        _html.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _services.Dispose();
        _sandbox.Dispose();
    }

    private sealed class AllowInput : IUiInteractionAuthorizer
    {
        public ValueTask<bool> AuthorizeAsync(ClaimsPrincipal user, UiSession session, UiInput input, CancellationToken cancellationToken)
            => ValueTask.FromResult(true);
    }
}
