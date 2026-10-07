# Sandboxed UI foundation

`DotBoxD.UI` and `DotBoxD.UI.Runtime` are optional .NET 10 packages implementing the
renderer-neutral foundation of [issue #1453](https://github.com/JKamsker/DotBoxD/issues/1453).
This is the first staged implementation: package/schema validation, restricted local kernels,
versioned state, remote events, and a trusted renderer contract. The reference sample is headless.
Avalonia controls, native rendering surfaces, and a component source generator are follow-up work.

## Trust boundary

```text
isolated plugin process                 trusted host process
C# component composition                UiPackageJson.Import + host policy
  -> data-only UiPackage  -- IPC -->     SandboxHost.PrepareAsync for every kernel
arbitrary remote handlers <-- events -- UiSession owns authoritative state
versioned scalar patches -- IPC -->     trusted IUiRenderer creates/updates controls
```

The host never loads plugin executable code. Packages contain a closed scalar schema, positive
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
generated UI-only interface is required. These APIs are public so future authoring sugar and renderer
adapters can be optional layers over the same installation/state/event primitives.

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
incompatible state types are rejected. The initial state value defines each slot's fixed scalar type.

| Primitive | Children | Specific properties |
| --- | --- | --- |
| Stack, Grid | bounded ordered children | basic container only |
| Border, ScrollViewer | at most one child | basic container only |
| Items | bounded static children | no dynamic item source/template yet |
| Text, Button | none | Text |
| TextBox | none | Text, including two-way state binding |
| CheckBox | none | Text, Checked, including two-way Checked binding |
| ProgressBar | none | Number-valued Value and Maximum |

All nodes support Boolean `Enabled` and `Visible`. There is no image/resource support in this slice.
State supports Boolean, Int32, finite Number (F64), and well-formed UTF-16 String. Properties specify
exactly one literal, state slot, or derived kernel. Only explicitly marked TextBox.Text and
CheckBox.Checked state bindings accept two-way input. The initial event kind is Button.Click.

A `UiKernel` contains ordinary restricted IR JSON, a declared entrypoint, and an optional input
slot ID. It has no parameters when input is absent; otherwise it takes exactly one parameter of
that slot's type. It returns one scalar. Local events write that result into their output slot;
derived bindings project results into properties without mutating state. Kernels shared by multiple
properties are evaluated once per evaluation pass.

Every declared kernel is prepared by the existing `SandboxHost` with the host-selected
`SandboxPolicy`, then executed in interpreted mode with ordinary capability/effect validation,
fuel, host-call, allocation, string, collection, and deadline limits. A host can register explicit
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
and normalizes kernel JSON through the existing importer/exporter. `ComputeHash` hashes this
canonical UTF-8 with SHA-256. Equivalent definition ordering and kernel whitespace produce the
same hash; child order and initial state remain significant. This is format-v1 identity, not a
promise that future schema versions use identical bytes.

## State ordering and atomicity

The host issues a fresh random session ID on every install and owns authoritative state. Session
operations serialize admission, candidate validation, local execution, binding evaluation, commit,
and renderer update. Each accepted nonempty mutation increments the global state version once,
including writes of an unchanged value. Empty patches leave the version unchanged.

Remote events capture a consistent session ID/version/full bounded scalar snapshot. The remote
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
If an update fails after commit, the session disconnects and disposes its renderer; a disconnected
session cannot be used to read or mutate the partially rendered UI. Renderer adapters must marshal
to the toolkit UI thread, dispose subscriptions/controls, and queue semantic input without
synchronously reentering the session from Materialize/Update/Dispose callbacks.

## Quotas, cancellation, and connection ownership

`UiPolicy` independently bounds encoded package bytes, node count, depth, children per node,
state slots/bytes, text length, kernels and their bytes, events/endpoints, patch slots, and
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
disconnect the session; cancelling one call by its caller does not disconnect it. Invalid patches
are rejected without disconnecting by default.

## Validation and follow-ups

The [headless process sample](../samples/SandboxedUi/README.md) runs in Linux/Windows CI and proves
that the host does not load the worker implementation assembly. Required security-boundary tests
cover malformed imports, quotas, state atomicity, fuel faults, remote ordering/concurrency,
renderer failure, and teardown. A seeded adversarial import corpus supplements hand-written cases.

`UiBenchmarks` measures 100/1,000-node validation/import/headless installation, local button events,
two-way text, batched 100-slot patches, and over-limit rejection with allocation reporting:

```bash
dotnet run --project benchmarks/DotBoxD.Kernels.Benchmarks -c Release -- --filter '*UiBenchmarks*'
```

Remaining stages of #1453: Avalonia/thread/input/off-screen integration; component attributes and
existing C# lowering ergonomics; multiple-input/multiple-output kernel contracts; compiled-mode
selection retaining existing verifier requirements; keyed/dynamic lists and virtualization;
styles/themes/assets/images; broader renderer allocation benchmarks and development preview tooling.
