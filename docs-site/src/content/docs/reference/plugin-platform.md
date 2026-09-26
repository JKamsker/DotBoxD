---
title: Package trust, inspection and replay
description: Operate plugins using optional public primitives for provenance, permissions and reproducible failures.
---

## One default path: Contracts, Host, Plugin

For a new .NET 10 application, reference the existing `DotBoxD` facade package and start with the
[getting-started walkthrough](/getting-started/). Think in three projects:

| Project | Owns | First API to find |
| --- | --- | --- |
| Contracts | Shared RPC interfaces, event DTOs and host binding contracts | `RpcServiceAttribute` and public contract types |
| Host | Implementations, grants, transport identity and plugin lifetime | `RpcHost`, `SandboxHost.Create`, the generated plugin-server facade |
| Plugin | Calls, subscriptions and sandboxed extension logic | Generated proxies and plugin package factories |

Use named pipes for a same-user sidecar. For network peers, follow [transport security](/security/transport/).
The existing `DotBoxD.Services.All` facade covers service-only/netstandard clients. Advanced hosts
can reference individual Services, Hosting, Kernels, Plugins and Pushdown packages. No new all-or-nothing
facade is required; generated wiring and every feature below remain optional over public primitives.

## Verifiable plugin artifacts

`PluginArtifactSigning.CreateUnsigned(package, version, publisher)` wraps an existing `PluginPackage`.
`Sign` adds an RSA-PSS/SHA-256 signature using a host-owned RSA key (minimum 2048 bits). The key ID is
the SHA-256 hash of SubjectPublicKeyInfo. The canonical signing bytes authenticate the schema,
plugin ID, publisher, version, content hash, key ID and **exact package JSON**, including the manifest's
required capabilities. Formatting changes invalidate a signature; this is intentional.

`PluginTrustPolicy` takes approved `(publisher, public key)` pairs. It never trusts a key merely
because the artifact supplied its ID. `Verify` checks hash, schema and signature before parsing the
package, and checks that the signed plugin ID agrees with the manifest. Unsigned artifacts require
`allowUnsigned: true`; invalid or unapproved signatures never fall back to unsigned acceptance.
The artifact's claimed publisher is not authenticated when `IsSigned` is false.

`GetSigningBytes` and `GetKeyId` are public so an HSM/external signer can construct the same artifact.
Tests exercise that hand-written path. `Serialize`/`Deserialize` carry the envelope as JSON. Existing
raw plugin-package JSON remains available without this optional envelope.

Trust does not replace IR validation or capability grants. Verify the artifact, review its requested
permissions, then use the existing installation path. Never load an arbitrary plugin CLR assembly
inside the host to extract its identity. The plugin exports its generated package as data.

For upgrades, `PluginCapabilityChanges.Compare(previous.Manifest, next.Manifest)` requires the same
plugin ID and returns sorted `Added`, `Removed` and `Unchanged` capability IDs. Use
`RequiresAdditionalPermission` to require explicit policy review. Parameter-constrained grants
(network destinations, filesystem roots, storage quotas) remain host policy; capability-ID diffs
alone do not authorize broader grant parameters. The host also owns rollback/version policy and
key rotation. Keep old approved keys only for the migration window you intend to permit.

## Permission and execution explanations

`PluginInspection.Explain(module, bindings, policy)` runs the public `ModuleValidator`, reports
capabilities with active grants or denial reasons, reached binding signatures and their fuel models,
resource limits, operation source positions, and exported lowered IR. Invalid lowering/validation
paths retain their stable diagnostic codes. Source positions refer to the IR's available source map;
original C# source is not reconstructed from an assembly.

Compose common policies using `SandboxPolicyBuilder`'s existing granular grants and limits. The
explanation uses the same policy capability matcher, including wildcard and expiry behavior. An
active capability grant still has parameter constraints; validation diagnostics explain incompatible
effects or malformed grants. Never use the explanation result as an authorization token.

`ExplainExecution(plan, entrypoint, options, runCount, compilerConfigured)` explains the built-in
Auto selector: dynamic-code availability, debug tracing, asynchronous bindings and warm-up threshold.
Compiled eligibility is a preflight, not a guarantee: compiler/verifier diagnostics remain authoritative.
A custom mode selector owns its own decision. This inspection surface is independent of a debugger
and can be consumed by future debugging tools.

## Capture and replay

`ExecutionRecording.CaptureAsync(module, policy, bindings, entrypoint, input, mode)` runs a real
in-process kernel with recording wrappers around the supplied public binding descriptors. It returns
an `ExecutionTrace` containing the module/policy/binding identities, input, grants and limits, boundary
call order/arguments/results/errors, initial cancellation, requested/actual backend and runtime,
language, compiler, verifier and type-system metadata. Save its `Serialize()` result as `.dbxtrace`.

The recorder is an explicit diagnostic execution path. It routes binding calls through the public
runtime dispatcher for both backends so compiled intrinsics cannot bypass recording. It does not
instrument the normal hot path, and does not intercept worker-process execution. Bindings that
perform host side effects still perform those effects during capture: record only an execution you
intend to run.

`ExecutionReplay.RunAsync(trace, mode)` reconstructs and validates the module and policy, then creates
bindings that consume recorded values. It never calls the original host delegates. Missing/extra
calls, changed call order/arguments and differing terminal values/error codes are divergence, not a
matching replay. A compiled request disables fallback, so compiler unavailability cannot masquerade
as a successful compiled replay. Running both backends turns an execution into a differential fixture.

Traces are sensitive: input, module literals, grants, binding values and error messages can contain
secrets. `trace.Redact(transform)` lets the host transform the **whole** document before export,
including policy parameters and embedded module JSON. It marks the result redacted, and exact replay
rejects it. After deliberate substitutions, create a reviewed replacement trace with coherent hashes
and expected outputs; do not describe it as the original execution. Redaction is application policy,
not an automatic promise to recognize every secret.

Replay models recorded boundary values and cancellation at recorded boundaries. It cannot reproduce
arbitrary host side effects, custom context mutations, OS scheduling, a cancellation arriving between
pure IR instructions, or an exact elapsed wall-clock deadline. Such recordings can report divergence;
there is no silent live-call fallback. Resource policy remains enforced, and replay rejects traces
exceeding local fuel, allocation, call-depth, file-size and wall-time safety ceilings. Runtime metadata
is informational so controlled cross-version replay remains possible; module/policy/binding hashes
must still match exactly.

## Local commands

Build the solution, then run the local tool:

```sh
dotnet run --project tools/DotBoxD.Cli -c Release -- explain package.json
dotnet run --project tools/DotBoxD.Cli -c Release -- explain failure.dbxtrace --json
dotnet run --project tools/DotBoxD.Cli -c Release -- replay failure.dbxtrace --backend interpreter
dotnet run --project tools/DotBoxD.Cli -c Release -- replay failure.dbxtrace --backend compiled
```

The built executable is named `dotboxd`. It accepts exported JSON/trace data, never plugin DLLs.
Raw module inspection uses the built-in pure bindings and default policy; a trace supplies the host's
binding signatures and policy for richer inspection. Human output is the default; `--json` provides
a versioned envelope. Exit 0 means success/matching replay, 1 validation failure/divergence, and 2
usage or input failure. No commands prompt, read stdin, modify input files or access the network.
