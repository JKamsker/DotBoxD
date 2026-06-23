# Plugin context & hooks: open fixes and design direction

Companion to [server-walkthrough.md](server-walkthrough.md),
[plugin-walkthrough.md](plugin-walkthrough.md),
[../remote-plugin-server-builder/interface-driven-plugin-server.md](../remote-plugin-server-builder/interface-driven-plugin-server.md),
and [kernel-binding-model.md](kernel-binding-model.md).

**Status:** Design direction + review backlog for PR #88 (`codex/improve-hooks-issue-87`, issue #87),
revised after a multi-lens review (see [How this doc was reviewed](#how-this-doc-was-reviewed)).
**Date:** 2026-06-23. **Observed PR head:** `41ec9172`.

Design guide this doc is measured against: **Simple · Obvious · Discoverable · Consistent · Minimal ·
Composable** — plus **Explicit · Stable · Testable** as working corollaries.

---

## TL;DR

There are **two independent problems** here; the doc keeps them apart on purpose.

1. **Correctness (blocks merge).** PR #88 has **three** runtime/codegen bugs and **red CI** that have
   nothing to do with the context's *shape*: a factory-collapse, a result-hook priority regression, a
   `RunLocal`-helper composition bug, and a Windows smoke failure. These gate merge on their own.

2. **Shape (a separate, larger thesis).** The plugin context is the only facade surface the author
   **hand-extends with an undeclared member set** — so "what's generated vs hand-written" is invisible.
   Making that surface *declarable* is worth doing, but it is a design with real open decisions (ownership,
   lifetime, feasibility limits), not a mechanical "do what the world surface does." It should land as its
   own PR(s), after those decisions are answered.

Direction in one line: **fix the correctness bugs first; make the context surface declarable carefully;
fix chain/context identity by ownership; and single-source the host-capability rule without removing the
host's independent install-time verification.**

---

## Part 1 — Where we already are (the good news)

The "server declares how it wants to be talked to; the client writes one line; codegen fills the rest"
model **already exists and is genuinely contract-driven** for the RPC-forwarding surface:

```csharp
[GeneratePluginServer]
public partial class GamePluginServer : IGameWorldAccess;
```

From the `[DotBoxDService]` interface graph the generator **enumerates members** and emits the RPC proxy
forwarders, the `IPluginServer<IGameWorldAccess>` lifecycle, the `Setup` accumulator, live-settings `Get`,
`IGameWorldServer`, and `GamePluginServerBuilder`:

| Generated artifact | Driven by | Where |
|---|---|---|
| World resolution | the interface carrying `[DotBoxDService]` (not the class name) | [PluginServerFacadeModelFactory.cs:71](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerFacadeModelFactory.cs) `ResolveWorldType` |
| Controls / forwarders / scoped clients | walking the interface's members + nested `[DotBoxDService]` returns | [PluginServerFacadeModelFactory.cs](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerFacadeModelFactory.cs) `ResolveControls` (:89), `ResolveMethods` (:125), `ResolveReturnWrapper` (:196) |
| `I{World}Server`, lifecycle, builder | the world **type symbol** | [PluginServerFacadeEmitter.cs:20](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerFacadeEmitter.cs), [PluginServerFacadeNameFormatter.cs:30](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerFacadeNameFormatter.cs) |
| **Context + hook/sub registries** | **the class-name string** (`FacadeRootName(name) + "Context"`) | [PluginServerContextSurfaceEmitter.cs:16](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerContextSurfaceEmitter.cs), [PluginServerFacadeNameFormatter.cs:21](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerFacadeNameFormatter.cs) |

**The honest framing.** The context is **not** the only string-derived generated *name* — `HookRegistryName`,
`SubscriptionRegistryName`, and `SetupInterfaceName` are all class-name-derived too
([PluginServerFacadeNameFormatter.cs:13-28](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerFacadeNameFormatter.cs)),
and `ResolveControlService` hardcodes the literal `.Ipc.IGamePluginControlService`
([PluginServerFacadeModelFactory.cs:87](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerFacadeModelFactory.cs))
inside the path the table calls contract-driven. The difference that matters: the registries and `Setup`
are string-*named* but their member surfaces are **fully generated fixed shells the author never edits**.
The context is the **only generated type whose member surface the author extends by hand, with no declared
contract** ([GamePluginContext.cs:3](../../../samples/GameServer/Examples.GameServer.Plugin/GamePluginContext.cs)).
That is the real gap — invisible generated-vs-handwritten, not "the one string name."

---

## Part 2 — Is the "RPC pipeline" one thing or two?

**Structurally it is two dispatch concepts that already share the lowering front-end and converge for
server extensions — plus one deliberately separate native terminal.** Treat the seam between them as a
*trust boundary*, not just duplication.

1. **Host call inside lowered IR** — a `Where`/`Select`/`Run` (or a server-extension body) that touches a
   host member is lowered to a sandbox `CallExpression(bindingId, args)`. For explicit `[HostBinding]`
   members, `bindingId` is the attribute value; for auto host-service bindings, it is derived as
   `host.{ns}.{Type}.{Method}`
   ([DotBoxDHostBindingExpressionLowerer.cs:89](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxDHostBindingExpressionLowerer.cs)).
   At **exec** time the interpreter resolves the id **per call** against the host-curated `BindingRegistry`
   and runs the host's descriptor under its effects + `RequiredCapability` + grant check
   ([ExpressionEvaluator.Calls.cs:149](../../../src/Kernels/DotBoxD.Kernels.Interpreter/Internal/Expressions/ExpressionEvaluator.Calls.cs),
   [BindingRegistry.cs:44](../../../src/Kernels/DotBoxD.Kernels/Bindings/BindingRegistry.cs)). An unknown id
   throws; the plugin's IR id is only a *lookup key into a host table*.

2. **Server-extension / `InvokeAsync` request-response** — a whole verified function dispatched **by
   `pluginId`** through `InvokeServerExtensionAsync`, whose internal host calls were effect/capability-checked
   at install. Two codegen factories feed this one runtime concept — `InvokeAsyncModelFactory` synthesizes
   the anonymous `"rpcEntrypoint":"Invoke"`
   ([InvokeAsyncModelFactory.cs:122](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/InvokeAsync/InvokeAsyncModelFactory.cs))
   and `RpcKernelModelFactory` emits named `[ServerExtension]` entrypoints — both via `DotBoxDRpcJsonLowerer`,
   both terminating at `InvokeServerExtensionAsync`. In-process (`ServerExtensionProxy`) and over-IPC (a
   generated client proxy,
   [RpcKernelClientProxyEmitter.cs](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Rpc/Client/RpcKernelClientProxyEmitter.cs))
   differ only in transport.

3. **`RunLocal` / `RegisterLocal` IPC terminal** — push-only, keyed by `subscriptionId`, running a **native
   delegate in the plugin process**; it shares the value marshaller but never reaches the binding registry
   ([RemoteLocalHandlerRegistry.cs](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/RemoteLocalHandlerRegistry.cs)).
   This is the **trust-boundary exit** — the one place a plugin runs unverified native code — and must stay
   a distinct, non-substitutable path.

**The seam (and why it is not "just duplication").** The auto-binding id and the effect/allocation
classification are re-derived in **both** the analyzer and the runtime — `HostBindingRoute` uses
`type.MetadataName` in the analyzer
([DotBoxDHostBindingExpressionLowerer.cs:192](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxDHostBindingExpressionLowerer.cs))
vs `type.Name` in the runtime
([HostServiceBindingFactory.cs:239](../../../src/Hosting/DotBoxD.Plugins/Runtime/Bindings/HostServiceBindingFactory.cs)),
and there is a literal "Must match HostServiceBindingFactory.ReturnAllocates" comment
([:112](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxDHostBindingExpressionLowerer.cs)).

> **This is a claim-vs-ground-truth cross-check, not redundancy.** The analyzer output ships **inside the
> plugin package** (the manifest's self-declared effects/capabilities). The runtime is the host's own
> truth: it re-derives entrypoint effects from its **own** registry at install (`FunctionAnalyzer`) and
> raises `DBXK041` (effects) / `DBXK044` (capabilities) when the plugin's *claim* ≠ the host's
> *recomputation*. `DBXK041` firing on drift is the **security control working**, not a bug to engineer
> away. The system premise — *host frozen at release; plugins ship later; verify what ships* — depends on
> the host recomputing rather than trusting the manifest.

**Unification target (corrected).** Collapse the **two hand-written copies of the derivation rule** into
one **server-owned definition** that both sides read; keep the host's independent install-time
recomputation and the `DBXK041`/`DBXK044` comparison. The win is that the check becomes *un-driftable*, not
that it disappears. **Non-goal:** never merge the two *authorization layers* — a binding-id call must
always resolve through the host-curated registry, never through plugin-supplied descriptor metadata. (See
§3.4 for why this is "one definition, two projections," not "one shared object.")

---

## Part 3 — The design direction

### 3.1 Make the context surface *declarable* — with open decisions

The goal is to make the context's surface visible and checkable rather than grafted onto a magic partial.
But this is **not** simply "enumerate an interface like the world surface does," for two reasons:

- **Different job.** The world surface *enumerates + forwards* (signatures → RPC forwarders). The context
  *wraps an ambient `HookContext` + carries members that lower into IR*. They are not the same kind of
  artifact, so the world's `ResolveControls`/`ResolveMethods` resolvers do **not** transfer — they read
  signatures for forwarding and have no notion of lowering.
- **Feasibility limit (load-bearing).** `[KernelMethod]` lowering **inlines the method body**
  ([DotBoxDKernelMethodInliner.cs](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxDKernelMethodInliner.cs)
  requires a source body, throwing "must be declared in source" otherwise). An **interface** member has no
  body, so a pure-interface context **silently breaks every `[KernelMethod]`**. A `[HostBinding]` member
  *can* live on a declared contract (it lowers from the attribute, no body needed). ⇒ if a single declared
  type is wanted it must be an **abstract class with concrete bodies**, not an interface; otherwise split
  the surface (contract carries `[HostBinding]`; `[KernelMethod]` helpers stay concrete).

**Open decisions the doc must answer before this is actionable** (none are decided yet):

| Decision | Options | Notes |
|---|---|---|
| **Ownership** | server-owned vs plugin-owned contract | Plugin-owned preserves today's autonomy (author adds ad-hoc helpers with no SDK round-trip). Server-owned is required for any **capability-bearing** member (see security note). Likely answer: **split** — plugin owns native/`[KernelMethod]`; server owns `[HostBinding]`. |
| **Attachment** | `[GeneratePluginServer(Context = typeof(IGameContext))]` opt-in vs convention default | Keep the convention-named partial as the **zero-config default** so existing plugins/the sample keep compiling. |
| **Lifetime & construction** | per-event (today) vs cached; who calls the factory | Today the context is built per publish from `HookContext`; a declared contract needs a stated construction story. |
| **Versioning** | how a contract evolves without breaking shipped plugins | The host is frozen at release; contract changes are a compat event. |
| **Native-only members** | stay on the partial vs move to the contract | Pure native helpers (e.g. sample `FormatCalmTarget`) are `RunLocal`-only and need no contract. |

**Migration (must be stated, not implied).** Keep `partial class {Root}Context` as the default; make the
declared contract an **opt-in overlay** enumerated *in addition to* the partial. Plain native members stay
on the partial; `[HostBinding]` members may move to the contract; `[KernelMethod]` helpers stay concrete.
Ship a regression test that the convention default still binds when no contract is declared, and migrate
the GameServer sample both ways before calling this "the library shape."

**Honest cost.** This keeps the call-site one-liner but **adds an authoring-side declaration** (net author
burden goes up: declare shape + implement it). A lighter alternative that buys most of the "visible +
checkable" benefit with **no new declaration**: an **analyzer diagnostic** on the existing partial that
flags a member used in a lowered stage that the analyzer cannot lower. Weigh the two before committing.

**Security (capability surface).** Only host-registered bindings grant anything. A `[HostBinding]` on a
plugin-authored context with **no matching host binding fails closed** (unknown-binding at validation /
`DBXK041` at install) — it does not self-grant. The design must keep capability-bearing members
**server-owned**; a plugin must not be able to *assert* a host capability the server never registered.

### 3.2 Make execution location *explicit*

A context member runs in one of three places. The current signal — an attribute (or its absence) on a
free-form partial — is too implicit; **"no marker ⇒ native" is a footgun.**

| Tier | Marker | Runs | Body may reference | Author rule of thumb |
|---|---|---|---|---|
| Pure helper, inlined | `[KernelMethod]` | server-side sandbox | scalars, other `[KernelMethod]`/`[HostBinding]` | "pure math on event fields" |
| Host capability | `[HostBinding(id, capability, effects)]` | server-side host | declared host call, gated by capability | "reads/writes host/game state" |
| Native | **(explicit local marker — see below)** | plugin process, post-IPC | arbitrary in-process code | "touches your plugin's own services" |

Two corrections to the previous draft:

- **The verbs are not interchangeable.** `Run`/`Register` are lowered (sandbox subset only);
  `RunLocal`/`RegisterLocal` run arbitrary native code. A `RunLocal` body that calls a plugin service does
  **not** become a valid `Run` by dropping the suffix — it fails to lower. Do **not** claim "the same
  expression, the suffix chooses where it runs."
- **Make native opt-in, not default.** Prefer an **explicit local-only marker** (or split the context type
  into a *lowerable facet* and a *native facet*) so a member's execution site is never inferred from the
  *absence* of an attribute. The native terminal is the trust-boundary exit; selecting it should be a
  deliberate, visible choice, and the generator must route tiers by **owned symbol identity** (§3.3), never
  by string name (§ P2.5/P2.6 below), so a typo or a foreign API cannot send a body to the wrong tier.

**Failure mode (state it).** A non-lowerable member used in a lowered stage compiles, then throws `DBXK062`
**synchronously at chain construction** (first run), not at a distant install step. Build-time author
diagnostics largely exist (`DBXK111`/`DBXK113`) but are `Info` severity and easy to miss — **raise them to
`Warning`.**

### 3.3 Fix identity — two *distinct* seams (not one)

Both today resolve by name/scan instead of by ownership, but they are different seams and need different
fixes:

- **P2.5 — which context type to inject.** `InferredGeneratedContextTypeFullName` scans **every** syntax
  tree and `return null`s on a second `[GeneratePluginServer]`
  ([GeneratedRemoteHookChainFallback.GeneratedContexts.cs:34](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/HookChains/Support/GeneratedRemoteHookChainFallback.GeneratedContexts.cs)).
  *Fix:* resolve from the owning server. The generated `On<TEvent>()` already returns
  `RemoteHookPipeline<TEvent, {Context}>`, so the context is recoverable as the receiver's return-type
  argument — no scan, no `[GeneratePluginServer]` symbol needed at the call site.
- **P2.4 — is this even a DotBoxD chain.** `CandidateKind`'s fast path matches a receiver member **named**
  `Hooks`/`Subscriptions` with no ownership check, and the "semantic" fallback `RegistryKind` is itself
  only a `Name.EndsWith("HookRegistry")` suffix match
  ([GeneratedRemoteHookChainFallback.cs:23](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/HookChains/Support/GeneratedRemoteHookChainFallback.cs)).
  A foreign fluent API with those names is mis-claimed. *Fix:* gate both paths on the receiver's **type
  owned by a `[GeneratePluginServer]` class**, not on member/type-name strings.

### 3.4 Single-source the host-capability rule — without weakening the check

`§3.4` is "**one server-owned definition, two projections**," **not** "one descriptor object consumed by
both." A shared runtime object is not even buildable: the analyzer is `netstandard2.0` with zero project
references (packed to `analyzers/dotnet/cs`) and the runtime is `net10.0`; the analyzer works on
`IMethodSymbol`, the runtime on `MethodInfo`. So:

- Put the binding-id formula and the effect/allocation/classification rules in **one dependency-free
  source file**, linked into both projects (`<Compile Include Link>`), and have both sides call it.
- The descriptor the rule produces must carry **more than id + effects**: **version, capability, async
  flag, parameter/return shapes, cost, audit, and safety** — these also drift and affect stability today.
- Replace the **method-name-prefix heuristic** (`IsWriteMethod`: names starting `Kill/Set/Update/…` ⇒
  `HostStateWrite`, duplicated verbatim in both assemblies) with an **explicit effect declaration**, so
  effects stop being inferred from method names on two sides (a method named `Patch`/`Spawn` is silently
  read-only today).
- **Keep** the host's independent install-time recomputation and `DBXK041`/`DBXK044` (Part 2). Both sides
  reading one server-owned definition makes the check *un-driftable*; it does not remove it.

---

## Part 4 — Open fixes (review backlog)

Each is verified against PR head `41ec9172`.

### P1 — blockers (correctness; independent of the §3 redesign)

1. **Factory collapse.** On a cache hit `On<TEvent, TContext>` returns the existing pipeline and **discards
   the new `createContext`**. *Fix:* do **not** key on raw delegate identity — keep one
   `(event, context)` pipeline and **fail fast on a conflicting factory**. (The generated convention path
   binds a static `FromHookContext` method group and is immune; this bites **hand-written** explicit
   factories.)
   [HookRegistry.cs:86](../../../src/Hosting/DotBoxD.Plugins/Runtime/HookRegistry.cs),
   [SubscriptionRegistry.cs:73](../../../src/Hosting/DotBoxD.Plugins/Runtime/Subscriptions/SubscriptionRegistry.cs).

2. **Result-hook priority is no longer global** once an event has multiple context pipelines.
   `FireManyAsync` returns the first non-null result in dictionary order; priority is sorted only *within*
   a slot. **The naive fix does not work:** `order` is **per-`ResultHookSlot`, not global**, so "merge-sort
   by `(priority, order)`" cannot order across pipelines. *Fix:* introduce a **registry-level sequence** or
   an **event-level result table**. (On `main`, `_pipelines` was keyed by event type alone, so this path
   did not exist — the PR introduces the regression.)
   [HookRegistry.Pipelines.cs:59](../../../src/Hosting/DotBoxD.Plugins/Runtime/HookRegistry.Pipelines.cs),
   [ResultHookSlot.cs:235](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/ResultHookSlot.cs).

3. **Reusable `RunLocal`/`RegisterLocal` helpers don't compose.** Chain identity is the **call-site source
   location** ("each chain emits a distinct package"), and that id is reused as the **subscription id**,
   whose registry is **idempotent — "re-registering the same subscriptionId replaces the previous
   handler."** So a chain factored into a helper and invoked twice silently **drops the first handler**.
   *Fix:* split **package identity** (source-location-stable, for generator incrementality) from
   **registration/subscription identity** (unique per call/instance).
   [HookChainIdentity.cs:14](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/HookChains/HookChainIdentity.cs),
   [RemoteLocalHandlerRegistry.cs:41](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/RemoteLocalHandlerRegistry.cs).

4. **CI is red — diagnose to a code path, don't rerun.** The Windows `Build` job's GameServer **smoke run**
   throws `RemoteServiceException: Internal error` from `BlinkBehindAsync`; the `Build & Test` summary jobs
   fail downstream off it. **Leading hypothesis:** this PR rewrote the analyzer side of the `DBXK041` seam
   (`DotBoxDManifestEffectModel`; [DotBoxDHandleModelFactory.cs:68](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/DotBoxDHandleModelFactory.cs)
   now always adds `HostStateWrite/Concurrency/Audit`) while the runtime side was untouched —
   `RemoteServiceException` is a generic wrapper, so a `DBXK041` install-reject and a dispatch throw look
   identical. **Capture the inner server-side diagnostic before attributing to environment/timing.** Closed
   only when the inner error is tied to a code path. Pin observations to head `41ec9172`.

### P2 — design hazards (fix before baselining)

5. **Generated-remote fallback is too broad** — recognition by member/type **name** with no ownership
   check (two seams; see §3.3). Breaks multi-server and third-party fluent APIs.
   [GeneratedRemoteHookChainFallback.cs:23](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/HookChains/Support/GeneratedRemoteHookChainFallback.cs),
   [GeneratedRemoteHookChainFallback.GeneratedContexts.cs:34](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/HookChains/Support/GeneratedRemoteHookChainFallback.GeneratedContexts.cs).

6. **Host-binding rule duplicated across the analyzer↔runtime trust seam** — single-source the rule, keep
   the check (§3.4). Scope is broader than id/effects (also the `IsWriteMethod` heuristic, and the
   descriptor should carry version/capability/async/shapes/cost/audit/safety). Also: the contract story is
   **overstated** — analyzer and runtime can read capability metadata from different places, and the
   GameServer abstractions use `[HostCapability]` *without* `[HostBinding]`; **decide whether the interface
   or the implementation owns binding metadata, then enforce it.**
   [DotBoxDHostBindingExpressionLowerer.cs:163](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxDHostBindingExpressionLowerer.cs),
   [HostServiceBindingExtensions.cs:50](../../../src/Hosting/DotBoxD.Plugins/Runtime/Bindings/HostServiceBindingExtensions.cs).

7. **Public API expansion is too large to baseline as "polish."** The typed-context surface, the
   `new`-shadowing `<TEvent>` shims, and the `UseGenerated*` / `UseProjecting*` plumbing should be
   **shrunk or hidden** (`[EditorBrowsable(EditorBrowsableState.Never)]` + generator-only docs) **before**
   the api-baseline is updated, not after. (api-baseline is already +268/−60; a redesign churns it again,
   and the check is set-based — commit deliberately.)
   [RemoteHookPipeline.cs:36](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/RemoteHookPipeline.cs),
   [HookPipeline.Default.cs](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/HookPipeline.Default.cs).

8. **Typed hook/subscription overload drift is a latent codegen build break, not polish.**
   `RemoteSubscriptionPipeline.Typed` is missing the two element-only `UseGeneratedLocalChain` overloads
   `RemoteHookPipeline.Typed` has (parity regression vs `main`), and `RemoteSubscriptionStage.Typed` is
   missing the same stage-level shape. Either restore parity (collapse onto a `kind`-parameterized base)
   **or** prove the generator never emits the element-only subscription local-chain shape (so the gap is
   unreachable) — and say which.

9. **Analyzer/generator disagreement on `Run(lambda)` is stale and misleading.** The generator now lowers
   fluent `Run` chains, but the analyzer still reports `DBXK110` saying `Run(lambda)` is "not yet lowered"
   and "will throw at runtime"; the unshipped release note still says `InvokeKernel(lambda)`. Unsupported
   chains should be diagnosed by the generator path that actually failed to lower, not by a blanket stale
   analyzer warning.
   [PluginAnalyzer.cs:33](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginAnalyzer.cs),
   [AnalyzerReleases.Unshipped.md:9](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/AnalyzerReleases.Unshipped.md).

### P3 — polish

10. **Stale doc-term sweep** (broaden beyond `server-walkthrough.md`): also `plugin-walkthrough.md` and
    `kernel-binding-model.md`; terms `server.Events` → `server.Subscriptions`, `server.Kernels.Register`,
    `InvokeKernel`, and other old naming. [server-walkthrough.md:263](server-walkthrough.md), [:269](server-walkthrough.md).
11. **Dead code** — `IPluginEventPipelineRegistry` (sole declaration repo-wide).
    [ServerContextFactory.cs:17](../../../src/Hosting/DotBoxD.Plugins/Runtime/ServerContextFactory.cs). Delete.
12. **Sample teaches noise** — `(e, _) =>` for filters that ignore `ctx`; use `e =>` unless the body
    touches `ctx`. Promote the rule ("arity names intent") next to the §3.2 tier table.
    [Program.cs:91](../../../samples/GameServer/Examples.GameServer.Plugin/Program.cs).
13. **`RegisterLocal` has three authoring shapes** (the legacy cancellation `(e, ctx, ct) =>
    ValueTask<TResult>`) vs the "exactly two" rule. Drop it (`ctx.CancellationToken` exists) or document it
    as the deliberate async exception.

---

## Part 5 — Sequencing

**Step 1 — unblock merge (split the surgical from the open-ended).**
- **1a.** Land the localized correctness fixes: P1.1 (factory), P1.2 (priority — with the registry-level
  sequence, *not* the naive merge-sort), P1.3 (helper composition). Each is a single-subsystem change.
- **1b.** **Diagnose P1.4 (CI) to a code path before estimating it.** If the root cause is the
  analyzer↔runtime effect drift (P2.6), the minimal unblock is a targeted effect-token fix, with §3.4 as
  the durable fix — but do not let CI force the redesign early.

**Step 2 — de-risk identity.** §3.3: resolve context by ownership (closes P2.5) and add the receiver-type
ownership check (closes P2.4). Convention-named context stays; only *identity resolution* changes.

**Step 3 — the larger moves, as their own PRs.** §3.1 (declarable context surface — **after** the §3.1 open
decisions are answered) and §3.4 (single-sourced host-capability rule). Not bolted onto #88.

> **Half-state risk (decide consciously).** Merging #88 + Step 2 but never landing Step 3 leaves a
> convention-named context with ownership-based identity — better than today, but the "undeclared surface"
> gap persists indefinitely. Either accept that intermediate state and add a visible guardrail (an analyzer
> info/warning on convention-named contexts so the debt is tracked), or gate Step 2 on an RFC-approved
> Step 3. Do not leave it implicit.

**Step 4 — polish + surface shrink** (P2.7 + P3), ideally with the duplication collapse so the
hook/subscription axis can't drift again.

---

## Part 6 — Tests to add before accepting the direction

These are acceptance gates, not afterthoughts (the PR already has `ResultHookSlotTests` /
`TypedServerContextTests` as homes):

- **Cross-context result priority** — a priority-`0` handler in an earlier context pipeline must lose to a
  priority-`100` handler in a later one (proves P1.2).
- **Conflicting context factories** — two `On<E, Ctx>(factoryA/ factoryB)` calls (hooks **and**
  subscriptions) either throw or keep distinct factories (proves P1.1).
- **Reusable-helper composition** — the same `RunLocal` helper invoked twice keeps **both** handlers
  (proves P1.3).
- **Foreign `.Hooks.On` negative analyzer fixture** — a non-DotBoxD fluent API named `Hooks`/`Subscriptions`
  is **not** intercepted (proves P2.4).
- **Multi-server context ownership** — two `[GeneratePluginServer]` types in one compilation each resolve
  their own context (proves P2.5).
- **Host-binding descriptor parity** — analyzer-derived vs runtime-derived id/effects agree (guards P2.6 /
  §3.4).
- **Typed hook/subscription overload parity** — the `Typed` pipelines expose the same `UseGeneratedLocalChain`
  set, including the stage-level typed subscription shape (guards P2.8).
- **Deterministic `BlinkBehindAsync` path** — a local, non-flaky test exercising the real server-extension
  dispatch the smoke run covers (guards P1.4).
- **Analyzer stale-warning guard** — `Run(lambda)` sites that are lowered by the generator must not also emit
  `DBXK110`; genuinely unsupported chains should produce the specific generator diagnostic (guards P2.9).
- **Generated context discovery docs** — generated context/registry XML docs name the default context type,
  distinguish generated members from authored members, and document the explicit-context escape hatch.
- **Plugin-fluent docs smoke** — include these design docs in docs smoke or add a targeted stale-term /
  compiled-snippet check so `server.Events`, `server.Kernels.Register`, and old `Invoke*` names cannot drift
  back in unnoticed.

---

## How this doc was reviewed

The findings here were cross-checked through independent review lenses — *Simple, Obvious, Discoverable,
Consistent, Minimal, Composable, Explicit, Stable, Testable, Boring* — plus two correctness audits against
PR head `41ec9172`. Where a reviewer's proposed fix was itself wrong (e.g. the P1.2 "merge-sort by
`(priority, order)`" that ignores per-slot `order`, or the §3.4 "remove the `DBXK041` drift class" that
would delete a trust-boundary check), the corrected version is what appears above.
