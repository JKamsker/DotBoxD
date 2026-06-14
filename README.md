# DotBoxD

> Source-generated, contract-first .NET extension runtime: **Services**, **Kernels**, **Pushdown**.

[![CI](https://github.com/JKamsker/DotBoxD/actions/workflows/ci.yml/badge.svg)](https://github.com/JKamsker/DotBoxD/actions/workflows/ci.yml)
[![CodeQL](https://github.com/JKamsker/DotBoxD/actions/workflows/codeql.yml/badge.svg)](https://github.com/JKamsker/DotBoxD/actions/workflows/codeql.yml)
[![NuGet](https://img.shields.io/nuget/v/DotBoxD.svg)](https://www.nuget.org/packages/DotBoxD)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4.svg)](https://dotnet.microsoft.com/)

DotBoxD lets a host and its clients share **one C# contract** and use it in three different ways,
all driven by Roslyn source generators (no runtime reflection on the hot path):

- **Services** — the host implements a contract; clients call it remotely over RPC.
- **Kernels** — a client supplies validated logic the host runs safely inside a metered sandbox.
- **Pushdown** — a kernel composes the host's own services *server-side*, so many small remote
  calls collapse into one validated round-trip.

The Services and channel libraries target `netstandard2.1`, so they run on **Unity / IL2CPP**.
The Kernels and Pushdown stack targets `net10.0`.

---

## The 3 ways to use one contract

All three snippets below are distilled from the runnable acceptance sample at
[`samples/Pushdown/DotBoxD.EndToEnd`](samples/Pushdown/DotBoxD.EndToEnd) — they use the real,
compiling API.

### 1. Services — define a contract, host it, call it remotely

```csharp
using DotBoxD.Services.Attributes;

// One contract, shared by host and client.
[DotBoxDService]
public interface ICatalogService
{
    ValueTask<int> GetUnitPriceAsync(string itemId, CancellationToken cancellationToken = default);
    ValueTask<CartTotal> ComputeCartTotalAsync(Cart cart, CancellationToken cancellationToken = default);
}
```

```csharp
using DotBoxD.Kernels.Transport.Ipc;   // IPC helper (ships in DotBoxD.Pushdown.Services)
using DotBoxD.Services.Generated;       // generated ProvideCatalogService / Get<T>

// Host: turn every accepted connection into a peer that serves the contract.
await using var host = DotBoxDDotBoxDRpcMessagePackIpc.ListenNamedPipe(
    pipeName,
    peer => peer.ProvideCatalogService(new CatalogService(prices)));
await host.StartAsync();

// Client: connect and get a strongly typed proxy — calls go over the wire.
await using var connection = await DotBoxDDotBoxDRpcMessagePackIpc.ConnectNamedPipeAsync(pipeName);
var catalog = connection.Get<ICatalogService>();

var unitPrice = await catalog.GetUnitPriceAsync("sword"); // one remote round-trip
```

The `[DotBoxDService]` attribute drives the `DotBoxD.Services.SourceGenerator`, which emits a typed
proxy, a dispatcher, and the `ProvideCatalogService(...)` / `Get<ICatalogService>()` extensions at
compile time. The GameService sample shows the same model over **TCP** with bidirectional callbacks
(see [`samples/Services/GameService`](samples/Services/GameService)).

### 2. Kernels — run validated logic under a policy

A kernel is restricted JSON IR (never C#, IL, or arbitrary host calls). The host imports it,
validates it against a capability/resource policy, and executes it inside a fuel-metered sandbox.

```csharp
using DotBoxD.Hosting;
using DotBoxD.Kernels;

// A sandbox host with only the safe, pure bindings enabled.
var host = SandboxHost.Create(builder =>
{
    builder.AddDefaultPureBindings();
    builder.UseInterpreter();
});

// A policy is a hard budget: fuel, loop iterations, list length, capability grants.
var policy = SandboxPolicyBuilder.Create()
    .WithFuel(1_000_000)
    .WithMaxLoopIterations(10_000)
    .WithMaxListLength(10_000)
    .Build();

var module = await host.ImportJsonAsync(kernelJson);
var plan = await host.PrepareAsync(module, policy);

var input = SandboxValue.FromList(
    [.. subtotals.Select(SandboxValue.FromInt32)],
    SandboxType.I32);

var result = await host.ExecuteAsync(plan, "main", input);

if (result.Succeeded && result.Value is I32Value total)
{
    // A buggy or hostile kernel cannot run away with host resources:
    Console.WriteLine($"total={total.Value}, fuel burned={result.ResourceUsage.FuelUsed}");
}
```

### 3. Pushdown — compose host services next to the data

A naive client makes one remote call per cart line, then sums the results. With pushdown, the host
exposes a **service method** — `ComputeCartTotalAsync`, part of the same `[DotBoxDService]` contract —
that composes its **own** catalog data and runs a validated **kernel** server-side, so the client
submits the whole cart in **one** round-trip.

> The method is *not itself* a kernel — it's an ordinary service method that **runs** one. The kernel is
> `CartTotalKernel` from step 2 (sandboxed JSON IR, validated + fuel-metered by `SandboxHost`); here it
> is invoked as `_kernel`.

```csharp
// Host side: a normal [DotBoxDService] method that runs the validated CartTotalKernel server-side.
public async ValueTask<CartTotal> ComputeCartTotalAsync(Cart cart, CancellationToken ct = default)
{
    // Host-side composition: turn each cart line into a subtotal using the host's own catalog.
    var subtotals = new int[cart.Lines.Length];
    for (var i = 0; i < cart.Lines.Length; i++)
        subtotals[i] = PriceOf(cart.Lines[i].ItemId) * cart.Lines[i].Quantity;

    // _kernel is the CartTotalKernel: the summation runs inside the metered sandbox, next to the service.
    var (total, fuelUsed) = await _kernel.RunAsync(subtotals, ct);
    return new CartTotal(total, fuelUsed);
}

// Client side: one submission instead of N price lookups.
var pushdown = await catalog.ComputeCartTotalAsync(cart);
// Round-trip win: 4 remote calls -> 1 (pushdown).
```

In the end-to-end sample, a 4-line cart that takes **4 remote calls** the naive way collapses into
**1 round-trip** with pushdown, and both paths produce the identical total (575).

---

## Quick start

```bash
# Full net10.0 stack (Services + Kernels + Pushdown):
dotnet add package DotBoxD

# Unity / netstandard2.1 service bundle:
dotnet add package DotBoxD.Services.All

# Preview pushdown IPC addon (prerelease while upstream deps are prerelease):
dotnet add package DotBoxD.Pushdown.Services --prerelease
```

Then read [`docs/getting-started`](docs/getting-started/) for first-service, first-kernel, and
pushdown walkthroughs, or run the acceptance sample:

```bash
dotnet run -c Release --project samples/Pushdown/DotBoxD.EndToEnd
```

---

## Architecture

```mermaid
flowchart LR
    Client["Client / Plugin"]
    Host["Host process"]

    subgraph Modes["One contract, three modes"]
        Services["Services<br/>RPC dispatch"]
        Kernels["Kernels<br/>metered IR sandbox"]
        Pushdown["Pushdown<br/>server-side composition"]
    end

    Client -->|"remote call"| Services
    Client -->|"submit validated IR"| Kernels
    Client -->|"one submission"| Pushdown

    Services --> Host
    Kernels --> Host
    Pushdown --> Kernels
    Pushdown --> Services

    subgraph Channels["Transports + Codecs"]
        Tcp["DotBoxD.Transports.Tcp"]
        Pipes["DotBoxD.Transports.NamedPipes"]
        MsgPack["DotBoxD.Codecs.MessagePack"]
    end

    Services --- Channels
    Pushdown --- Channels

    subgraph Runtime["Kernel runtime"]
        Validation["Validation"]
        Interp["Interpreter"]
        Compiler["Compiler + Verifier"]
    end

    Kernels --> Runtime
```

The generators (`DotBoxD.Services.SourceGenerator`, `DotBoxD.Plugins.Analyzer`) emit proxies,
dispatchers, and plugin factories at compile time. Diagnostics are namespaced `DBXS###` (services)
and `DBXK###` (kernels/plugins). See [`docs/index.md`](docs/index.md) for the full picture.

---

## Packages

| Package | Purpose | TFM | Stability |
|---------|---------|-----|-----------|
| `DotBoxD` | Meta-package: the full net10.0 stack (Services + Kernels + Pushdown) | net10.0 | Preview |
| `DotBoxD.Services.All` | Meta-package: service + Unity bundle | netstandard2.1 | Stable · **Unity/IL2CPP** |
| `DotBoxD.Services` | Contract attributes, `RpcPeer`/`RpcHost`, dispatch | netstandard2.1 | Stable · **Unity/IL2CPP** |
| `DotBoxD.Codecs.MessagePack` | MessagePack serializer for the wire format | netstandard2.1 | Stable · **Unity/IL2CPP** |
| `DotBoxD.Transports.Tcp` | TCP transport | netstandard2.1 | Stable · **Unity/IL2CPP** |
| `DotBoxD.Transports.NamedPipes` | Named-pipe transport (local IPC) | netstandard2.1 | Stable · **Unity/IL2CPP** |
| `DotBoxD.Services.SourceGenerator` | Roslyn generator for `[DotBoxDService]` proxies/dispatchers | netstandard2.0 | Stable |
| `DotBoxD.Abstractions` | Plugin-to-host authoring contracts (`[Plugin]`, `IEventKernel<TEvent>`) | net10.0 | Preview |
| `DotBoxD.Kernels` | IR model, policy model, resource metering, canonical hashing | net10.0 | Preview |
| `DotBoxD.Kernels.Validation` | Structural, type, effect, policy, binding validation | net10.0 | Preview |
| `DotBoxD.Kernels.Runtime` | Safe host bindings (files, time, random, logging, strings, math) | net10.0 | Preview |
| `DotBoxD.Kernels.Interpreter` | Direct IR execution backend | net10.0 | Preview |
| `DotBoxD.Kernels.Compiler` | Generated-runtime backend + persistent artifact cache | net10.0 | Preview |
| `DotBoxD.Kernels.Verifier` | Generated-assembly verifier | net10.0 | Preview |
| `DotBoxD.Kernels.Serialization.Json` | JSON IR importer/exporter + schema | net10.0 | Preview |
| `DotBoxD.Hosting` | Host-facing orchestration API (`SandboxHost`) | net10.0 | Preview |
| `DotBoxD.Hosting.Http` | HTTP GET binding, grant helpers, pinned transport | net10.0 | Preview |
| `DotBoxD.Plugins` | Host runtime that loads/validates/dispatches plugins | net10.0 | Preview |
| `DotBoxD.Plugins.Analyzer` | Generator + analyzer for local plugin packages | netstandard2.0 | Preview |
| `DotBoxD.Pushdown.Services` | MessagePack IPC addon that composes kernels with services | net10.0 | **Preview / prerelease** |

`DotBoxD.Pushdown.Services` is published on a **prerelease** channel while its upstream net10.0
dependencies are prerelease; stable release gates fail if it is included in a stable package set.

### Common namespaces & key types

After installing, these are the entry points you'll reach for:

- `DotBoxD.Services`: `[DotBoxDService]` contracts, `RpcPeer` / `RpcHost`, and the generated
  `Provide{Service}` / `Get<TService>()` wiring.
- `DotBoxD.Hosting`: `SandboxHost` — import, validate, prepare, and execute kernels under policy.
- `DotBoxD.Kernels.Serialization.Json`: JSON IR import **and export** round-trip via
  `DotBoxDJsonImporter` and `DotBoxDJsonExporter`.
- `DotBoxD.Pushdown.Services`: the MessagePack IPC bridge that runs kernels next to host services.

---

## Security: what is and isn't a boundary

DotBoxD is precise about its trust boundary — read this before deploying:

- **Safe mode is the real boundary.** A kernel is restricted IR that is validated, capability-gated,
  fuel/quota-metered, and (for compiled mode) verified before it runs. Users never supply C#, raw IL,
  CLR member names, assemblies, or arbitrary host calls.
- **Trusted-plugin mode is NOT a security boundary.** It loads normal .NET assemblies via
  `AssemblyLoadContext`, and **`AssemblyLoadContext` is not a sandbox** — loaded code has full CLR
  capabilities. Only use it for code you already trust.
- **Untrusted arbitrary .NET code must be out-of-process / OS-isolated.** In-process restrictions
  defend against accidental and many malicious-author attacks, but hard multi-tenant isolation
  requires a worker process, container, or OS-level boundary.

See [`SECURITY.md`](SECURITY.md) and [`docs/security`](docs/security/) for the threat model, the
three execution modes, and the capabilities/bindings model.

---

## Status & roadmap

DotBoxD merges the former standalone ShaRPC (RPC) and Safe-IR (kernel sandbox) repositories into one
contract-first runtime. The net10.0 Kernels/Pushdown stack is **preview**; the netstandard2.1
Services/channel stack is the more mature surface. Deferred work and known gaps are tracked in
[`docs/architecture/follow-up-issues.md`](docs/architecture/follow-up-issues.md).

## Contributing

Build, test, and the CI gate list live in [`CONTRIBUTING.md`](CONTRIBUTING.md). In short:

```bash
dotnet build DotBoxD.slnx -c Release
dotnet test  DotBoxD.slnx -c Release
```

Please read the [Code of Conduct](CODE_OF_CONDUCT.md). For how to view pre-merge history of the two
original repos, see
[`docs/contributing/migration-from-standalone-repos.md`](docs/contributing/migration-from-standalone-repos.md).

## License

DotBoxD is [MIT licensed](LICENSE). It preserves the attribution of both original projects:
**Copyright (c) 2026 Danial Jumagaliyev** (ShaRPC, the Services/channels stack) and
**Copyright (c) 2026 Jonas Kamsker** (Safe-IR / DotBoxD, the Kernels/Pushdown stack).
