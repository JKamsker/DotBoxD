# Followups: `[KernelMethod]` inlining and kernel RPC services

Two follow-on features layered on the lowering pipeline and the kernel runtime. Both keep the SafeIR
invariant: plugin authors write plain C#, the generator lowers it to **verified, sandboxed IR**, and the
server never compiles plugin source.

---

## 1. `[KernelMethod]` — call reusable kernel methods from hooks

### Goal

Let a plugin author factor shared gate/handler logic out of a `Where`/`Select`/`InvokeKernel` lambda (or
a kernel-class `ShouldHandle`/`Handle`) into a named static helper, without leaving the sandbox:

```csharp
server.Hooks.On<MonsterAggroEvent>()
    .Where((e, ctx) => IsBullyingLowLevelPlayer(e.MonsterLevel, e.PlayerLevel, e.Distance, 3, 5, 5))
    .InvokeKernel((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));

[KernelMethod]
public static bool IsBullyingLowLevelPlayer(
    int monsterLevel, int playerLevel, int distance, int levelGap, int aggroRange, int protectMaxLevel)
    => monsterLevel - playerLevel >= levelGap && distance <= aggroRange && playerLevel <= protectMaxLevel;
```

### How it works (inlining, not calling)

There is **no call** in the lowered IR — the method body is **inlined** at each call site. The generator:

1. Resolves the invoked symbol and checks for `[KernelMethod]`
   ([SafeIrKernelMethodInliner](../../../src/SafeIR.PluginAnalyzer/Analysis/Lowering/Expressions/SafeIrKernelMethodInliner.cs)).
   A method without the attribute returns `null` so the normal dispatch continues; once the attribute is
   seen the inliner *owns* the call and any unsupported shape throws `NotSupportedException` (fail-safe).
2. Lowers each call-site argument in the **calling** context (so a `[HostBinding]` call or
   `[Capability]`-gated read passed as an argument still contributes its capability to the kernel's
   manifest).
3. Lowers the method's body with each parameter name bound to its already-lowered argument IR — the same
   compile-time substitution the `Select` projection uses (`ProjectedElement`), generalized to N
   parameters via `SafeIrExpressionLoweringContext.InlinedBindings` and resolved first in
   `LowerIdentifier` (so a parameter correctly shadows a same-named live setting).
4. Switches to the method body's own semantic model (the body may live in another file/tree).

The result is identical to writing the body inline, so it composes with everything the lambda lowering
already supports (AND-composed `Where`s, `Select` projection, the terminal `Send`).

### Where it plugs in

`SafeIrInvocationExpressionLowerer.Lower` tries, in order: `[HostBinding]` → `[KernelMethod]` →
`string.Equals`/`Substring` → throw. So the inliner sits next to the existing host-binding lowerer and
benefits from the same `catch (NotSupportedException)` fail-safe in `HookChainModelFactory` /
`PluginKernelModelFactory` (no package emitted; the runtime terminal throws `SGP062`).

### Constraints (verified at generation time; violation fails safe)

- The method must be **`static`**.
- Its body must be an **expression body** or a **single `return` statement** (no locals/loops — those are
  what the kernel-RPC statement lowerer in §2 adds for the batch-entrypoint shape).
- Parameters and return must be **supported scalars** (`bool`, `int`, `long`, `double`, `string`) — the
  same scalar set the rest of the lowering pipeline uses. A host-service interface cannot be passed as a
  parameter; call the `[HostBinding]` directly and pass its scalar result.
- **No recursion** — a `[KernelMethod]` inlining itself (directly or transitively) is rejected via an
  inline-stack guard.

### Incrementality note

Like the existing `[HostBinding]` lowering, the inliner reaches across syntax trees through the semantic
model. A full `dotnet build` always regenerates correctly; in rare IDE incremental-refresh cases editing
only a `[KernelMethod]` body in a *different* file than the call site may not re-lower until the next
build. This matches the host-binding precedent.

### Example

`GuardianKernel.ShouldHandle` ([example](../../../examples/GameServer/SafeIR.Game.Plugin/Kernels/GuardianKernel.cs))
factors its "is this monster bullying a weaker player in range?" rule into
`IsBullyingLowLevelPlayer([KernelMethod])`, passing the live settings as arguments. The example runs
end-to-end unchanged (exit 0; damage/tick drops from baseline exactly as before).

### Tests

[`PluginAnalyzerKernelMethodTests`](../../../tests/SafeIR.Tests/PluginAnalyzer/Generated/PluginAnalyzerKernelMethodTests.cs):
inlining into a kernel-class `ShouldHandle` (runtime gate equivalence), into an inline `Where` chain,
multi-argument helpers, capability collection through a `[HostBinding]` argument (manifest + install
under a wildcard grant), and the multi-statement-body fail-safe (no chain package generated).

---

## 2. Kernel RPC services

See the second half of this document (added with the feature).
