# Kernel runtime

The kernel runtime executes validated IR under hard budgets. Key pieces:

## Two execution backends

- **Interpreter** (`DotBoxd.Kernels.Interpreter`) — executes verified IR directly. Predictable
  semantics, easy quotas, great diagnostics, AOT-friendly, no codegen. This is the default and the
  safety baseline.
- **Compiler** (`DotBoxd.Kernels.Compiler`) — emits verified IL for hot kernels and caches the artifact
  (content-addressed by module hash + entrypoint + policy hash + compiler version). The emitted assembly
  is checked by `DotBoxd.Kernels.Verifier` before it runs, so the compiled path enforces the same
  restrictions as the interpreter.

## Metering & policy

Every run is bounded by a `SandboxPolicy`:

- **fuel** (instruction budget), **loop iteration** and **call-depth** budgets,
- **list/collection cardinality** and **output-byte** budgets,
- **capability grants** (e.g. `file.read`, `net.http.get`) with parameters, expiry, and per-capability
  quotas,
- **effect** controls (Pure / Read / Write / ExternalCall / Time / Random / NonDeterministic), with a
  deterministic mode (logical clock + seeded random) available.

## Effects & capabilities

Bindings (`DotBoxd.Kernels.Runtime`, `DotBoxd.Hosting.Http`) are the only way a kernel reaches outside
pure computation, and only when the policy grants the matching capability. This is what makes
author-supplied logic safe to run in-process. See
[security/sandbox-caveats.md](../security/sandbox-caveats.md) and the full specification under
[`docs/Specs/`](../Specs/).
