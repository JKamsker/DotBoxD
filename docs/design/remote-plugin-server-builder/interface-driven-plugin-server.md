# Interface-driven plugin server + unified game surface — Design

Companion to [plan.md](plan.md), [invoke-async.md](invoke-async.md), and
[concept-naming-decision.md](concept-naming-decision.md).

**Status:** Exploratory design, **vibe-validated** in the GameServer sample (the three sample projects were
rewritten to the target shape; they intentionally **do not compile** — the generated halves and one
framework library are absent). Not yet implemented in the analyzer/runtime. **Date:** 2026-06-16.

This record supersedes the **hand-written-control** portions of [plan.md](plan.md): the remote-control
surface is no longer authored by hand in the sample. Instead it is **declared as interfaces** in the game
abstractions and **source-generated** on the plugin side. The lifecycle/naming decisions from
[plan.md](plan.md) (synchronous `Build()`, async `StartAsync()` does the I/O) and the verb names from
[concept-naming-decision.md](concept-naming-decision.md) (`Replace`, `Extend`, top-level `InvokeAsync`) are
preserved.

---

## 1. Problem

Today every plugin-side surface — `RemotePluginServer` (facade), `RemotePluginServerBuilder`,
`RemoteServiceControl`, `RemoteWorldControl`, `RemoteMonsterControl`, `RemoteEntityControl`,
`RemoteServerExtensionControl`, `RemoteKernelHandle` — is **hand-written** in
`samples/Kernels/GameServer/Examples.GameServer.Plugin/Client/`. Only the registration accumulators are
generated. That is a lot of boilerplate per game, and it duplicates the wire contract: the control method
`RemoteMonsterControl.KillAsync` hand-forwards to `IGamePluginControlService.KillMonsterAsync`, the world
read `RemoteEntityControl.GetHealthAsync` to `GetEntityHealthAsync`, and so on.

Worse, the **same world** is described three times with three vocabularies: the IPC wire contract
(`IGamePluginControlService`, async), the sandbox host surface (`IGameWorldAccess`, sync, `[HostBinding]`),
and the plugin control surface (`RemoteMonsterControl` etc.). The capability of a call is stated twice — on
the abstraction's `[HostBinding]` and again in the server's `GameWorldHost.AddBindings`.

We want the dev to **declare interfaces and get the rest generated**, and we want **one** description of the
world that the server implements, the plugin proxies, and kernels consume.

## 2. The core idea

**One interface, three consumers.** The game declares a single async domain surface `IGameWorldAccess`. The
**server implements it** for real. The **plugin gets an RPC proxy** generated for it (exactly like any other
`[DotBoxDService]`) — that proxy *is* the plugin's `GamePluginServer` facade. A **kernel gets it injected**;
because the kernel runs on the server its calls are local (a completed `ValueTask`, no real IPC hop), but the
dev writes them exactly like the remote calls.

Because the server implements the same method names the plugin calls, there is **no separate wire contract
and no `[WireCall]` mapping**. Because routing is the method's identity, there are **no per-method routing
annotations**. Because the server owns the policy, the **capability lives on the server implementation**, not
on the contract.

## 3. Layering — who declares what, who owns what

| Layer | Lives in | Authored by | Contents |
|---|---|---|---|
| **Framework contracts** | `DotBoxD.Abstractions` (markers) + `DotBoxD.Plugins.Client` (runtime) | framework | `IPluginServer<TWorld>`, `IServiceControl`, `IExtensibleControl`, `ILiveSettingsHandle<>`, `RemoteServerInvocation<,,>`, `[GeneratePluginServer]` |
| **Game domain surface** | game `*.Server.Abstractions` | game-SDK owner | `IGameWorldAccess : IServiceControl`, `IMonsterControl`/`IEntityControl : IExtensibleControl`, `MonsterSnapshot` |
| **Generated plugin facade** | plugin assembly (generated) | source generator | `GamePluginServer : IGameWorldAccess, IPluginServer<IGameWorldAccess>` + `GamePluginServerBuilder` + the control RPC proxies + the registration accumulators |
| **Reusable runtime library** | `DotBoxD.Plugins.Client` (hand-written once) | framework | lifecycle, `Replace`/`Extend`/`Get` bodies, the anonymous-`InvokeAsync` plumbing, the started-gate |
| **Server implementation** | game server | server author | `GameWorldAccess : IGameWorldAccess` over the live world; `[HostCapability]` per method |

The boundary that matters: **framework-generic behavior is a tested library** (not regenerated per build);
only the **domain forwarders + facade glue + accumulators** are generated. This is what makes the design
cheap and keeps the generator small.

### Framework contracts (the generic half)

```csharp
// lifecycle + the anonymous server-side invokes + hold; generic over the world type so the framework
// never names a game type. The generated facade implements this alongside IGameWorldAccess.
public interface IPluginServer<TWorld> where TWorld : class
{
    ValueTask StartAsync(CancellationToken ct = default);
    ValueTask RunAsync(CancellationToken ct = default);
    ValueTask<TReturn> InvokeAsync<TReturn>(Func<TWorld, ValueTask<TReturn>> lambda);
    ValueTask<TReturn> InvokeAsync<TCaptures, TReturn>(
        TCaptures captures, RemoteServerInvocation<TWorld, TCaptures, TReturn> lambda) where TCaptures : class;
    ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
}

public interface IExtensibleControl                       // every control + the root
{ ValueTask<string> Extend<TService, TKernel>() where TService : class where TKernel : class; }

public interface IServiceControl : IExtensibleControl     // the root adds service-level verbs
{
    ValueTask<string> Replace<TService, TKernel>() where TService : class where TKernel : class, TService;
    ILiveSettingsHandle<TKernel> Get<TKernel>() where TKernel : class, new();
}

[AttributeUsage(AttributeTargets.Class)] public sealed class GeneratePluginServerAttribute : Attribute;
```

### Game domain surface (the only thing the SDK owner writes)

```csharp
[DotBoxDService]                                          // -> plugin RPC proxy generated "like normally"
public interface IGameWorldAccess : IServiceControl
{ IMonsterControl Monsters { get; } IEntityControl Entities { get; } }

public interface IMonsterControl : IExtensibleControl
{
    ValueTask<MonsterSnapshot> GetAsync(string entityId);     // pure signatures — no [HostBinding],
    ValueTask<bool> KillAsync(string entityId);               // no [WireCall], no routing id
    ValueTask<bool> IsMonsterAsync(string entityId);
    ValueTask<int> GetThreatAsync(string entityId);
}
public interface IEntityControl : IExtensibleControl
{ ValueTask<int> GetHealthAsync(string id); ValueTask<int> GetLevelAsync(string id); ValueTask<int> GetPositionAsync(string id); }
```

## 4. The unified surface — why async, and the three call sites

The surface is **async** (`ValueTask<T>`). That is forced by the unification: the remote plugin path is a
real async IPC hop, so the contract must be async; the server and kernel paths return already-completed
`ValueTask`s. One contract, two runtime realities.

```csharp
// SERVER implements it for real:
public ValueTask<bool> KillAsync(string id) => ValueTask.FromResult(_world.KillMonster(id));
// PLUGIN calls the generated proxy of the same interface:
var killed = await server.Monsters.KillAsync("monster-4");
// KERNEL gets it injected; local on the server, but reads like the remote call:
killed = await _world.Monsters.KillAsync(id);
```

## 5. Member → route binding (killing `[WireCall]`)

Earlier drafts bound a control method to a differently-named wire method with
`[WireCall(nameof(IGamePluginControlService.KillMonsterAsync))]`. That mapping only existed because the wire
contract and the control surface were **different interfaces with different names**. Once the server
**implements the control interface directly**, the names are identical by construction and the route is the
**method's own identity**. `[WireCall]` is deleted; the separate domain methods on
`IGamePluginControlService` are deleted (it keeps only the control-plane: install IR / settings / hold).

## 6. Capabilities — off the contract, onto the server impl

The contract carries **no** `[HostBinding]`. The three things `[HostBinding]` used to conflate are split by
who owns them:

- **routing id** → derived from the method identity (no annotation);
- **effect** (read/write/alloc) → inferred from the server implementation (it knows `KillMonster` mutates);
- **capability** → declared on the **server impl**, the trusted side that owns policy:

```csharp
[HostCapability("game.world.monster.write.kill")]   // effect inferred from the impl
public ValueTask<bool> KillAsync(string entityId) => ValueTask.FromResult(_world.KillMonster(entityId));
```

> **Decided — annotate the impl, do not infer everything.** The routine reads *could* be inferred
> (`monster.read.{method}`), but `GetThreatAsync` is gated under `game.world.combat.threat` — a different
> subtree from the `monster.read.*` grants (it is the capability the guardian is deliberately denied). A
> naming convention would force it into `monster.read.threat` and silently break that boundary. Once
> exceptions need a home, one uniform `[HostCapability]` on the impl beats convention-plus-overrides.

This **removes the duplication**: the capability used to appear on the abstraction's `[HostBinding]` *and*
again in `GameWorldHost.AddBindings`. Now it appears once, on the impl; the sandbox host bindings are derived
from `GameWorldAccess` + its annotations (routing from the method, effect from the body, capability from the
annotation). The capability **grants** side (`ServerPolicy`) is unchanged — grants are distinct from the
bindings that require them.

## 7. The generated facade + builder

The dev writes a shell; the generator fills it in and emits a builder:

```csharp
[GeneratePluginServer]
public partial class GamePluginServer : IGameWorldAccess   // generator adds : IPluginServer<IGameWorldAccess>
{
    partial void OnConfigured() => Console.WriteLine("[plugin] custom wiring ran.");   // optional hook
}
```

Lifecycle honors the locked decisions: `Build()` is synchronous and does no I/O; `StartAsync()` connects,
ships verified IR, registers, then runs `OnConfigured()`; `RunAsync() = StartAsync + HoldUntilShutdownAsync`;
the typed surface throws `"Call StartAsync() before using the server."` until started. `Program.cs` reads:

```csharp
using var server = GamePluginServerBuilder.FromPipeName(pipeName).Build();   // sync, no I/O
await server.StartAsync();
await server.Replace<IMonsterAggroService, GuardianKernel>();                // root service verb
await server.Monsters.Extend<IMonsterKillerService, MonsterKillerKernel>();  // per-control verb
var killed = await server.Monsters.KillAsync("monster-4");                   // domain RPC
var hp = await server.InvokeAsync(async w => (await w.Monsters.GetAsync("monster-2")).Health);
await server.HoldUntilShutdownAsync();
```

## 8. What the generator must actually do (implementation notes)

These are grounded in the current analyzer; they are the work to make the design real.

1. **RPC proxy for `IGameWorldAccess`** — the existing `[DotBoxDService]` proxy path, extended to a nested
   control graph (the root returns `IMonsterControl`/`IEntityControl`, themselves proxied).
2. **`InvokeAsync` interceptor must track a user-named facade.** Today the receiver-type guard
   (`InvokeAsyncModelFactory.IsServerInvocationSurface`) string-compares against the constant
   `DotBoxDGenerationNames.Metadata.ServerInvocationSurfaceType` (the FQN of `RemotePluginServer`), and the
   interceptor's `this` parameter (`InvokeAsyncInterceptorEmitter`, the `this {ReceiverType} server` line)
   uses the same constant. To support `GamePluginServer`: replace the constant guard with an **attribute
   check** (`[GeneratePluginServer]`) and feed the discovered FQN as the interceptor `ReceiverType`. The
   interceptor body calls `server.Services.EnsureAnonymousKernelAsync(...)` /
   `server.Services.WireClient.InvokeServerExtensionAsync(...)`; with the install plumbing moving to the
   `DotBoxD.Plugins.Client` library, those two members must be reachable across assemblies — make them
   `public` on the library type (note: `InternalsVisibleTo` cannot target the *generated* interceptor, which
   compiles into the consuming plugin assembly, so `public` is the robust choice).
3. **`[ServerExtensionClient]` graft accepts an interface receiver.** Verified:
   `RpcKernelClientExtensionModelFactory.ReceiverType` accepts any `INamedTypeSymbol`, and both
   `ReceiverHasMember` and `ReceiverHasServerExtensionRegistry` walk `AllInterfaces`. So
   `[ServerExtensionClient(typeof(IMonsterControl))]` validates when `IMonsterControl : IExtensibleControl`
   exposes the registry. (Caveat: emitting a C# 14 extension member onto an *interface* receiver needs a
   real compile test against the pinned Roslyn — it replaces a concrete-class path that has coverage.)
4. **Registration accumulators** — reuse the existing `Analysis/Registration/*` emitter; the root accumulator
   must be emitted by the surface generator (a generated control is invisible to a second
   `ForAttributeWithMetadataName` pass).
5. **Bindings derived from the impl** — generate the sandbox host bindings from `GameWorldAccess` and its
   `[HostCapability]` annotations instead of the hand-written `AddBindings` registry.

## 9. Server side

The server now provides **two** services per connection: the control-plane (`IGamePluginControlService` —
install IR, settings, hold, world snapshot) and the domain surface (`IGameWorldAccess`):

```csharp
peer.ProvideGamePluginControlService(service);
peer.ProvideGameWorldAccess(new GameWorldAccess(world));   // generated from [DotBoxDService]
```

`GameWorldAccess` wraps the live `GameWorld`; its `Monsters`/`Entities` controls forward to the world and
carry `[HostCapability]`. `GameWorldHost` no longer hand-types binding ids/capabilities — it derives them
from `GameWorldAccess`.

## 10. Open questions / unresolved tensions

- **[A — highest] Async surface vs sync event-kernel gates.** The unified surface is async, but
  `IEventKernel<TEvent>.ShouldHandle`/`Handle` are sync (`bool`/`void`). A guardian gate that reads the
  world (`GuardianKernel.ShouldHandle` → `ctx.Host<IGameWorldAccess>()...GetHealthAsync`) cannot `await`.
  In lowered IR the `await` is erased to a sync host-binding call (capability model unchanged), but the C#
  author shape needs a decision: **async event hooks**, or a **sync sandbox view** of the world for
  `ShouldHandle`/`Handle`. Marked at the call site in the sample.
- **[B] Install verbs do not fit the server.** `IExtensibleControl`/`IServiceControl` put
  `Extend`/`Replace`/`Get` on every control, but the server cannot implement them (they ship plugin IR with
  generic kernel types) — every server-side control throws via `ServerControlBase`. The throwers are the
  signal that the install verbs should live on the **plugin facade only**, leaving `IGameWorldAccess` a pure
  domain contract the server implements with zero throwers. Recommended cleanup.
- **[C] `InvokeAsync` placement.** Kept on the framework `IPluginServer<TWorld>` (and concrete on the
  facade) rather than on `IGameWorldAccess` — the interceptor needs a concrete receiver and the locked
  decision puts `InvokeAsync` top-level. The user's original sketch had it on the game interface; this is the
  reconciliation.
- **[D] Effect inference.** Effects are inferred from the impl in this design; if a binding's effect is not
  derivable from its body, a second optional `[HostCapability(effects: …)]` argument may be needed.
- **[E] Wire generalization.** `GameWorldAccess`/the library are hard-typed to `IGamePluginControlService`
  for the control-plane. Parameterizing over the wire interface is deferred (YAGNI until a second game).

## 11. Vibe-check artifacts (in the sample; do not compile)

- **`Examples.GameServer.Server.Abstractions`** — `Control/PluginAuthoringFramework.cs` (framework markers),
  `IGameWorldAccess.cs` (pure unified surface), `Ipc/IGamePluginControlService.cs` (trimmed to control-plane).
- **`Examples.GameServer.Plugin`** — `GamePluginServer.cs` (the shell), `Program.cs` (the dev's wiring),
  `Kernels/MonsterKillerKernel.cs` (async, injected unified surface), `Kernels/GuardianKernel.cs` (carries
  the tension-A note); `Client/*` deleted.
- **`Examples.GameServer.Server`** — `Ipc/GameWorldAccess.cs` (`IGameWorldAccess` over the world +
  `[HostCapability]` + `ServerControlBase`), `Ipc/GamePluginControlService.cs` (domain methods removed),
  `Simulation/GameWorldHost.cs` (bindings derived from the impl), `Program.cs` (registers both services).

## 12. Delta from the original sketch

- `IPluginServer` (game) → renamed to `IGameWorldAccess`; a new **framework** `IPluginServer<TWorld>` holds
  lifecycle + invokes + hold.
- The world-access and remote-control surfaces are **unified** into one `IGameWorldAccess` the server
  implements and the plugin proxies (kills the separate wire contract and `[WireCall]`).
- `[HostBinding]` **removed** from the abstraction; capability annotated on the server impl
  (`[HostCapability]`), routing derived, effects inferred.
- `IServiceControl` is filled in (`Replace`/`Get`) rather than left empty; all controls are
  `IExtensibleControl`.

## 13. Provenance

Converged over a 2026-06-16 session: a multi-lens design workflow (interface-driven generation; library-vs-
generated split) produced the first synthesis; iterative owner steering then (a) renamed and split the
framework vs game server interfaces, (b) unified `IGameWorldAccess` with the plugin-server surface so the
server simply implements it and the plugin RPC-proxies it, killing `[WireCall]`, and (c) moved capabilities
off the abstraction onto the server implementation. Validated by rewriting the three sample projects to the
target shape.
