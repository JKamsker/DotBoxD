---
title: 'Tutorial: hand-write an IR-backed hook pipeline'
description: 'Build the same shape as a generated hook pipeline using public primitives: package IR, install it, wire it to hooks or subscriptions, and optionally opt a custom fluent surface into analyzer lowering.'
---
The fluent hook chain:

```csharp
server.Hooks.On<MonsterAggroEvent>()
    .Where(e => e.Distance <= 4)
    .Select(e => e.MonsterId)
    .Run((monsterId, ctx) => ctx.Messages.Send(monsterId, "calm"));
```

is convenience, not a privileged path. The generated code produces a `PluginPackage` containing verified
kernel IR, installs that package on a `PluginServer`, and wires the resulting `InstalledKernel` to the
event pipeline. You can do the same thing yourself when you need to import IR from another language, emit
packages from your own build system, or build a hook-like pipeline that is not shaped exactly like DotBoxD's
fluent API.

This tutorial shows the public primitives behind that path:

1. Build or load a `PluginPackage`.
2. Validate/install it under a `SandboxPolicy`.
3. Wire the installed kernel to `server.Hooks` or `server.Subscriptions`.
4. Use `IRBuilder` and `LoweredPipelineComposer` when you want to merge small `Where`/`Select` fragments into one module.
5. Add `[IRBodyOf]` optional `IRFunc`/`IRKernel` parameters when your own fluent API opts into analyzer lowering.

For the generated version of the same idea, read [event pipelines](/tutorials/event-pipeline-runlocal/).

## Step 1 - Know the generated shape

A hook package is just a plugin package with two kernel entrypoints:

- `ShouldHandle(event) -> bool` decides whether the event matches.
- `Handle(event) -> unit` runs the server-side effect, or returns a projection/result for local terminals.

The package also carries a manifest subscription that tells the host which event the kernel belongs to.
Generated packages use this same model:

```csharp
var manifest = new PluginManifest(
    PluginId: "calm-close-monsters",
    Contract: "IEventKernel<MonsterAggroEvent>",
    Mode: ExecutionMode.Auto,
    Effects: ["Cpu", "HostStateWrite", "Audit"],
    LiveSettings: [],
    Subscriptions:
    [
        new HookSubscriptionManifest(
            Event: typeof(MonsterAggroEvent).FullName!,
            Kernel: "calm-close-monsters")
    ])
{
    RequiredCapabilities = [RuntimeCapabilityIds.Async, PluginMessageBindings.CapabilityId]
};

var package = PluginPackage.Create(
    manifest,
    module,
    new KernelEntrypoints("ShouldHandle", "Handle"));
```

`module` is a public `SandboxModule`. You can build it as C# objects, import it from JSON with the kernel
JSON importer, or emit JSON and validate it against the published schemas. The schema reference is at
[Schemas](/reference/schemas/).

## Step 2 - Hand-write the package

For a plain server-side hook, `Handle` returns `unit`. This is the direct equivalent of `.Run(...)`: nothing
crosses back to the plugin process because the server-side IR does the work.

The outline below is intentionally small. It shows the important shape: event fields become entrypoint
parameters, `ShouldHandle` gates on `Distance`, and `Handle` calls a registered host binding.

```csharp
using DotBoxD.Kernels;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;

var span = new SourceSpan(1, 1);

var eventType = typeof(MonsterAggroEvent).FullName!;
var pluginId = "calm-close-monsters";

var module = new SandboxModule(
    Id: pluginId,
    Version: SemVersion.One,
    TargetSandboxVersion: SemVersion.One,
    CapabilityRequests:
    [
        new CapabilityRequest(RuntimeCapabilityIds.Async, "host message send is asynchronous"),
        new CapabilityRequest(PluginMessageBindings.CapabilityId, "send calm messages")
    ],
    Functions:
    [
        new SandboxFunction(
            Id: "ShouldHandle",
            IsEntrypoint: true,
            Parameters: EventParameters(),
            ReturnType: SandboxType.Bool,
            Body:
            [
                new ReturnStatement(
                    new BinaryExpression(
                        new VariableExpression("e_Distance", span),
                        "<=",
                        new LiteralExpression(SandboxValue.FromInt32(4), span),
                        span),
                    span)
            ]),

        new SandboxFunction(
            Id: "Handle",
            IsEntrypoint: true,
            Parameters: EventParameters(),
            ReturnType: SandboxType.Unit,
            Body:
            [
                new ExpressionStatement(
                    new CallExpression(
                        PluginMessageBindings.SendBindingId,
                        [
                            new VariableExpression("e_MonsterId", span),
                            new LiteralExpression(SandboxValue.FromString("calm"), span)
                        ],
                        GenericType: null,
                        span),
                    span),
                new ReturnStatement(new LiteralExpression(SandboxValue.Unit, span), span)
            ])
    ],
    Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["kernel"] = pluginId,
        ["pluginId"] = pluginId
    });

var package = PluginPackage.Create(
    new PluginManifest(
        pluginId,
        $"IEventKernel<{eventType}>",
        ExecutionMode.Auto,
        Effects: ["Cpu", "HostStateWrite", "Audit"],
        LiveSettings: [],
        Subscriptions:
        [
            new HookSubscriptionManifest(eventType, pluginId)
        ])
    {
        RequiredCapabilities = [RuntimeCapabilityIds.Async, PluginMessageBindings.CapabilityId]
    },
    module,
    new KernelEntrypoints("ShouldHandle", "Handle"));

static Parameter[] EventParameters() =>
[
    new("e_MonsterId", SandboxType.String),
    new("e_PlayerId", SandboxType.String),
    new("e_Distance", SandboxType.I32),
    new("e_MonsterLevel", SandboxType.I32),
    new("e_PlayerLevel", SandboxType.I32)
];
```

The names above are not magic to C#; they are the parameter names your event adapter maps into the sandbox
call. A generated adapter uses the event contract. A hand-written adapter can choose its own names, as long
as `ShouldHandle`, `Handle`, and the adapter agree.

If `Handle` calls a host binding with a required capability, include that capability in the IR module and
manifest path the same way generated packages do. The host still decides whether the policy grants it; a
hand-written package does not bypass validation, capability checks, effect checks, or metering.

## Step 3 - Install and wire it

The shortest server-side route is `InstallAsync` plus typed `Use`:

```csharp
var policy = SandboxPolicyBuilder.Create()
    .GrantLogging()
    .GrantHostMessageWrite()
    .WithFuel(100_000)
    .WithMaxHostCalls(1_000)
    .Build();

using var server = PluginServer.Create(
    messages,
    defaultPolicy: policy);

using var session = server.CreateSession();
var kernel = await session.InstallAsync(package, policy);

server.Hooks
    .On<MonsterAggroEvent>()
    .Use(kernel);
```

Now publishing a `MonsterAggroEvent` runs `ShouldHandle` first. If it returns `true`, the sandbox invokes
`Handle`.

For a production plugin connection, prefer the staged helper because it keeps ownership, rollback, and
hot-replace behavior correct:

```csharp
using var session = server.CreateSession();

await session.InstallAndWireAsync(
    package,
    wire: installed => server.Hooks.On<MonsterAggroEvent>().Use(installed),
    policy: _ => policy);
```

`InstallAndWireAsync` is not special access. It is the public convenience over:

```csharp
var staged = await session.InstallStagedAsync(package, policy);
try
{
    server.Hooks.On<MonsterAggroEvent>().Use(staged);
    session.Promote(staged);
}
catch
{
    session.Uninstall(staged.InstallId);
    throw;
}
```

Use the staged form when a failed wire must not revoke a live incumbent package.

## Step 4 - Route by manifest instead of event type

If your host receives packages from plugins, it usually should not switch on every event type by hand. Register
event adapters with the server, install the package, then let the router resolve the manifest event name and
select the correct terminal:

```csharp
await session.InstallAndWireAsync(
    package,
    wire: installed => server.WireHook(installed),
    policy: package => BuildPolicyFromTrustedHostGrants(package),
    validate: package => ValidateHostRoute(package));
```

`WireHook` recomputes the trusted terminal classification from verified package metadata and calls the typed
pipeline for you:

| Classified terminal | Typed equivalent |
|---|---|
| plain server-side hook | `server.Hooks.On<TEvent>().Use(installed)` |
| remote `RunLocal` projection | `server.Hooks.On<TEvent>().UseProjecting(installed, subscriptionId, push)` |
| server-side `Register` result hook | `server.Hooks.On<TEvent>().UseResult(installed, resultType, priority)` |
| remote `RegisterLocal` result hook | `server.Hooks.On<TEvent>().UseProjectingResult(installed, subscriptionId, resultType, request, priority)` |

For fire-and-forget events, route through `server.WireSubscription(installed)` instead. Subscriptions reject
result terminals because there is no decision value to return.

## Step 5 - Build filter/projection IR from fragments

If you are building a hook-like pipeline, do not make every stage emit a complete module. Let each stage emit
a small `LoweredPipelineStep`, then compose the ordered steps once. For dynamic server-side filters and
projections, use `IRBuilder.For<TEvent>()` instead of hand-assembling the `$dotboxd.current` parameter and
manifest shape tags yourself:

```csharp
using DotBoxD.Plugins;

var maxDistance = 4;
var ir = IRBuilder.For<MonsterAggroEvent>();
var steps = new[]
{
    ir.FilterStep(e => e.LessThanOrEqual(e.Field(2), e.Int32(maxDistance))),
    ir.ProjectionStep<string>(e => e.Field(0))
};
```

`Field(index)` reads the event DTO in its marshalled order: public readable properties first, then public
fields. The builder validates the input/output CLR types through the same RPC marshaller used by the plugin
runtime, produces generator-compatible manifest tags, and returns the same public `IRFunc` /
`LoweredPipelineStep` carriers that analyzer-generated calls use.

When a dynamic fragment depends on host-bound operations or audited effects, declare that metadata at the
same place you build the step:

```csharp
var filtered = ir.FilterStep(
    e => e.LessThanOrEqual(e.Field(2), e.Int32(maxDistance)),
    requiredCapabilities: ["world.monsters.read"],
    effects: ["Cpu"]);
```

```csharp
var composed = LoweredPipelineComposer.Compose(new LoweredPipelineComposition(
    ModuleId: "calm-close-monsters",
    Steps: steps,
    ResultType: SandboxType.String)
{
    Version = SemVersion.One,
    TargetSandboxVersion = SemVersion.One,
    ShouldHandleFunctionId = "ShouldHandle",
    HandleFunctionId = "Handle"
});
```

The composer produces the same two-entrypoint shape described above:

- `ShouldHandle(input) -> bool` threads the input through filters and stops on the first miss.
- `Handle(input) -> ResultType` applies projections and returns the final value.

That is useful for a remote `RunLocal` style package, where `Handle` should return the projected value instead
of performing a host-side effect. Mark the subscription as a local terminal:

```csharp
new HookSubscriptionManifest(eventType, pluginId)
{
    LocalTerminal = true,
    ProjectedType = "string"
};
```

The remote client allocates a callback route, installs the package with that route id, and registers the native
delegate on its side. The server validates the package, runs `ShouldHandle` and `Handle`, then pushes only the
encoded projection for matching events.

## Step 6 - Keep the security boundary boring

Treat generated and hand-written packages the same:

- Validate the package before install.
- Derive the policy from the host's grants, not from trust in the plugin.
- Register only the host bindings the sandbox is allowed to call.
- Keep `ShouldHandle` pure; if it has side effects, remember it is allowed to run more often than `Handle`.
- Route local terminals only when the connection supplied the required push/result callback.
- Uninstall by install id during rollback, not by plugin id, so a failed replacement does not remove the
  incumbent.

The important design point is that hand-written IR goes through the same `SandboxHost` validation and execution
path as generated IR. The generator saves authoring effort; it does not grant authority.

## Step 7 - Optional: expose a lowerable fluent surface

DotBoxD's analyzer recognizes the built-in pipeline role names (`Where`, `Select`, `Run`, `RunLocal`,
`Register`, and `RegisterLocal`). If you want a custom surface to be lowered automatically, keep those names
and make the lowered body explicit in the method signature. The delegate stays as the ergonomic authoring API.
An optional `IRFunc` parameter carries a stage fragment, and an optional `IRKernel` parameter carries a
generated terminal package:

```csharp
using System.Collections.Generic;
using DotBoxD.Abstractions;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

[PipelineSurface(PipelineTransport.Remote)]
public sealed class MyRemotePipeline<TEvent>
{
    private readonly List<LoweredPipelineStep> _steps = [];

    public MyRemotePipeline<TEvent> Where(
        Func<TEvent, bool> predicate,
        [IRBodyOf(nameof(predicate), LoweredPipelineStepKind.Filter)]
        IRFunc<TEvent, bool>? irPredicate = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(irPredicate);
        _steps.Add(irPredicate.Step);
        return this;
    }

    public MyRemoteStage<TEvent, TNext> Select<TNext>(
        Func<TEvent, TNext> selector,
        [IRBodyOf(nameof(selector))] IRFunc<TEvent, TNext>? irSelector = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(irSelector);
        _steps.Add(irSelector.Step);
        return new(this);
    }

    public MyRemotePipeline<TEvent> Run(
        Action<TEvent, HookContext> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(irHandler);
        return UseGeneratedChain(irHandler.Package);
    }

    public MyRemotePipeline<TEvent> UseGeneratedChain(PluginPackage package)
    {
        // Install the generated package through your transport/session.
        return this;
    }
}

[PipelineSurface(PipelineTransport.Remote)]
public sealed class MyRemoteStage<TEvent, TCurrent>
{
    public MyRemoteStage(MyRemotePipeline<TEvent> root) => Root = root;

    private MyRemotePipeline<TEvent> Root { get; }

    public MyRemotePipeline<TEvent> Run(
        Action<TCurrent, HookContext> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(irHandler);
        return Root.UseGeneratedChain(irHandler.Package);
    }
}
```

A user can now write a chain on your surface:

```csharp
pipeline
    .Where(e => e.Distance <= 4)
    .Select(e => e.MonsterId)
    .Run((monsterId, ctx) => ctx.Messages.Send(monsterId, "calm"));
```

The terminal still needs a public install method such as `UseGeneratedChain(PluginPackage package)` (and the
local-terminal equivalents if you support `RunLocal`/`RegisterLocal`). The generated output calls public API
you could have called yourself:

```csharp
pipeline.Where(
    e => e.Distance <= 4,
    IRBuilder.For<MonsterAggroEvent>().Filter(e => e.LessThanOrEqual(e.Field(2), e.Int32(4))));
```

That is the important boundary: the generator supplies the `IRFunc` automatically, but the API does not depend
on an internal overload. If you prefer domain-specific method names, keep them as a hand-written layer that
builds `IRFunc` values with `IRBuilder` and calls the same public primitive methods; those names are not
automatically lowered by the analyzer. Older `[LowerToIr]` methods with a sibling `LoweredPipelineStep`
overload still work for compatibility, but new custom fluent surfaces should prefer the explicit `IRBodyOf`
shape so the lowering contract is visible and hand-writable.

## Next steps

- [Event pipelines](/concepts/event-pipelines/) - the generated hook model and terminal matrix.
- [Schemas](/reference/schemas/) - JSON contracts for hand-authored kernel and plugin packages.
- [Kernels](/concepts/kernels/) - the sandbox execution model used by generated and hand-written IR.
