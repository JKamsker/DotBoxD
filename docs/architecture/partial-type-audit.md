# Partial Type Audit

Issue #498 removed mechanical test splits named `*.Part*.cs` and added a CodeEnforcer budget of zero
for future files using that pattern. CodeEnforcer also ratchets non-generated source partial types
that span multiple files with `maxSourceMultiFilePartialTypeCount`.

## Refactored Source Splits

These implementation partials were split into normal collaborators because their partial-private
coupling mostly existed to distribute line count:

- `PluginPackageJsonSerializer`: now a serializer facade plus reader, writer, and server JSON
  extension helpers.
- `SandboxWorkerExecutor`: worker result validation and binding evidence moved to focused helpers.
- `CollectionCallAnalyzer`: record and map intrinsic analysis moved to named analyzers.
- `ResourceMeter`: bulk, flat-value, deadline, reset, and host-call behavior moved to composed
  resource-meter helpers.
- `PendingRequests`: timeout scheduling moved to `PendingRequestTimeoutScheduler`.
- `SafeFileSystem`: path resolution and reparse checks moved to path resolver/guard helpers.
- `SandboxValidatedValueShapeMeter`: scalar, collection, limit, and error handling moved to normal
  helper types.
- `BindingRegistryValidator` and `PluginPackageValidator`: phase buckets moved to normal phase
  collaborators.

The source multi-file partial budget was ratcheted after each removal; it is currently 66.

## Remaining Source Partials

The remaining high-fanout source partials are retained because their files describe stable domain
facets, generator phases, or API-shape facets rather than numbered line-count splits. They remain
under the ratchet so any new source partial fanout must be deliberate.

Representative retained groups:

| Partial type | Reason retained |
| --- | --- |
| `KernelRpcMarshaller` | Public runtime marshalling facade split by wire/value family and record-shape internals. |
| `DotBoxDRpcJsonLowerer`, `RpcKernelValueConversionEmitter`, `RpcKernelPayloadReadEmitter` | Source-generator lowering and payload emitters split by syntax/value family. |
| `HookChainModelFactory`, `GeneratedRemoteHookChainFallback` | Analyzer model/fallback phases for hook-chain semantics and diagnostics. |
| `DotBoxDKernelMethodInliner` | Kernel descriptor lowering split by descriptor facet and host-binding shape. |
| `RpcPeerOutboundInvoker` | Runtime invocation facade split by unary, ValueTask, streaming, frame-send, and response-completion mechanics; `PendingRequests` was extracted from this area. |
| `SandboxHost`, `RpcHost`, `RpcPeer`, `PluginServer`, `PluginSession`, `InstalledKernel` | Runtime lifecycle/capability/API-surface facets with public facade stability. |
| `CompiledRuntime`, `KernelRpcBinaryCodec`, `CompiledBindingDispatcher` | Runtime binding/value-family facades used by generated or verified code. |
| `SandboxContext`, `SafeFileNoFollow` | State and platform-sensitive boundaries split by capability or OS-specific implementation. |

New splits should follow the same rule: use a meaningful domain name, keep files within the
CodeEnforcer budgets, and extract named support or composed helper types when the split only serves
line count.
