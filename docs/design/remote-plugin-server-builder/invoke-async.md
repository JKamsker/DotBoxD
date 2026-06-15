# InvokeAsync — inline-kernel and explicit capture bag (deep dive)

This document specifies how `server.Kernels.InvokeAsync(lambda)` is detected, lowered to verified sandboxed
IR at compile time, shipped over async IPC, and executed server-side. It also documents the implemented
capture sync-in/out fallback: an explicit mutable capture bag. The server never compiles plugin source; only
verified IR crosses the boundary.

It corrects the reviewed designs where they were unsound and states every deferral explicitly.

---

## 1. Target shape

No-capture inline kernel:

```csharp
var monsterHealth = await server.Kernels.InvokeAsync((IGameWorldAccess world) =>
{
    // Runs INSIDE the kernel (server-side, sandboxed verified IR).
    var monster = world.GetMonster("monster-2");
    return monster.Health;
});
```

Explicit capture-bag sync-in/out:

```csharp
var capture = new MonsterProbeCapture { MonsterId = "monster-2" };
var monsterName = await server.Kernels.InvokeAsync(
    capture,
    (IGameWorldAccess world, MonsterProbeCapture bag) =>
    {
        var monster = world.GetMonster(bag.MonsterId);
        bag.LastHealth = monster.Health;
        return monster.Name;
    });
```

The lambda body is lowered to the same verified IR a `[KernelRpcService]` method produces. The explicit bag
is encoded as one record argument, and assigned bag properties are returned in a response record and written
back to the same object after the await.

### Object snapshot surface

The implemented object surface is the flat host binding `world.GetMonster(id)`, returning
`MonsterSnapshot(string Id, string Name, int Health, int Level, int Position)`. Member access such as
`monster.Health` lowers to `record.get` by positional field order. The nested spelling
`world.Monsters.Get(id)` remains an ergonomic alias option; the verified binding is flat.

---

## 2. Detection (fourth generator pipeline)

Mirror the existing `InvokeKernel` pipeline in `PluginPackageGenerator.Initialize`:

```csharp
var invokeAsyncResults = context.SyntaxProvider
    .CreateSyntaxProvider(
        static (node, _) => node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "InvokeAsync" }
        },
        static (ctx, ct) => InvokeAsyncModelFactory.Create(ctx, ct))
    .Where(static r => r is not null)
    .Select(static (r, _) => r!);
```

The predicate is allocation-free and name-only. `InvokeAsyncModelFactory.Create` then **resolves the
receiver's type and returns `null` unless it is the kernel-invocation surface**
(`DotBoxDGenerationNames.Metadata.KernelInvocationSurfaceType` = the FQN of `RemoteKernelControl`), mirroring
`HookChainModelFactory`'s `HookPipeline<TEvent>` guard. Name alone is not a sufficient discriminator.

### Lambda shape validation

Accept only:
- either a single explicitly-typed `IGameWorldAccess` lambda parameter, or
- a mutable capture object argument plus a two-parameter lambda
  `(IGameWorldAccess world, TCaptures captures)`, where `TCaptures` is a supported record-shaped DTO, and
- a **block body** (expression-body lambdas are out of scope — use `InvokeKernel` for those).

Anything else returns `null` (fail-safe, no output).

---

## 3. Capture-bag analysis

Use Roslyn data-flow:

```csharp
var flow = semanticModel.AnalyzeDataFlow(lambdaBlock);
```

- Lambda-only calls reject `DataFlowsIn` / `DataFlowsOut` symbols other than the lambda parameter. This keeps
  no-capture lowering honest.
- Capture-bag calls also reject ambient `DataFlowsIn` / `DataFlowsOut`; only the explicit bag parameter can
  carry values across the boundary.
- **Sync-in** = the capture-bag object encoded as a `KernelRpcValue.Record`.
- **Sync-out** = simple assignments to supported settable bag properties, e.g. `bag.LastHealth = ...`.

Each assigned bag property gets an initialized IR local (`__syncOut_<Property>`), and returns read those
locals back through the response envelope.

Nullable-reference capture-bag fields are a documented caveat. The IR scalar type system cannot represent
"String-or-null": `KernelRpcValue.String(null)` coerces to empty, and `KernelRpcValueConverter.ToSandboxValue`
validates each value's kind against the IR-declared `SandboxType`. Capture bags should use non-nullable fields
or accept null-to-empty-string behavior.

---

## 4. IR function shape

```
function "$anon:<hex>" () -> <return>
function "$anon:<hex>" (captures: Record([...])) -> Record([returnValue, syncOut0, ...])
```

- The lambda's `IGameWorldAccess world` parameter is **not** an IR parameter — calls on it
  (`world.GetMonster(id)`) lower to a host-binding call via the existing host-binding lowerer.
- For capture-bag calls, the bag lambda parameter is the single IR parameter. Reads like `bag.MonsterId`
  lower to `record.get(Var("bag"), 0)`.

### Return type

- **No sync-out:** the IR return type is the lowered lambda return type directly.
- **With sync-out:** the IR return type is `Record([returnValue, syncOut0, …])`.

### Manifest / package JSON

Constructed exactly like `RpcKernelModelFactory.EmitPackage`:
`pluginId = "$anon:" + HookChainIdentity.Compute(invocation)` (FNV-1a of file path + span start; verified to
pass `ValidateText` and the descriptor guards — a colon and a hex run are not forbidden), `mode=Auto`,
`liveSettings=[]`, `subscriptions=[]`, `rpcEntrypoint`=the function id, `requiredCapabilities`=the
host-binding capability sink, `effects`=`Cpu` (+`Alloc` when the lowerer allocates). The generator emits
`module.id == pluginId` **and** `module.metadata.pluginId == pluginId` identically (the existing RPC factory
already emits `"metadata":{"kernel":…,"pluginId":…}`).

The package is emitted as a generated `…$anon_<hex>PluginPackage.Create()` static whose body is
`PluginPackageJsonSerializer.Import("<json literal>")` — identical structure to every other generated
package. No runtime package resolution.

---

## 5. Body lowering — honest reuse boundary

`DotBoxDRpcJsonLowerer.LowerBody(block)` lowers the body **statements** unchanged: locals, assignments,
`foreach`, `if`/`else`, `record.new`, `list.Add`, `return <expr>`. What is **net-new** in
`InvokeAsyncModelFactory` (not "reuse"):

1. Building the optional single record-shaped capture-bag parameter.
2. Declaring leading IR locals for assigned bag properties, initialized from the inbound bag.
3. Overriding simple assignments to bag properties so `bag.LastHealth = expr` lowers to
   `set __syncOut_LastHealth`.
4. For sync-out: synthesizing `return record.new([userReturnExpr, syncOut0, …])`. This is done structurally
   for each lowered return path; the implementation does not scan or post-process JSON text.

---

## 6. Capture marshalling (the central mechanism)

A C# interceptor's non-receiver parameters must match the intercepted method's argument list **exactly** —
it **cannot** add `out`/`ref` parameters or change the return type (confirmed by the existing
`DotBoxDHookChainInterceptorEmitter`, whose interceptor returns `HookPipeline<TEvent>` and forwards the
identical handler). Therefore captures cannot be threaded as extra interceptor parameters, and
**closure-`Target` reflection is rejected** (`GetField("name")` depends on Roslyn name-mangling, which is
not spec-guaranteed; the reviewed design even mis-guessed `"<lastMonsterName>i__Field"`).

Implementation finding: the call-site-local mechanism above is not valid C#. A generated interceptor method
body is compiled as a normal generated method body; it cannot reference locals from the intercepted caller's
lexical scope by name. The generator therefore rejects lambda-only calls that capture caller locals. That
keeps the lambda-only overload correct for no-capture inline kernels and avoids closure-field reflection.

The implemented capture path is an explicit mutable capture bag:

```csharp
var bag = new MonsterProbeCapture { MonsterId = "monster-2" };
var name = await server.Kernels.InvokeAsync(
    bag,
    (IGameWorldAccess world, MonsterProbeCapture captures) =>
    {
        var monster = world.GetMonster(captures.MonsterId);
        captures.LastHealth = monster.Health;
        return monster.Name;
    });
```

The bag is encoded as one `KernelRpcValue.Record` argument (sync-in). Assignments to bag properties lower to
generated IR locals. If any bag property is assigned, the anonymous kernel returns
`Record([returnValue, syncOut0, …])`; the interceptor decodes the response and writes each sync-out value
back onto the same bag object after the await. This is more explicit than closure-local capture, but it is
reflection-free, compiler-stable, and works with the interceptor parameter-shape rules.

### Generated interceptor (no-capture)

```csharp
[InterceptsLocation(version, "<data>")]
internal static async ValueTask<int> InvokeAsync_0(
    this RemoteKernelControl kernels,
    Func<IGameWorldAccess, int> lambda)   // matches the original signature exactly
{
    ArgumentNullException.ThrowIfNull(lambda);
    var __pluginId = await kernels
        .EnsureAnonymousKernelAsync("$anon:<hex>", global::…$anon_<hex>PluginPackage.Create)
        .ConfigureAwait(false);

    var __request  = KernelRpcBinaryCodec.EncodeArguments(Array.Empty<KernelRpcValue>());
    var __response = await kernels.WireClient
        .InvokeKernelRpcAsync(__pluginId, __request).ConfigureAwait(false);
    var __result   = KernelRpcBinaryCodec.DecodeValue(__response);

    return __result.RequireInt32();
}
```

### Capture-bag sync-out addition

The response is a `Record([returnValue, syncOut0, …])`. The interceptor splits it, assigns each sync-out
field back to the caller-provided bag object, then returns the decoded return value.

---

## 7. Anonymous-kernel install — identity, caching, concurrency

New members on `RemoteKernelControl`:

```csharp
internal IKernelRpcWireClient WireClient => _control;   // IGamePluginControlService implements InvokeKernelRpcAsync

private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _anonymousKernels = new(StringComparer.Ordinal);

internal Task<string> EnsureAnonymousKernelAsync(string pluginId, Func<PluginPackage> packageFactory)
    => _anonymousKernels.GetOrAdd(
        pluginId,
        static (id, args) => new Lazy<Task<string>>(
            () => InstallAnonymousKernelAsync(id, args.PackageFactory, args.Control),
            LazyThreadSafetyMode.ExecutionAndPublication),
        (PackageFactory: packageFactory, Control: _control)).Value;

private static async Task<string> InstallAnonymousKernelAsync(
    string pluginId,
    Func<PluginPackage> packageFactory,
    IGamePluginControlService control)
{
    var json = PluginPackageJsonSerializer.Export(packageFactory());
    var installedPluginId = await control.InstallKernelRpcAsync(json).ConfigureAwait(false);
    if (!string.Equals(installedPluginId, pluginId, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(...);
    }

    return installedPluginId;
}
```

- **Install-once-per-id-per-connection.** The `ConcurrentDictionary<string, Lazy<Task<string>>>` `GetOrAdd`
  ensures concurrent first-calls share a single install task. A naive check-then-install races: two installs
  of the same `$anon:` id trigger the same-owner reinstall guard (`KernelRegistry.Add`, DBXK060), which
  **replaces and revokes** the incumbent — cancelling an in-flight invoke's execution gate. The concurrency-safe
  cache is **mandatory**, not an optimization.
- **Per-connection cache.** `RemoteKernelControl` is constructed fresh per connection
  (`new RemotePluginServer(...)`), so the cache clears naturally on reconnect; the new session re-installs
  and the old one is revoked on disconnect.
- **Throwing stub overload.** `RemoteKernelControl` declares
  `public ValueTask<TReturn> InvokeAsync<TReturn>(Func<IGameWorldAccess, TReturn> lambda) => throw …;` so an
  un-intercepted call (interceptors opt-in missing, or `GetInterceptableLocation` returned null) fails
  loudly. Ensure this is the **sole** `InvokeAsync` candidate at the call site to avoid overload-resolution
  mis-binding, and emit a build diagnostic when interception location is null.

---

## 8. Server-side execution

Anonymous kernels reuse the entire named-RPC path, unchanged:

1. `InstallKernelRpcAsync(json)` → `PluginPackageJsonSerializer.Import` → `ServerPolicy.ForKernel(manifest
   .RequiredCapabilities)` → `PluginSession.InstallRpcAsync` → `PluginServer.InstallRpcCoreAsync` →
   `RpcKernelPackageValidator.Validate` → `SandboxHost.PrepareAsync` (capability deny-at-install via
   `PolicyResolver.Validate`) → `RpcKernelPackageValidator.ValidatePrepared` → `new InstalledKernel(...)`
   owned by the session.
2. `InvokeKernelRpcAsync(pluginId, bytes)` → `DecodeArguments` → per-arg `KernelRpcValueConverter
   .ToSandboxValue` against each `function.Parameters[i].Type` → `InstalledKernel.InvokeRpcAsync` →
   `BuildRpcInput` → execute → return one `SandboxValue`.

### `BuildRpcInput` parameter shapes (must match the generated IR)

Verified in `InstalledKernel.Rpc.cs`:
- **0 captures** → 0-param entrypoint → input `SandboxValue.Unit`.
- **1 capture** → 1-param entrypoint → input is the **bare** `arguments[0]` (not a 1-element frame). The
  generated IR body must read the bare value.
- **N≥2 captures** → input is `SandboxValue.FromList(values, values[0].Type)` — a positional frame whose
  declared element type is the first capture's type. Heterogeneous captures round-trip correctly because
  each wire arg was already validated against its own IR parameter type before packing; the element-type
  tag is an internal positional-frame detail the IR destructures by index.

### Capability gating

`requiredCapabilities` are derived by `DotBoxDHostBindingExpressionLowerer` from the `[HostBinding]` calls in
the lambda body — the same sink used for named RPC kernels. `ServerPolicy.ForKernel` grants exactly the
matching namespaces; a lambda touching an ungranted binding fails at install. **No new policy
infrastructure.**

---

## 9. Sync-out wire envelope

The response must carry mutated capture-bag fields alongside the return value. The implemented carrier is a
single `Record` over the existing `InvokeKernelRpcAsync` method:

- no sync-out: the response is the user return value directly;
- with sync-out: the response is `Record([returnValue, syncOut0, …])`.

The generated interceptor knows the expected field count from the source-generated capture shape and checks
it before writing sync-out values back to the bag. This needs no manifest metadata, no new IPC method, and no
new binary codec.

---

## 10. Interceptor attribute dedup — hard prerequisite

`DotBoxDHookChainInterceptorEmitter.Emit` calls
`context.AddSource("DotBoxDInterceptsLocationAttribute.g.cs", AttributeSource)`. A second emitter adding the
same hint name **crashes the generator** whenever a compilation contains both a hook chain and an
`InvokeAsync`. Before wiring the `InvokeAsync` pipeline, extract a shared
`InterceptsLocationAttributeEmitter.EnsureEmitted`, driven by a combined `IncrementalValueProvider<bool>`
over both interception sets, that emits the attribute file exactly once. The hook-chain emitter is
refactored to call it.

---

## 11. Phasing summary

- **Phase 2:** detection + receiver guard + lambda-shape validation + no-capture body lowering + anonymous
  package + interceptor + concurrency-safe install + attribute dedup.
- **Phase 3:** explicit mutable capture-bag sync-in/out via record argument plus response record envelope.
- **Phase 4:** object-returning host binding via flat `world.GetMonster(id)`, Record-typed
  `BindingDescriptor`, and member access through `record.get`.

## 12. What is reused vs new

**Reused as-is:** `HookChainIdentity.Compute`; `DotBoxDRpcJsonLowerer.LowerBody`/`LowerInvocation`/
`LowerMemberAccess`; `DotBoxDHostBindingExpressionLowerer` (capability sink); `DotBoxDRpcTypeMapper.JsonType`;
`KernelRpcBinaryCodec` (encode args / decode value); `KernelRpcValueConverter`; `InstalledKernel
.InvokeRpcAsync` + `BuildRpcInput`; `PluginSession`/`PluginServer` install + ownership + revocation;
`SandboxHost.PrepareAsync` + `PolicyResolver` capability gating; `RpcKernelPackageValidator`.

**New:** `InvokeAsyncModelFactory`, `InvokeAsyncCallShape`, `InvokeAsyncInterceptorEmitter`, shared
`InterceptsLocationAttributeEmitter`, the fourth generator pipeline,
`DotBoxDGenerationNames.Metadata.KernelInvocationSurfaceType`,
`DotBoxDGenerationNames.Metadata.KernelInvocationDelegateType`, `RemoteKernelControl` members
(`InvokeAsync<TReturn>` stub, capture-bag `InvokeAsync<TCaptures,TReturn>` stub, `WireClient`,
`EnsureAnonymousKernelAsync`), and the sample `world.GetMonster(id)` snapshot binding.
