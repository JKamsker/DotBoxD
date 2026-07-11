# Code Enforcer Quality Gates

## Coverage

`eng/scripts/check-coverage.ps1` reads `coverage.json` and enforces coverage over shipping
`DotBoxD.*` assemblies whose source files live under `src/`.

The gate checks:

- global line coverage
- global branch coverage
- per-area line and branch coverage
- critical-component line and branch coverage

The CI coverage job writes `artifacts/coverage/coverage-summary.md` and appends the same table to
the GitHub step summary. A failure prints the failing scope, observed coverage, required floor, and
the summary path.

Current floors are conservative ratchets based on the local baseline collected on 2026-07-05
after the issue #500 mutation-test improvements:

| Scope | Line baseline | Branch baseline |
| --- | ---: | ---: |
| Global shipping assemblies | 85.22% | 77.85% |
| Kernels | 87.17% | 79.00% |
| Services | 85.98% | 84.70% |
| Channels and transports | 91.51% | 85.49% |
| Code generation | 85.68% | 75.01% |
| Hosting and pushdown | 81.83% | 78.33% |
| Critical verifier | 92.42% | 85.91% |
| Critical runtime | 79.02% | 67.14% |
| Critical validation | 91.19% | 89.16% |
| Critical services | 85.98% | 84.70% |

The next coverage ratchets are critical runtime and code-generation branch coverage. They remain
below 85% because the measured branch baselines are 67.14% and 75.01%; raise those floors only
after targeted tests increase the measured support. Do not lower a floor without explaining the
coverage loss in the PR.

## Banned APIs

`eng/scripts/check-banned-apis.ps1` reads `banned-apis.json` and scans tracked C# source for
layer-aware API bans. Each rule defines:

- `forbiddenIn`: glob patterns where the API is banned
- `symbols`: regex patterns for the banned API family
- `allowedIn`: explicit exception paths, each with a non-empty `reason`
- `reason` and `remediation`: text printed when the guard fails

Current policy covers direct console I/O, process spawning, direct networking outside transport
owners, nondeterministic time/random primitives in kernel code, and raw filesystem access in kernel
validation outside the policy-owned validator. Existing intentional boundary owners are allowlisted
with reasons in `banned-apis.json`.

## C# File Shape

`eng/scripts/check-csharp-file-lines.ps1` enforces line-count, folder-size, and mechanical split
budgets for non-generated C# files.

The `maxPartFileCount` budget forbids tracked or new files named `*.Part*.cs`. When a test or
implementation grows past the size guard, split by behavior or extract named support types instead
of creating `Class.PartN.cs` continuations.

The `maxSourceMultiFilePartialTypeCount` budget ratchets non-generated `src/` partial types that
span multiple files. Source-generation contracts and meaningful domain slices can remain partial,
but adding a new multi-file source partial now requires either extracting collaborators or
intentionally raising the budget with a justification.

The current production partial-type audit is documented in
[`docs/architecture/partial-type-audit.md`](../../docs/architecture/partial-type-audit.md).
