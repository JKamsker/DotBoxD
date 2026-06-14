# Migration: from ShaRPC + Safe-IR to DotBoxd

DotBoxd is the merger of two formerly standalone repositories into one contract-first runtime:

| Former repo | Became | DotBoxd role |
|-------------|--------|--------------|
| **ShaRPC** (transport-agnostic RPC framework) | `DotBoxd.Services`, `DotBoxd.Transports.*`, `DotBoxd.Codecs.MessagePack`, `DotBoxd.Services.SourceGenerator` | **Services** + Channels |
| **Safe-IR** (restricted-IR kernel sandbox) | `DotBoxd.Kernels.*`, `DotBoxd.Hosting`, `DotBoxd.Abstractions`, `DotBoxd.Plugins`, `DotBoxd.Plugins.Analyzer`, `DotBoxd.Pushdown.Services`, `DotBoxd.Hosting.Http` | **Kernels** + **Pushdown** |

Both projects were MIT-licensed; both copyright notices are preserved in [`LICENSE`](../../LICENSE).
The original root files of ShaRPC (README, solution, license) are archived under
[`docs/legacy/`](../legacy/).

## What changed in the merge

- **Namespaces/packages** were renamed `ShaRPC.*` / `SafeIR.*` → `DotBoxd.*` (see the package table in
  the root [README](../../README.md)).
- **Diagnostic IDs** were renamed: ShaRPC's `SHARPC###` → `DBXS###` (Services); Safe-IR's `SGP###` →
  `DBXK###` (Kernels). If you suppressed any of the old IDs, update your suppressions.
- **Marker attributes**: `[ShaRpcService]`/`[ShaRpcMethod]` → `[DotBoxdService]`/`[DotBoxdMethod]`.
- **JSON schemas** were renamed to `schemas/v1/dotboxd-kernel-module.schema.json` and
  `dotboxd-plugin-package.schema.json`.
- **Build**: one solution (`DotBoxd.slnx`), Central Package Management (`Directory.Packages.props`),
  and the former NuGet dependency Safe-IR took on ShaRPC is now an in-repo `ProjectReference`.

## Viewing the pre-merge git history

Both repositories' **full histories are preserved and reachable** in this repo — they were imported via
a git *subtree merge*, so every original commit (and its author) is an ancestor of the merge commits
`merge: import ShaRPC history ...` and `merge: import Safe-IR history ...`.

Because subtree-merge introduces the files under a new path at the merge boundary, `git log --follow`
from a file's **current** path stops at the import. To see a file's pre-merge history, query by its
**original** path against all history:

```bash
# Pre-merge history of a former ShaRPC core file:
git log --all -- src/ShaRPC.Core/RpcPeer.cs

# Pre-merge history of a former Safe-IR core file:
git log --all -- src/SafeIR.Core/SandboxModule.cs

# Or browse from the import-merge's second parent:
git log <import-merge-sha>^2
```

`git blame` on the current files works up to the merge; for older lines, blame the original path on the
imported history (`git blame <import-merge-sha>^2 -- <original-path>`).

## Building & testing

```bash
dotnet build DotBoxd.slnx -c Release
dotnet test  DotBoxd.slnx -c Release
```

See [`CONTRIBUTING.md`](../../CONTRIBUTING.md) for the full CI gate list (security-boundary suite,
API baselines, file-length, spec manifest, rebrand-completeness, docs smoke).
