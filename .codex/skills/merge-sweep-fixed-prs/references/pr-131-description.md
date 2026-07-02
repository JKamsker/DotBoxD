# JKamsker/DotBoxD#131 PR Description Reference

Source: https://github.com/JKamsker/DotBoxD/pull/131

This body is the reference for the desired summary depth: a short lead-in, grouped change areas, and concrete validation notes.

```markdown
## Summary

Continuation of the adversarial "surprise" sweep -- a large wave of fail-closed hardening and lowering / metadata-fidelity fixes spanning the source generators, hosting, services, and kernel layers (~97 focused commits, 240 files). Each commit pairs a narrow regression (usually a red test) with the minimal fix, so a previously silent miscompile, fail-open capability, or dropped contract detail now either lowers correctly or fails closed with an explicit DotBoxD diagnostic.

### Generated metadata & default-value fidelity
- Emit typed `default(T)` for non-nullable value-type defaults instead of boxing to `null`, covering `Guid` / `DateTime` / `decimal` / enum / primitive defaults.
- Preserve optional service parameters, default-parameter-value attributes, and nullable flow / return attributes through generated service proxies, RPC clients, and plugin server facades.

### Query / queryable lowering hardening
- Reject `Contains` over collections whose comparer semantics an ordinal `In` filter cannot reproduce -- non-ordinal `SortedSet<string>`, culture-sensitive, custom, and hidden key comparers.
- Reject custom `Contains` methods and unknown query text escapes; support static `string.Equals` queries; keep integer query satisfiability exact; bind indexed predicate schema values to their type.

### Host service binding validation
- Reject concrete (non-interface) host service contracts and handles, colliding / overloaded bindings, and ambiguous binding routes before traversal, surfacing explicit diagnostics instead of late reflection (`GetInterfaceMap`) failures.

### RPC shape & payload validation
- Reject payload bytes on no-payload shapes, raw stream-handle and delegate payloads, generic RPC kernel methods, and split-constructor / inherited-immutable DTO gaps.
- Require JSON entrypoint aliases; preserve readonly collection shapes, params contracts, and return flow attributes; reject nullable event reference properties and RPC event capability self-assertions.

### DTO projection / RunLocal
- Reject unreconstructible, omitted, reordered, and non-constructible DTO / projection members; validate constructor parameter types and RunLocal projection return shapes; preserve record projection identity.

### Plugin server facade
- Reject field / object-member collisions and non-public members, serialize generated startup, wrap service returns, close disposed surfaces, and reject server-extension client properties without services.

### Hook chains & local terminal
- Bypass local stages for generated chain interceptors; require a local terminal / projected type for projected hook types; reject host writes in local-terminal filters and custom hook stage methods; validate local result hooks against the event contract; make local-terminal setup replay idempotent; preserve mixed hook result shapes.

### Kernel registry, live settings & module schema
- Align registry `Get` with `TryGet`, expose a typed installed-kernel primitive and a reserved `RequireInstalledKernel` helper, reject comparer-map construction and invalid plugin-id shapes.
- Reject invalid / task-like / revoked typed live-setting views and validate live-setting defaults against ranges.
- Constrain module schema type strings, semver, GUID, and path / URI literals.

### Cancellation & connection lifecycle
- Honor cancellation in single-connection connect, normalize named-pipe accept cancellation, reject concurrent pending client connects, and avoid caching canceled anonymous installs.

### Event values & registration accumulator
- Validate event writer and adapter value types; honor inherited and assignable registration-accumulator children and preserve nullable accumulator constraints.

## Validation

- `dotnet restore DotBoxD.slnx`
- `GITHUB_ACTIONS=true dotnet build DotBoxD.slnx -c Release --no-restore`
- `dotnet format whitespace DotBoxD.slnx --verify-no-changes --no-restore`
- `pwsh ./eng/scripts/check-csharp-file-lines.ps1`
- `pwsh ./eng/scripts/check-csharp-folder-layout.ps1`
- `pwsh ./eng/scripts/check-api-compat-baseline.ps1`
- `pwsh ./eng/scripts/check-spec-manifest.ps1`
- `pwsh ./eng/scripts/check-docs-smoke.ps1 -Configuration Release`
- `dotnet test DotBoxD.slnx -c Release --no-build` passed all projects except AgentQueue when `/tmp/agentq-tests` was root-owned/unwritable; `TMPDIR=/tmp/cx-agentq dotnet test tools/AgentQueue/tests/AgentQueue.Tests/AgentQueue.Tests.csproj -c Release --no-build` passed. A whole-suite rerun with workspace `TMPDIR` caused unrelated Unix named-pipe path-length failures, so it was not used as the final signal.

<!-- This is an auto-generated comment: release notes by coderabbit.ai -->
## Summary by CodeRabbit

* **Bug Fixes**
  * Improved generated factory metadata so `default` parameter values emit `default(<type>)` semantics.
  * Updated `SingleConnectionTransport.ConnectAsync` to throw `OperationCanceledException` immediately for pre-canceled tokens.
  * Strengthened host service binding validation to reject non-interface contracts and overloaded/duplicate binding routes.

* **Tests**
  * Added coverage for generated factory default values across numeric, enum, GUID, and date/time inputs.
  * Added coverage for `SingleConnectionTransport` cancellation with a pre-canceled token.
  * Expanded query-translation hardening for `SortedSet<string>.Contains` (ordinal vs non-ordinal comparers).
  * Added host binding contract tests for concrete contracts and duplicate routes.

* **CI / Automation**
  * Expanded the required-test gate and updated minimum executed-test thresholds to match the new coverage.
<!-- end of auto-generated comment: release notes by coderabbit.ai -->
```
