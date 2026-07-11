---
title: 'Diagnostics reference'
description: 'Stable, actionable reference for every DBXS and DBXK diagnostic emitted by DotBoxD.'
---

This page is the stable help target for every Services (`DBXS`) and Kernels/plugins (`DBXK`)
diagnostic. Analyzer help links point directly to the anchors below. Runtime hosts can obtain the
same `DBXK` meaning, audience, cause, and remediation from `PluginDiagnosticCodes`.

“Never” in the suppression column means suppressing the diagnostic can leave generated code absent,
accept an invalid package, or weaken a security boundary. Where suppression is allowed, verify the
remaining generated/public-primitive path in a test.

## Compile-time diagnostics

| Code | Cause | Bad example → correction | Alternative or fallback | Suppression policy |
|---|---|---|---|---|
| <a id="dbxs001"></a>`DBXS001` | An unexpected Services generator stage failed. | A service triggers an unhandled generator case → fix earlier compiler errors and reduce/report the reproducer. | Hand-write `IServiceDispatcher` and `IRpcInvoker` using public primitives. | Never for a service that must be callable. |
| <a id="dbxs002"></a>`DBXS002` | An RPC method uses an unsupported parameter, return, or DTO shape. | `void Update(ref int value)` → `UpdateResult Update(UpdateRequest request)`. | Exclude the method from the RPC contract and implement it outside RPC. | Only when the method is intentionally not part of the contract. |
| <a id="dbxs003"></a>`DBXS003` | The service is nested, open generic, or otherwise unsupported. | `[RpcService] interface IStore<T>` → a top-level, closed `IStringStore`. | Hand-write a dispatcher/proxy for the specialized public contract. | Never when the service is registered. |
| <a id="dbxs004"></a>`DBXS004` | A projected `Async` convenience name collides with another member. | `Get()` plus `GetAsync()` with conflicting generated shape → rename one wire method or author the async member explicitly. | Call the non-colliding generated member or a hand-written invoker. | Only when the omitted convenience member is unused and tested. |
| <a id="dbxk001"></a>`DBXK001` | Plugin code calls a forbidden host/runtime API. | `File.ReadAllText(path)` in lowered code → use a declared, policy-granted file capability binding. | Move native work outside the plugin or expose a narrow public host binding. | Never; this is a sandbox boundary. |
| <a id="dbxk020"></a>`DBXK020` | A `[LiveSetting]` member has an unsupported type. | `[LiveSetting] DateTime Start` → use `string`, `bool`, `int`, `long`, or `double` and convert outside IR. | Declare the equivalent live-setting primitive in a hand-written manifest. | Never for generated plugin settings. |
| <a id="dbxk100"></a>`DBXK100` | A kernel expression cannot be represented by the supported IR subset. | Interpolate an arbitrary object → interpolate supported scalar values or build the value outside the kernel. | Delete the sugar and construct a `PluginPackage` with public primitives. | Never; generation did not preserve the requested semantics. |
| <a id="dbxk111"></a>`DBXK111` | A recognized remote `RunLocal` chain could not be lowered. | Use an unsupported predicate/projection → use supported scalar/DTO stages. | Execute locally without the remote chain, or install a hand-written package. | Never; the native terminal otherwise throws. |
| <a id="dbxk112"></a>`DBXK112` | A hook-result type lacks the required control contract. | A mutable class without `Success`/`Reason` → a top-level readonly record struct with `bool Success` and `string? Reason`. | Return a supported result contract through public hook primitives. | Never; result builders cannot be emitted safely. |
| <a id="dbxk113"></a>`DBXK113` | A result hook has the wrong context/result or an unsupported stage. | Register a handler returning a different result type → use the context's `[Hook]` result and supported stages. | Register a hand-written public hook pipeline. | Never; the native terminal otherwise throws. |
| <a id="dbxk114"></a>`DBXK114` | A recognized `Run` chain could not be lowered. | Put unsupported code in `Where`, `Select`, or the terminal → use supported IR expressions. | Bind a kernel class with `Use`/`Register` or install public primitives. | Never; the runtime terminal otherwise reports `DBXK062`. |
| <a id="dbxk115"></a>`DBXK115` | Two generated server-extension grafts have the same lookup signature. | Generate duplicate receiver/name/parameters in one namespace → rename or move one graft. | Call a non-grafted public client explicitly. | Never; extension lookup would be ambiguous. |
| <a id="dbxk116"></a>`DBXK116` | A `[NativeOnly]` member reached server-side IR. | Call a native context helper in a lowered predicate → move it outside the chain. | Expose an explicit capability-gated host binding. | Never; server semantics cannot reproduce the call. |
| <a id="dbxk117"></a>`DBXK117` | An unexpected plugin-generator stage failed. | A syntax item crashes a generation stage → fix earlier errors, minimize, and report it. | Construct the equivalent package using public primitives. | Never for the affected generated item. |

## Runtime package, policy, and lifecycle diagnostics

| Code | Cause | Bad example → correction | Alternative or fallback | Suppression policy |
|---|---|---|---|---|
| <a id="dbxk010"></a>`DBXK010` | The manifest plugin ID is empty. | `PluginId = ""` → choose a stable non-empty ID. | Reject the upload and ask the author to regenerate it. | Never. |
| <a id="dbxk011"></a>`DBXK011` | Manifest and module IDs differ. | Manifest `weather`, module `chat` → make both `weather`. | Regenerate the package from one identity source. | Never. |
| <a id="dbxk012"></a>`DBXK012` | Module metadata is missing or has a different plugin ID. | Omit plugin-id metadata → emit metadata matching the manifest. | Rebuild through the supported package generator. | Never. |
| <a id="dbxk013"></a>`DBXK013` | Kernel metadata is missing or a subscription targets another kernel. | Module `guard`, subscription `chat` → bind both to `guard`. | Split unrelated kernels into separate packages. | Never. |
| <a id="dbxk014"></a>`DBXK014` | The contract is not `IEventKernel<TEvent>` or its event differs from the subscription. | Contract `IEventKernel<A>`, subscription `B` → use the same event type. | Author a separate package per event contract. | Never. |
| <a id="dbxk021"></a>`DBXK021` | A live-setting name is duplicated. | Declare `Threshold` twice → give every setting a unique name. | Combine the values into one supported setting. | Never. |
| <a id="dbxk022"></a>`DBXK022` | A non-numeric setting declares a numeric range. | `string Mode` with min/max → remove the range or use a numeric type. | Validate string choices in host configuration. | Never for the invalid manifest. |
| <a id="dbxk023"></a>`DBXK023` | A supplied setting value is outside its range. | Set `Percent = 150` for range 0–100 → provide an in-range value. | Keep the previous accepted value and report the rejection. | Never; operator input is invalid. |
| <a id="dbxk024"></a>`DBXK024` | A setting's minimum exceeds its maximum. | Range 10–1 → change it to 1–10. | Remove the range if it is not meaningful. | Never. |
| <a id="dbxk030"></a>`DBXK030` | A hook plugin has no subscriptions. | Ship an event kernel with `Subscriptions = []` → declare at least one event. | Use a server-extension package for request/response behavior. | Never. |
| <a id="dbxk031"></a>`DBXK031` | A subscription omits event/kernel or runtime wiring uses an unsubscribed event. | Wire `DamageEvent` to a chat-only kernel → wire only declared events. | Register a separate correctly declared kernel. | Never. |
| <a id="dbxk032"></a>`DBXK032` | A required entrypoint is missing or non-public. | Point `Handle` at a private/missing function → expose the named public entrypoint. | Regenerate standard entrypoint aliases. | Never. |
| <a id="dbxk033"></a>`DBXK033` | Entrypoint parameters or return type do not match the event/settings contract. | `ShouldHandle` returns `Int32` → return `Bool` with exact parameters. | Rebuild from the typed kernel abstraction. | Never. |
| <a id="dbxk034"></a>`DBXK034` | Entrypoints disagree on parameters or adapters conflict. | Give `Handle` an extra parameter → make both signatures identical. | Use one adapter registration per event type. | Never. |
| <a id="dbxk035"></a>`DBXK035` | Live settings are not exact trailing entrypoint parameters. | Put a renamed setting before event fields → append exact name/type settings in manifest order. | Regenerate the entrypoint signature. | Never. |
| <a id="dbxk036"></a>`DBXK036` | An adapter writes a different count/type than it declares. | Declare one `Int32`, write two strings → make declaration and writer exactly agree. | Replace the host adapter before running the plugin. | Never; this is a host contract error. |
| <a id="dbxk040"></a>`DBXK040` | Effects are empty or contain an unknown value. | Declare `"filesystem"` → use defined `SandboxEffect` values derived from verification. | Regenerate effects from the module analysis. | Never. |
| <a id="dbxk041"></a>`DBXK041` | Declared effects differ from verified entrypoint effects. | Declare no file effect while reading a file → declare the verified exact set. | Remove the effectful operation and re-verify. | Never; policy decisions depend on it. |
| <a id="dbxk042"></a>`DBXK042` | The execution-mode value is undefined. | Cast `99` to the enum → choose a supported mode. | Use `Auto` when no specific backend is required. | Never. |
| <a id="dbxk043"></a>`DBXK043` | Verified async behavior lacks the runtime async grant. | Install async binding under a sync-only policy → call `AllowRuntimeAsync()`. | Remove the async binding or run in a suitable containment profile. | Never. |
| <a id="dbxk044"></a>`DBXK044` | Required capabilities differ from verified capabilities. | Hand-add or strip a capability → regenerate from verified source. | Remove the capability-touching operation and rebuild. | Never; capability claims are not self-asserted. |
| <a id="dbxk045"></a>`DBXK045` | A required package collection or entry is null. | Set `Functions = null` → provide non-null collections without null entries. | Recreate the package with its public builder/generator. | Never. |
| <a id="dbxk046"></a>`DBXK046` | An indexed predicate uses an undefined operator. | Cast `99` to `IndexPredicateOperator` → use a defined comparison. | Omit the index hint and evaluate verified IR. | Never for a claimed index. |
| <a id="dbxk047"></a>`DBXK047` | An indexed predicate declares an unsupported value type. | Set value type to `decimal` → use bool/int/long/double/string. | Omit the index hint and evaluate verified IR. | Never for a claimed index. |
| <a id="dbxk048"></a>`DBXK048` | Full index coverage is claimed without any predicates. | `IndexCoversPredicate = true` with an empty list → provide predicates or set false. | Evaluate the verified predicate normally. | Never; the claim could skip authorization logic. |
| <a id="dbxk049"></a>`DBXK049` | An indexed value's runtime type differs from its declared type. | Declare `int` while boxing a `long` → make type and value agree. | Omit the invalid index hint. | Never for a claimed index. |
| <a id="dbxk050"></a>`DBXK050` | Manifest text is blank, controlled, or resembles a forbidden CLR/IL descriptor. | Use an identifier containing a newline/type descriptor → use stable plain text. | Regenerate identifiers from trusted source names. | Never; this is an input-hardening boundary. |
| <a id="dbxk051"></a>`DBXK051` | A required capability is a wildcard. | Require `event.read.*` → list concrete capability IDs. | Split broad behavior into explicit least-privilege grants. | Never. |
| <a id="dbxk052"></a>`DBXK052` | A required capability identifier has an empty dotted segment. | Require `event.read.bad..id` → use a valid concrete capability ID. | Regenerate required capabilities from validated package metadata. | Never; malformed IDs cannot express a safe grant. |
| <a id="dbxk060"></a>`DBXK060` | Another session owns the plugin ID. | Session B replaces session A's ID → choose another ID or let A uninstall. | Disconnect the owning session through host policy. | Never; this is an ownership boundary. |
| <a id="dbxk061"></a>`DBXK061` | A session updates settings for another owner's plugin. | Session B updates A's kernel → update only kernels installed by B. | Route the request through the owning session. | Never; this is an ownership boundary. |
| <a id="dbxk062"></a>`DBXK062` | A `Run` terminal reached runtime without generated verified IR. | Omit/fail analyzer lowering then call `Run` → fix `DBXK114` and regenerate. | Bind a kernel class with `Use`/`Register`. | Never. |
| <a id="dbxk063"></a>`DBXK063` | A generated hook chain uses a pipeline without an installer. | Construct a standalone pipeline then call `UseGeneratedChain` → use `server.Hooks`. | Install the package explicitly through `PluginServer`. | Never. |
| <a id="dbxk064"></a>`DBXK064` | One subscription pipeline receives conflicting adapters. | Register two adapter shapes for the same event → retain one stable adapter. | Use a distinct event type/pipeline. | Never. |
| <a id="dbxk065"></a>`DBXK065` | A generated subscription chain uses a pipeline without an installer. | Call `UseGeneratedChain` outside `server.Subscriptions` → use the server pipeline. | Install the package explicitly. | Never. |
| <a id="dbxk066"></a>`DBXK066` | A result hook is fired with the wrong result type. | Fire `OtherResult` for a context declaring `Decision` → use `Decision`. | Create a distinct context for the other result contract. | Never. |
| <a id="dbxk067"></a>`DBXK067` | Registrations provide different context factories for one event/context pair. | Register two factory delegates → reuse one stable factory. | Choose a distinct context type. | Never. |
| <a id="dbxk070"></a>`DBXK070` | A server-extension manifest has no RPC entrypoint. | Install an extension with `RpcEntrypoint = null` → generate/set the batch entrypoint. | Package it as a hook kernel if it is event-driven. | Never. |
| <a id="dbxk071"></a>`DBXK071` | The RPC entrypoint is missing or non-public. | Name a private/missing function → expose the exact public function. | Regenerate with `[ServerExtension]`. | Never. |
| <a id="dbxk072"></a>`DBXK072` | A server extension returns an unsupported sandbox type. | Return an arbitrary CLR object → return a supported scalar/list/record DTO. | Marshal through a public supported DTO shape. | Never. |
| <a id="dbxk073"></a>`DBXK073` | One manifest mixes hook subscriptions and RPC extension shape. | Set both subscriptions and `RpcEntrypoint` → split the packages. | Compose the separate packages at the host. | Never. |
| <a id="dbxk074"></a>`DBXK074` | Entrypoint aliases differ from the RPC entrypoint. | Alias `Handle` to a hook function → alias both to the extension entrypoint. | Regenerate the extension package. | Never. |
| <a id="dbxk075"></a>`DBXK075` | A manifest collection contains a null entry. | Include null in settings/subscriptions/predicates → remove it. | Recreate the manifest through typed builders. | Never. |

## Migration and source of truth

The analyzer release tables remain the machine-readable severity inventory, while this page is the
stable actionable help surface. Runtime `DBXK` entries are also maintained in
`PluginDiagnosticCodes`; CI compares the production descriptors/catalog with the anchors above.

Legacy ShaRPC `SHARPC###` codes map to `DBXS###`; legacy Safe-IR `SGP###` codes map to `DBXK###`.
Update old `.editorconfig` and `<NoWarn>` entries. See
[Migration from standalone repositories](/contributing/migration-from-standalone-repos/).
