# Kernel runtime

The kernel runtime executes validated IR under hard budgets. Key pieces:

## Two execution backends

- **Interpreter** (`DotBoxD.Kernels.Interpreter`) — executes verified IR directly. Predictable
  semantics, easy quotas, great diagnostics, AOT-friendly, no codegen. This is the default and the
  safety baseline.
- **Compiler** (`DotBoxD.Kernels.Compiler`) — emits verified IL for hot kernels and caches the artifact
  (content-addressed by module hash + entrypoint + policy hash + compiler version). The emitted assembly
  is checked by `DotBoxD.Kernels.Verifier` before it runs, so the compiled path enforces the same
  restrictions as the interpreter.

## Metering & policy

Every run is bounded by a `SandboxPolicy`:

- **fuel** (instruction budget), **loop iteration** and **call-depth** budgets,
- **list/collection cardinality** and **output-byte** budgets,
- **capability grants** (e.g. `file.read`, `net.http.get`) with parameters, expiry, and per-capability
  quotas,
- **effect** controls (`Cpu`, `Alloc`, file/network/host effects, `Time`, `Random`, `Concurrency`,
  `Audit`), with a deterministic mode (logical clock + seeded random) available.

## Effects & capabilities

Bindings (`DotBoxD.Kernels.Runtime`, `DotBoxD.Hosting.Http`) are the only way a kernel reaches outside
pure computation, and only when the policy grants the matching capability. This is what makes
author-supplied logic safe to run in-process. See
[security/sandbox-caveats.md](../security/sandbox-caveats.md) and the full specification under
[`docs/Specs/`](../Specs/).

Async-capable bindings are opt-in. A binding marked `BindingDescriptor.IsAsync` adds the
`Concurrency` effect and requires the `dotboxd.runtime.async` runtime capability. Hosts grant it with
`SandboxPolicyBuilder.AllowRuntimeAsync()`; without that grant, preparation fails closed and the
runtime backstop rejects genuinely pending `ValueTask` results.

When a plugin authoring interface uses `[HostBinding]`, set the additive
`HostBindingAttribute.IsAsync` named property to mirror the registered descriptor's `IsAsync` value.
The property defaults to `false`, so existing source remains compatible while async host bindings can
derive `dotboxd.runtime.async` into generated manifests.

## Typed server contexts

Hook and subscription chains pass two different values through their fluent lambdas:

- the first parameter is the event, hook result context, or projected value currently flowing through
  the chain;
- the second parameter is the server context selected by the host.

Servers can select hook and subscription contexts independently:

```csharp
var server = PluginServer.Create(messages, configureHost: AddBindings, defaultPolicy: policy)
    .WithHookContext<GameHookContext>(ctx => new GameHookContext(ctx, world))
    .WithSubscriptionContext<GameSubscriptionContext>(ctx => new GameSubscriptionContext(ctx, audience));
```

Plugin authors then use the same value-only shorthand or two-argument full form on each fluent stage:

```csharp
server.Hooks.On<DamageContext>()
    .Where((damage, ctx) => ctx.CanInspect(damage.Amount))
    .Register((damage, ctx) => new DamageResult
    {
        Success = true,
        Damage = damage.Amount + ctx.DamageAdjustment,
        Reason = ctx.DecisionLabel,
    });

server.Subscriptions.On<DamageEvent>()
    .Where((damage, ctx) => ctx.ShouldReceive(damage.TargetId))
    .RunLocal((damage, ctx) => ctx.DeliverLocal(damage));
```

`RunLocal` and `RegisterLocal` delegates execute as native plugin/client code, so they can call ordinary
members on the configured context object. `Where`, `Select`, `Run`, and `Register` are lowered to
verified IR when they run in the sandbox. Context members used there must be explicit lowering markers:

- use `[KernelMethod]` on expression-bodied or single-return helper methods that should inline into IR;
- use `[HostBinding]` on context properties or methods that should lower to registered host bindings.

For example:

```csharp
public sealed class GameHookContext
{
    public GameHookContext(HookContext inner, GameWorld world)
    {
        Inner = inner;
        World = world;
    }

    public HookContext Inner { get; }

    public GameWorld World { get; }

    public string LocalShard => World.ShardName; // native-only; valid in RunLocal/RegisterLocal

    [KernelMethod]
    public bool CanInspect(int amount) => amount <= 100;

    [HostBinding(
        "game.damage.adjustment",
        "game.damage.read",
        SandboxEffect.Cpu | SandboxEffect.HostStateRead | SandboxEffect.Audit)]
    public int DamageAdjustment
        => throw new NotSupportedException("Lowering marker; implemented by a host binding.");

    [HostBinding(
        "game.damage.decisionLabel",
        "game.damage.read",
        SandboxEffect.Cpu | SandboxEffect.HostStateRead | SandboxEffect.Audit)]
    public string DecisionLabel
        => throw new NotSupportedException("Lowering marker; implemented by a host binding.");
}
```
