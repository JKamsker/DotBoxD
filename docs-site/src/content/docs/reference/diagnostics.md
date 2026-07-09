---
title: 'Diagnostics reference'
description: 'DotBoxD''s compile-time generators/analyzers and runtime emit namespaced diagnostic codes. Each family has a reserved prefix so codes never collide as the…'
---
DotBoxD's compile-time generators/analyzers and runtime emit namespaced diagnostic codes. Each family
has a reserved prefix so codes never collide as the product grows.

These diagnostics exist because the analyzer and kernel validators fail **closed**: an unsupported
construct is rejected at build time (or at plugin import time) instead of being silently miscompiled or
lowered into something that misbehaves at runtime. So a `DBXS`/`DBXK` code means "this construct isn't
supported here" — it's telling you to express the intent a different way, not a bug in the generator to
work around.

| Prefix | Area | Source |
|--------|------|--------|
| `DBXS` | **Services** — `[RpcService]` proxy/dispatcher generation | `DotBoxD.Services.SourceGenerator` |
| `DBXK` | **Kernels / plugins** — plugin authoring + validation | `DotBoxD.Plugins.Analyzer` + kernel validators |
| `DBXP` | **Pushdown** | reserved |
| `DBXH` | **Hosting** | reserved |
| `DBXT` | **Transports** | reserved |
| `DBXG` | **Generators / codegen (shared)** | reserved |

## Services codes (`DBXS###`)

If you hit one of these while generating a `[RpcService]` proxy, look it up here:

| ID | Severity | Meaning |
|--------|----------|---------|
| `DBXS001` | Error | DotBoxD source generator failure |
| `DBXS002` | Error | Unsupported method shape (e.g. a `ref`/`in`/`out` parameter) |
| `DBXS003` | Error | Unsupported service shape (e.g. a generic or nested interface) |
| `DBXS004` | Warning | Async sibling interface method name collides with another method |

### DBXS001

Cause: an unexpected generator stage failed. Fix the first underlying compiler error and rebuild; if
the diagnostic remains, capture the full message and file a bug. There is no safe suppression because
the generated surface may be incomplete. Fallback: implement `IServiceDispatcher` and `IRpcInvoker`
against the public primitives while isolating the reproducer.

### DBXS002

Cause: a method uses an unsupported wire shape such as `ref`, `in`, `out`, pointers, or an unsupported
DTO. Replace mutation-by-reference with an explicit request/response DTO. Suppress only when the method
is intentionally excluded from the RPC contract; otherwise generation would omit behavior.

### DBXS003

Cause: the service interface itself is unsupported, commonly because it is nested or open generic.
Move it to a top-level non-generic interface or close type parameters in DTOs. Do not suppress on a
service expected to be registered: no proxy/dispatcher is emitted.

### DBXS004

Cause: projecting a synchronous method to its generated `Async` sibling collides with another member.
Rename one wire method or author the asynchronous member explicitly. Suppression is acceptable only
when the colliding convenience sibling is unused and the retained generated surface is verified.

See [First service](/tutorials/first-service/) for a corrected top-level contract example.

## Generator fail-closed codes (`DBXK###`)

### DBXK111, DBXK113, and DBXK114

These errors mean a recognized `RunLocal`, result-hook `Register`/`RegisterLocal`, or `Run` chain could
not be lowered without changing C# semantics. Replace the unsupported predicate/projection/body with a
supported scalar/DTO shape, or delete the fluent attribute/sugar and install an equivalent hand-written
`PluginPackage` through public APIs. Suppression is unsafe in production: the native terminal is only a
test/debug fallback and throws when reached.

### DBXK115

Two generated server-extension grafts have the same receiver/signature. Rename or move one graft. This
cannot be safely suppressed because extension lookup would be ambiguous.

### DBXK116

A `[NativeOnly]` helper reached server-side IR. Move the call outside the lowered expression or expose a
capability-gated public host binding. Suppression is unsafe because the server cannot reproduce native
client semantics.

### DBXK117

An unexpected plugin-generator stage failed. Fix earlier diagnostics first; otherwise report the stage,
exception type, and minimal source. Hand-written public primitives remain the supported fallback.

## Authoritative lists

The shipped/unshipped code lists are maintained alongside each generator and are CI-enforced. These are
the source of truth — including the full `DBXK###` set, which is not reproduced here:

- Services (`DBXS###`): [`AnalyzerReleases.Shipped.md`](https://github.com/JKamsker/DotBoxD/blob/main/src/CodeGeneration/DotBoxD.Services.SourceGenerator/AnalyzerReleases.Shipped.md)
  and its [`AnalyzerReleases.Unshipped.md`](https://github.com/JKamsker/DotBoxD/blob/main/src/CodeGeneration/DotBoxD.Services.SourceGenerator/AnalyzerReleases.Unshipped.md) sibling.
- Kernels/plugins (`DBXK###`): [`AnalyzerReleases.Shipped.md`](https://github.com/JKamsker/DotBoxD/blob/main/src/CodeGeneration/DotBoxD.Plugins.Analyzer/AnalyzerReleases.Shipped.md)
  and its [`AnalyzerReleases.Unshipped.md`](https://github.com/JKamsker/DotBoxD/blob/main/src/CodeGeneration/DotBoxD.Plugins.Analyzer/AnalyzerReleases.Unshipped.md) sibling
  (plus the kernel runtime diagnostic-code source). The generator codes with user-actionable fixes are
  summarized above; release tables remain the complete machine-maintained inventory.

> Migration note: these were renamed during the merge — ShaRPC's `SHARPC###` → `DBXS###` and Safe-IR's
> `SGP###` → `DBXK###`. If you previously suppressed any old IDs, update your `.editorconfig` /
> `<NoWarn>`. See [migration-from-standalone-repos.md](/contributing/migration-from-standalone-repos/).
