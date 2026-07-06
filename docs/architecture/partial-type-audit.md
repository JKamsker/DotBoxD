# Partial Type Audit

Issue #498 removed mechanical test splits named `*.Part*.cs` and added a CodeEnforcer budget of zero
for future files using that pattern.

Production still uses partial types where the split is a named domain boundary rather than a line-count
escape hatch:

- Host and service runtime surfaces such as `SandboxHost`, `RpcHost`, `RpcPeer`, `IRpcInvoker`, and
  `RpcPeerOutboundInvoker` are split by public capability area, lifecycle, streaming, ValueTask, or
  invocation mechanics.
- Kernel runtime and verifier surfaces such as `CompiledRuntime`, `CompiledBindingDispatcher`,
  `ResourceMeter`, `SandboxContext`, and `GeneratedAssemblyVerifier` are split by value family,
  binding family, or generated-verifier facet.
- Plugin RPC and hook runtime surfaces such as `KernelRpcMarshaller`, `KernelRpcBinaryCodec`,
  `HookPipeline`, `RemoteHookPipeline`, `HookRegistry`, `PluginServer`, and `PluginSession` are split
  by wire type family, generated hook chains, result hooks, installation, or policy behavior.
- Analyzer/source-generator models and emitters are split by syntax/semantic phase, payload type
  family, diagnostic family, or generated source facet.
- Platform-sensitive helpers such as `SafeFileNoFollow` are split by native/platform implementation.

Those production partials remain justified because their file names describe behavior or platform
ownership. New splits should follow the same rule: use a meaningful domain name, keep files within
the CodeEnforcer budgets, and extract named support or composed helper types when the split is only
serving line count.
