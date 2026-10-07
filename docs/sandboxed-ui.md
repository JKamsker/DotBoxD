# Sandboxed UI

`DotBoxD.UI`, `DotBoxD.UI.Runtime` and `DotBoxD.UI.Avalonia` are optional .NET 10 packages
implementing [issue #1453](https://github.com/JKamsker/DotBoxD/issues/1453). Plugin authors compose
C# components and typed state in their worker. The existing plugin analyzer lowers declared local
methods into restricted IR. Trusted host code validates the package, owns state, and renders Avalonia
controls without loading the plugin implementation assembly.

## Trust boundary

```text
isolated plugin process                 trusted host process
C# component composition                UiPackageJson.Import + host policy
  -> data-only UiPackage  -- IPC -->     SandboxHost.PrepareAsync for every kernel
arbitrary remote handlers <-- events -- UiSession owns authoritative state
versioned scalar patches -- IPC -->     trusted IUiRenderer creates/updates controls
```

The host never loads plugin executable code. Packages contain a closed scalar/keyed-text-list schema, positive
numeric IDs, fixed primitive/property/event enums, and existing DotBoxD restricted IR JSON.
There are no plugin-selected CLR types, arbitrary objects, delegates, markup extensions, AXAML,
Avalonia instances, native pointers, or graphics resources in the protocol. Text that happens to
contain markup is plain text; renderer adapters must never interpret it as executable markup.

This does not sandbox arbitrary managed assemblies or Avalonia subclasses. The embedding application
must launch workers with its own OS/process isolation policy. The sample launches a separate process
to demonstrate the execution boundary; it does not configure reduced OS permissions.

## Public primitives

Consumers can hand-write the entire feature using public APIs. Construct `UiPackage`, `UiNode`,
`UiStateSlot`, `UiProperty`, `UiKernel`, and `UiEvent`, then call `UiHost.InstallAsync`. Plugin-side
functions can compose reusable node collections with IDs selected by the author. No attribute or
generated UI-only interface is required. These APIs are public so the authoring builder, handler generator and renderer
adapter are optional layers over the same installation/state/event primitives.

```csharp
using var sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings());
var host = new UiHost(sandbox, SandboxPolicyBuilder.Create().WithFuel(10_000).Build(),
    new UiPolicy { MaxNodes = 1_000, MaxStateBytes = 64 * 1024 });
var package = UiPackageJson.Import(packageJson, new UiPolicy());
await using var session = await host.InstallAsync(package, trustedRenderer, connectionAdapter);
await session.SetInputAsync(textBoxId, UiPropertyId.Text, UiValue.FromString("query"));
await session.DispatchAsync(searchEventId);
```

The host borrows `SandboxHost` and the connection-bound `IUiRemoteTransport`. Their owners manage
their lifetimes. Installation takes ownership of the renderer, including when validation, initial
bindings, or materialization fails. A renderer instance belongs to exactly one installation.

## V1 schema and supported features

Format version is independently versioned at `1`. Node/state/kernel/event/endpoint IDs are positive,
unique within each ID domain, and scoped to one session. Nodes form one static rooted tree. Cycles,
shared children, unreachable nodes, unknown primitives/properties, ambiguous property sources, and
incompatible state types are rejected. The initial state value defines each slot's fixed type.

| Primitive | Children | Specific properties |
| --- | --- | --- |
| Stack | bounded ordered children | Horizontal, Spacing |
| Grid | bounded ordered children | Columns (1–64); attached Row/Column on children |
| Border, ScrollViewer | at most one child | Padding on Border |
| Items | bounded static children **or** keyed text rows | Items bound to a typed list slot |
| Text, Button | none | Text |
| TextBox | none | Text, including two-way state binding |
| CheckBox | none | Text, Checked, including two-way Checked binding |
| ProgressBar | none | Number-valued Value and Maximum |
| Slider | none | Number-valued Value (two-way) and Maximum |

All nodes support Boolean `Enabled` and `Visible`. State supports Boolean, Int32, finite Number
(F64), well-formed UTF-16 String and `ImmutableArray<UiListItem>` with unique nonempty string keys
and text values. Properties specify exactly one literal, state slot, or derived kernel. The event
kind is Button.Click. Only registered input properties accept two-way writes. Layout numbers have
finite, explicit bounds; arbitrary CLR properties and styles are never reflected or interpreted.

Keyed Items rows are trusted TextBlocks. A batch can replace/reorder the bounded data list; existing
keys retain their controls and missing keys are removed. The runtime checks total evaluated row
count plus static nodes against `MaxNodes` before installation or commit, so one state patch cannot
force unbounded materialization. Custom dynamic templates and virtualization are outside v1.
Image/resource handles are an explicit post-MVP limitation permitted by the issue's definition of
done. No resource URIs are fetched, decoded or executed; the protocol's resource budget is zero.

A `UiKernel` contains ordinary restricted IR JSON, a declared entrypoint, and an optional input
slot ID. It has no parameters when input is absent; otherwise it takes exactly one parameter of
that slot's type. It returns one scalar. Local events write that result into their output slot;
derived bindings project results into properties without mutating state. Kernels shared by multiple
properties are evaluated once per evaluation pass.

Every declared kernel is prepared by the existing `SandboxHost` with the host-selected
`SandboxPolicy`, then executed with ordinary capability/effect validation,
fuel, host-call, allocation, string, collection, and deadline limits. `UiHost` accepts optional
`SandboxExecutionOptions`: interpreted is the default; compiled execution uses the same prepared
plan and ordinary artifact verifier. Both modes have validation regression coverage. A host can register explicit
bindings through the existing `SandboxHostBuilder.AddBinding` public API. The UI layer adds no
grants and no alternate compiler/verifier path. Bindings may have externally visible host effects;
those effects are governed by the chosen sandbox policy and cannot be rolled back by UI state.
Use pure bindings for derived UI properties when rollback of all observable behavior is required.

## Import and canonical identity

`UiPackageJson.Import` enforces encoded package byte and JSON-depth limits before decoding the
typed model, checks collection lengths before typed allocations, rejects duplicate/unknown JSON
properties, checks scalar Unicode/types, validates the tree/schema, and imports kernel IR through
the existing JSON importer. It does not authorize kernels: that happens at installation.
Hand-written packages go through the same boundary before materialization.

`Export` sorts definition collections and properties by stable IDs, preserves child order,
and normalizes kernel JSON through the existing importer/exporter. Import and export revalidate
the normalized package, including kernel byte limits when canonicalization adds optional fields.
`ComputeHash` hashes this canonical UTF-8 with SHA-256. Equivalent definition ordering and kernel whitespace produce the
same hash; child order and initial state remain significant. This is format-v1 identity, not a
promise that future schema versions use identical bytes.

## State ordering and atomicity

The host issues a fresh random session ID on every install and owns authoritative state. Session
operations serialize admission, candidate validation, local execution, binding evaluation, commit,
and renderer update. Each accepted nonempty mutation increments the global state version once,
including writes of an unchanged value. Empty patches leave the version unchanged.

Remote events capture a consistent session ID/version/full bounded state snapshot. The remote
adapter maps a declared numeric endpoint to an existing typed IPC contract. The reply must echo
the dispatched ID and version. Patches also compare against the current live version. If any
local edit or another reply has committed since dispatch, the response is rejected; the worker
cannot rebase its response onto a newer version. An application may explicitly dispatch a new event
to retry against fresh state. No implicit merge or last-write-wins behavior exists in v1.

Patches validate all IDs/types, duplicate slots, slot count, string lengths, and total state bytes
before any state commit. All derived bindings evaluate against candidate state before commit.
Malformed, over-limit, cross-session, stale, cancelled-before-commit, or kernel-failing operations
leave state/version/rendered values unchanged. Local event output writes one slot; a remote patch
may write several. External host binding effects are outside this state transaction.

One renderer update contains all changed property values. The normal tree is materialized once.
If an update fails after commit, the session closes admission before releasing its state gate,
then disconnects and disposes its renderer; a disconnected
session cannot be used to read or mutate the partially rendered UI. Renderer adapters must marshal
to the toolkit UI thread, dispose subscriptions/controls, and queue semantic input without
synchronously reentering the session from Materialize/Update/Dispose callbacks.

## Quotas, cancellation, and connection ownership

`UiPolicy` independently bounds encoded package bytes, node count, depth, children per node,
state slots/bytes, text/list length, input rate, kernels and their bytes, events/endpoints, patch slots, and
in-flight remote events. Depth traversal is iterative. Unknown versions/features fail closed.
The trusted adapter must separately cap IPC frames and decoding allocations; session validation
cannot retroactively bound an object graph a transport already decoded. The sample uses bounded
named-pipe frames and a typed scalar request/reply contract.

Remote calls release the state gate, allowing immediate local input. They have a host-selected
timeout and receive linked caller/session cancellation. Teardown invalidates admission first,
cancels in-flight operations, serializes renderer disposal, and clears live state/routes/plans and
adapter references. Even a transport that ignores cancellation cannot delay the session's wait
indefinitely. The transport owner remains responsible for cancelling/draining its underlying RPC
work and notifying/disconnecting sessions when its connection closes. Transport failures/timeouts
disconnect the session; cancelling one call by its caller does not disconnect it. Releasing remote
admission does not wait for unrelated rendering work. Invalid patches
are rejected without disconnecting by default.

## C# component authoring

`DotBoxD.UI.Authoring` is a worker-side builder layered over the public schema. `IUiComponent.Render`
can compose other components using the same builder; typed `UiState<T>`, `UiBinding<T>` and
`UiBoundKernel<T>` keep scalar binding and local event targets aligned. Builder allocation order
produces deterministic IDs; canonical hashing also normalizes definition order. Authoring executes
only in the worker; it never ships a render delegate or plugin object to the host.
`UiElement` handles belong to their creating builder; composing children or selecting a root from
another builder is rejected before changing nodes. Handwritten packages use the public `UiNode`
and `UiPackage` schema directly.

Reference the optional `DotBoxD.Plugins.Analyzer` as an analyzer to use `[UiLocalHandler]` on static
methods of a top-level non-generic partial class/struct. Each method accepts zero or one
int/bool/double/string parameter and returns one scalar. The generated `MethodUiKernel()` returns
a public `UiKernelDefinition<TInput,TOutput>`. The existing RPC C# lowerer handles expressions and
statement bodies, including structured control flow and explicitly attributed host bindings.
Use `value.ToString(CultureInfo.InvariantCulture)` for invariant Int32 text. Other unsupported
operations produce error `DBXU001` at the method, suggesting explicit remote RPC or a host binding.
There is no automatic remote fallback. Overloads, captures, instance handlers, generic/ref/async
local signatures, file-local containers and unlowerable library calls fail closed. Expression and
block bodies share return-conversion checks: built-in numeric widening is supported; user-defined
implicit scalar conversions are rejected during generation.

`[UiRemoteHandler(7)]` generates `MethodUiEndpoint`, a stable numeric endpoint for
`UiBuilder.RemoteButton`. The method body remains ordinary C# in the worker; the host binds the
endpoint through `IUiRemoteTransport` to an existing generated typed RPC contract. It does not
invoke or discover plugin methods by name. The sample demonstrates this mapping explicitly.

`GenerateKernel = false` and `GenerateEndpoint = false` independently disable the corresponding
method output. A handwritten member with the generated name, including an accessible inherited
member, wins before generation-only restrictions
on container shapes and handler overloads are applied. No attribute is necessary: a
consumer may handwrite the identical kernel definition, route, builder or entire package. Tests
compare handwritten/generated execution and canonical hashes and guard incremental output caching.

## Avalonia integration

`AvaloniaUiRenderer` maps the fixed primitive enum to trusted control constructors and applies
registered properties. Creation, updates, capture and teardown marshal to `Dispatcher.UIThread`.
The host owns Avalonia application setup, themes and the containing TopLevel. Its UI-thread-only
`Root` can be mounted in a trusted Window/game compositor; mouse, focus, keyboard and value changes
then use normal Avalonia input routing into the semantic input queue. `UiHost` automatically
connects renderers implementing `IUiInputSource` to the session. Input queues and rate limits are
bounded; overflow disconnects and releases the session. Remote events do not block local typing;
rejected local edits (such as an overlong paste) and stale remote input are visible through
`UiSession.LastInputError` without closing the session. A rejected edit leaves host state unchanged
and restores the edited control to its authoritative value before reporting the error. Subsequent
valid input can proceed. Renderer update batches may therefore include an authoritative input
correction even when the state value has not changed.

`CaptureAsync(PixelSize, Vector)` measures/arranges the root and returns a host-owned
`RenderTargetBitmap` for CPU/offscreen composition. The embedding host disposes it and decides how
to upload/composite it. Graphics devices, textures, native handles and the bitmap stay in trusted
code. There is no per-frame IPC or plugin-to-host texture transfer. Hosts using another offscreen
backend can mount Root and drive their own Avalonia rendering/input pipeline. Dispose sessions
before stopping the dispatcher; the adapter detaches subscriptions and child controls on that thread.

## Validation

The [Avalonia offscreen process sample](../samples/SandboxedUi/README.md) runs in Linux/Windows CI and proves
that the host does not load the worker implementation assembly. Required security-boundary tests
cover malformed imports, quotas, state atomicity, fuel faults, remote ordering/concurrency,
renderer failure, and teardown. A seeded adversarial import corpus supplements hand-written cases.

`UiBenchmarks` measures 100/1,000-node validation/import/renderer-neutral installation, local button events,
two-way text, batched 100-slot patches, and over-limit rejection with allocation reporting:

```bash
dotnet run --project benchmarks/DotBoxD.Kernels.Benchmarks -c Release -- --filter '*UiBenchmarks*'
```

`UiAvaloniaBenchmarks` adds real 100/1,000-node Avalonia installation/materialization, repeated
state-only renderer allocation measurements and keyed row reorder updates. Use the filter
`*UiAvaloniaBenchmarks*` with the same benchmark command. Measurements establish regression
coverage; this change makes no claim of improved throughput or allocation counts.
