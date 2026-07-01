# Diagnostics reference

DotBoxD's compile-time generators/analyzers and runtime emit namespaced diagnostic codes. Each family
has a reserved prefix so codes never collide as the product grows.

These diagnostics exist because the analyzer and kernel validators fail **closed**: an unsupported
construct is rejected at build time (or at plugin import time) instead of being silently miscompiled or
lowered into something that misbehaves at runtime. So a `DBXS`/`DBXK` code means "this construct isn't
supported here" — it's telling you to express the intent a different way, not a bug in the generator to
work around.

| Prefix | Area | Source |
|--------|------|--------|
| `DBXS` | **Services** — `[DotBoxDService]` proxy/dispatcher generation | `DotBoxD.Services.SourceGenerator` |
| `DBXK` | **Kernels / plugins** — plugin authoring + validation | `DotBoxD.Plugins.Analyzer` + kernel validators |
| `DBXP` | **Pushdown** | reserved |
| `DBXH` | **Hosting** | reserved |
| `DBXT` | **Transports** | reserved |
| `DBXG` | **Generators / codegen (shared)** | reserved |

## Authoritative lists

The shipped/unshipped code lists are maintained alongside each generator and are CI-enforced:

- Services (`DBXS###`): `src/CodeGeneration/DotBoxD.Services.SourceGenerator/AnalyzerReleases.Shipped.md`
  and `AnalyzerReleases.Unshipped.md`.
- Kernels/plugins (`DBXK###`): `src/CodeGeneration/DotBoxD.Plugins.Analyzer/AnalyzerReleases.Shipped.md`
  (and the kernel runtime diagnostic-code source).

> Migration note: these were renamed during the merge — ShaRPC's `SHARPC###` → `DBXS###` and Safe-IR's
> `SGP###` → `DBXK###`. If you previously suppressed any old IDs, update your `.editorconfig` /
> `<NoWarn>`. See [migration-from-standalone-repos.md](../contributing/migration-from-standalone-repos.md).
