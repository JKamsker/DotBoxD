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

Current floors are conservative ratchets based on the local baseline collected on 2026-07-05:

| Scope | Line baseline | Branch baseline |
| --- | ---: | ---: |
| Global shipping assemblies | 85.15% | 77.80% |
| Kernels | 87.02% | 78.88% |
| Services | 85.64% | 84.22% |
| Channels and transports | 91.71% | 85.49% |
| Code generation | 85.77% | 75.08% |
| Hosting and pushdown | 81.77% | 78.33% |
| Critical verifier | 92.42% | 85.91% |
| Critical runtime | 79.02% | 67.14% |
| Critical validation | 89.09% | 87.70% |
| Critical services | 85.64% | 84.22% |

Raise floors toward current values when tests improve. Do not lower a floor without explaining the
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
