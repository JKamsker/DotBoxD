# Diagnostics reference

DotBoxd's compile-time generators/analyzers and runtime emit namespaced diagnostic codes. Each family
has a reserved prefix so codes never collide as the product grows:

| Prefix | Area | Source |
|--------|------|--------|
| `DBXS` | **Services** — `[DotBoxdService]` proxy/dispatcher generation | `DotBoxd.Services.SourceGenerator` |
| `DBXK` | **Kernels / plugins** — plugin authoring + validation | `DotBoxd.Plugins.Analyzer` + kernel validators |
| `DBXP` | **Pushdown** | reserved |
| `DBXH` | **Hosting** | reserved |
| `DBXT` | **Transports** | reserved |
| `DBXG` | **Generators / codegen (shared)** | reserved |

## Authoritative lists

The shipped/unshipped code lists are maintained alongside each generator and are CI-enforced:

- Services (`DBXS###`): `src/CodeGeneration/DotBoxd.Services.SourceGenerator/AnalyzerReleases.Shipped.md`
  and `AnalyzerReleases.Unshipped.md`.
- Kernels/plugins (`DBXK###`): `src/CodeGeneration/DotBoxd.Plugins.Analyzer/AnalyzerReleases.Shipped.md`
  (and the kernel runtime diagnostic-code source).

> Migration note: these were renamed during the merge — ShaRPC's `SHARPC###` → `DBXS###` and Safe-IR's
> `SGP###` → `DBXK###`. If you previously suppressed any old IDs, update your `.editorconfig` /
> `<NoWarn>`. See [migration-from-standalone-repos.md](../contributing/migration-from-standalone-repos.md).
