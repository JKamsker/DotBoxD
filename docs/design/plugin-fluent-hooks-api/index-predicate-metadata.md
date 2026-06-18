# Index-predicate metadata for lowered subscriptions

> Implements [issue #47](https://github.com/JKamsker/DotBoxD/issues/47):
> *Expose host-readable index metadata for lowered subscription predicates.*

## Why

A host often has event-specific dispatch tables or indexes. Without predicate metadata the
only safe implementation is **broad subscription + run the lowered predicate for every event** —
correct, but expensive for high-volume event families.

DotBoxD already owns predicate lowering (`.Where(...).Select(...).Run(...)` → verified IR). This
feature makes DotBoxD *also* publish a structured, stable description of the index-eligible
constraints it found, so a host can compile that into whatever equality/range dispatch structure
is natural for its runtime — without inventing an expression-tree parser or leaking host-specific
filter DTOs into plugin code.

## What ships on the manifest

`HookSubscriptionManifest` carries two additive, back-compatible members
(`src/Hosting/DotBoxD.Plugins/PluginManifest.cs`):

```csharp
public sealed record HookSubscriptionManifest(string Event, string Kernel)
{
    public IReadOnlyList<IndexedPredicate> IndexedPredicates { get; init; } = [];
    public bool IndexCoversPredicate { get; init; }
}

public sealed record IndexedPredicate(
    string Path, IndexPredicateOperator Operator, object? Value, string ValueType);

public enum IndexPredicateOperator
{
    Equals, NotEquals, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual,
}
```

For the chain

```csharp
server.Subscriptions.On<AttackEvent>()
    .Where(e => e.AttackerId == "player-1" && e.Damage >= 5)
    .Select(e => e.TargetId)
    .Run((targetId, ctx) => ctx.Messages.Send(targetId, "watched-hit"));
```

the generated manifest subscription serializes to:

```json
{
  "event": "DotBoxD.Kernels.Game.Server.Abstractions.Events.AttackEvent",
  "kernel": "HookChain_…",
  "indexedPredicates": [
    { "path": "AttackerId", "operator": "Equals", "value": "player-1", "valueType": "string" },
    { "path": "Damage", "operator": "GreaterThanOrEqual", "value": 5, "valueType": "int" }
  ],
  "indexCoversPredicate": true
}
```

## Extraction rules (v1)

Extraction happens in `HookChainIndexPredicateExtractor`
(`src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/HookChains/`) from the `.Where(...)` stages,
reusing the same member/constant detection the IR lowering already performs. A leaf is index-eligible
when it is `event-property <op> compile-time-constant`:

- Operators: `==`, `!=`, `>`, `>=`, `<`, `<=`.
- Operands are **normalized** so the event property is the left operand
  (`5 >= e.Damage` ⇒ `Damage <= 5`).
- The constant must be resolvable with `GetConstantValue()` (literals and `const`); runtime-captured
  locals are not index values.
- Only leaves reachable through **top-level `&&`** are emitted, so every `IndexedPredicate` is a
  *necessary* AND condition of the real predicate. This is what makes host rejection on any single
  predicate always sound.

`IndexCoversPredicate` is `true` only when the whole predicate reduces to that conjunction — no `||`,
no `!`, no `.Where()` after a `.Select()`, no non-constant or non-property leaf, no unsupported type.
Anything else conservatively forces partial coverage (`false`) and the un-indexable parts remain in
the verified IR.

## How a host uses it (correctness fallback)

1. **Register**: read `IndexedPredicates`, keep the ones whose `Path` it actually indexes, build
   equality/range buckets.
2. **Prefilter**: on publish, evaluate the cheap index checks. Any failure ⇒ skip dispatch entirely
   (no event materialization, no sandbox entry).
3. **Fallback**: if the index check passes, run the verified IR predicate **unless**
   `IndexCoversPredicate` is `true`. The verified IR stays the source of truth; the index is only an
   optimization.

## Sample demonstration (`samples/GameServer`)

- `AttackEvent` marks `AttackerId`, `TargetId`, `Damage` with `[EventIndexKey]` — the host's
  declaration of which fields it indexes (`Examples.GameServer.Server.Abstractions`).
- `EventIndexMatcher<TEvent>` compiles the manifest metadata into a cheap, reflection-free-per-call
  prefilter, honoring only `[EventIndexKey]` paths.
- The plugin installs an indexed subscription in `Program.ConfigureRuntimeHooks`.
- On install, the server logs what it indexed:
  ```text
  [server] registered indexed subscription: AttackEvent AttackerId == player-1
  [server] registered indexed prefilter: AttackEvent Damage >= 5
  ```
- `EventIndexFanoutTests` publishes 100 attacks where only 3 share the indexed bucket and proves the
  lowered predicate ran exactly 3 times — the other 97 never entered the sandbox.

## Non-goals / follow-ups

- The running sample only *logs* what it indexed; wiring the matcher into live `GameWorld` dispatch
  is tracked as a follow-up.
- Promoting `EventIndexMatcher` into a first-class framework index registry (so every host doesn't
  reimplement it), kernel-class `ShouldHandle` extraction, and nested property paths are follow-ups.
