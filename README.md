# DotBoxD

> A source-generated, contract-first **plugin system for .NET hosts** — Services (RPC), Kernels (sandboxed logic), Pushdown (server-side batching).

[![CI](https://github.com/JKamsker/DotBoxD/actions/workflows/ci.yml/badge.svg)](https://github.com/JKamsker/DotBoxD/actions/workflows/ci.yml)
[![CodeQL](https://github.com/JKamsker/DotBoxD/actions/workflows/codeql.yml/badge.svg)](https://github.com/JKamsker/DotBoxD/actions/workflows/codeql.yml)
[![NuGet packages](https://img.shields.io/badge/NuGet-packages-004880.svg?logo=nuget)](https://www.nuget.org/packages?q=DotBoxD)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4.svg)](https://dotnet.microsoft.com/)
[![Docs](https://img.shields.io/badge/docs-online-2ea44f.svg)](https://dotboxd.kamsker.at/)

**For .NET hosts — game servers, desktop apps, backend services — that must run untrusted
third-party plugins safely.** The Services stack targets `netstandard2.1`; Unity/IL2CPP deployments
must use generated MessagePack DTO formatters and validate their own IL2CPP build. The Kernels and
Pushdown stack targets `net10.0`.

## The problem

.NET cannot safely isolate arbitrary code **in-process**: AppDomains are gone, and
`AssemblyLoadContext` is a loading feature, **not** a security boundary. The only thing the OS can
truly isolate is a whole *process*, so the safe way to run untrusted plugins is an unprivileged
**sidecar** behind IPC. That safe design imposes two costs: **round-trips** — every interaction
crosses the process boundary, so an N-item loop pays latency × N — and the **filter-API dilemma** —
the host vendor (who is *not* the plugin author) has to design an event-filter API blind, and it is
always either too coarse (oversharing plus overhead) or too exhaustive (endless unused options, still
not expressive enough). If your .NET host must accept third-party plugins safely, you pay both costs.

## The answer

DotBoxD is a **complete plugin system for .NET hosts** that keeps the process boundary but gives
plugin *logic* a safe way back into the host: a validated, capability-gated, fuel-metered in-process
sandbox that runs restricted IR (intermediate representation) — never loaded C#/IL. Author-written
filters, projections, and batches are compiled down to that IR at build time and run next to the
host's data, so:

- **Event pipelines push only server-side filtered/projected data** — 0 round-trips.
- **Pushdown collapses N calls into 1.**
- **The plugin author — not the vendor — writes the filter.**

It also ships the rest of the plugin-system ceremony (transports, codecs, generated
proxies/dispatchers, registries, lifecycle such as unload-on-disconnect), all driven by Roslyn source
generators with no runtime reflection on the hot path.

📖 **Start here:** [**Why DotBoxD?** — the isolation-vs-latency dilemma in three diagrams](https://dotboxd.kamsker.at/why-dotboxd/).
Full guide, tutorials, examples, and the generated API reference live at <https://dotboxd.kamsker.at/>.

**How:** one C# contract, delivered three ways.

- **Services** — the host implements a contract; clients call it remotely over RPC. A typed proxy and
  dispatcher are generated from the same interface, so they cannot drift; AOT deployments also need a
  generated/static codec resolver.
- **Kernels** — a client supplies validated logic the host runs safely inside a fuel-metered sandbox.
  Downstream, this event-pipeline flavor is called *Query (RunLocal)*.
- **Pushdown** — a plugin ships its *own* sandboxed batch operation that runs *server-side*, looping
  over the host's existing fine-grained bindings so many small remote calls collapse into one
  round-trip.

---

## The three ways to use one contract

The snippets below use the real, compiling API. The maintained runnable example is the GameServer
sample at [`samples/GameServer/Examples.GameServer.Server`](samples/GameServer/Examples.GameServer.Server),
which combines service IPC, event kernels, live settings, host bindings, policies, and server extensions.
Features that used to be split across removed samples are tracked in
[the examples coverage-gaps page](https://dotboxd.kamsker.at/examples/coverage-gaps/).

### 1. Services — define a contract, host it, call it remotely

```csharp
using DotBoxD.Services.Attributes;

// One contract, shared by host and client.
[RpcService]
public interface ICatalogService
{
    ValueTask<int> GetUnitPriceAsync(string itemId, CancellationToken cancellationToken = default);
    ValueTask<CartTotal> ComputeCartTotalAsync(Cart cart, CancellationToken cancellationToken = default);
}
```

```csharp
using DotBoxD.Pushdown.Services;       // IPC helper
using DotBoxD.Services.Generated;       // generated ProvideCatalogService / Get<T>

// Host: turn every accepted connection into a peer that serves the contract.
await using var host = RpcMessagePackIpc.ListenNamedPipe(
    pipeName,
    peer => peer.ProvideCatalogService(new CatalogService(prices)));
await host.StartAsync();

// Client: connect and get a strongly typed proxy — calls go over the wire.
await using var connection = await RpcMessagePackIpc.ConnectNamedPipeAsync(pipeName);
var catalog = connection.Get<ICatalogService>();

var unitPrice = await catalog.GetUnitPriceAsync("sword"); // one remote round-trip
```

The `[RpcService]` attribute drives the `DotBoxD.Services.SourceGenerator`, which emits a typed
proxy, a dispatcher, and the `ProvideCatalogService(...)` / `Get<ICatalogService>()` extensions at
compile time.

### 2. Kernels — run validated logic under a policy

A kernel is restricted JSON IR (never C#, IL, or arbitrary host calls). The host imports it,
validates it against a capability/resource policy, and executes it inside a fuel-metered sandbox.
Hosts can still expose their own APIs deliberately through policy-gated host bindings; see
[Host bindings](https://dotboxd.kamsker.at/concepts/host-bindings/).

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

### 3. Pushdown — plugins ship server-side batch operations

This is the payoff. The host is typically **frozen at release** and exposes only fine-grained
bindings (e.g. "kill *one* monster"); it ships no batch operations. A client that needs to act on
many entities would otherwise make one remote call per entity. With pushdown, a plugin supplies its
own server-side aggregate as a sandboxed server extension: the analyzer lowers its C# batch method
to verified IR that runs server-side, looping over the host's *existing* bindings. The server is
never recompiled — only the plugin changes — and **N round-trips collapse into one**.

```csharp
// The host (frozen at release) exposes only a fine-grained binding — there is NO batch method here.
public interface IGameWorld
{
    [HostBinding("host.world.kill", "game.world.monster.write.kill",
                 SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
    bool Kill(int id);
}

// A PLUGIN adds its own batch aggregate. `KillMonsters` does not exist on the host — the plugin ships it.
// The analyzer lowers this method to verified, capability-gated, fuel-metered IR (a sandboxed kernel).
public interface IMonsterKillerService { List<KillResult> KillMonsters(List<int> monsterIds); }
public readonly record struct KillResult(int MonsterId, bool Success);

[ServerExtension("monster-killer", typeof(IMonsterKillerService))]
public sealed partial class MonsterKillerKernel
{
    public List<KillResult> KillMonsters(List<int> monsterIds, HookContext ctx)
    {
        var results = new List<KillResult>();
        foreach (var id in monsterIds)
            results.Add(new KillResult(id, ctx.Host<IGameWorld>().Kill(id))); // calls the host's existing binding
        return results;
    }
}

// Server installs the plugin's kernel; the caller invokes it in ONE round-trip:
await server.RegisterServerExtensionAsync<IMonsterKillerService, MonsterKillerKernel>();
List<KillResult> killed = server.ServerExtension<IMonsterKillerService>().KillMonsters(ids); // 1 round-trip, not N
```

The batch logic is **author-supplied**, so it runs as a validated sandboxed kernel under the same trust
model as event kernels: it can reach only the host bindings the server already exposes, gated by
capabilities and fuel/quota limits, and it can take and return complex objects and lists of objects
(via the IR `Record` type). The GameServer sample demonstrates server extensions over the plugin IPC
control plane; see
[`docs/design/plugin-fluent-hooks-api/followups.md`](docs/design/plugin-fluent-hooks-api/followups.md)
for the full design.

---

## Quick start

```bash
# Full net10.0 stack (Services + Kernels + Pushdown):
dotnet add package DotBoxD --prerelease

# Unity / netstandard2.1 service bundle:
dotnet add package DotBoxD.Services.All --prerelease
```

Then read [Getting started](https://dotboxd.kamsker.at/getting-started/) for first-service, first-kernel, and
pushdown walkthroughs, or run the GameServer sample:

```bash
dotnet run -c Release --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj
```

---

## Installing from NuGet

To pull individual packages instead of a meta-package, add only the pieces you need. Main-branch CI
packages are published as `0.1.0-ci.*` prereleases; omit `--prerelease` once you target a stable tag
release.

```bash
# Host orchestration (SandboxHost: import, prepare, execute kernels under policy):
dotnet add package DotBoxD.Hosting --prerelease

# Safe host runtime bindings (files, time, random, logging, strings, math):
dotnet add package DotBoxD.Kernels.Runtime --prerelease

# JSON IR import/export round trip (JsonImporter / JsonExporter):
dotnet add package DotBoxD.Kernels.Serialization.Json --prerelease

# HTTP GET binding, grant helpers, and pinned-transport policy validation:
dotnet add package DotBoxD.Hosting.Http --prerelease

# Plugin authoring contracts ([Plugin], IEventKernel<TEvent>, HookContext):
dotnet add package DotBoxD.Abstractions --prerelease

# Host runtime that loads, validates, and dispatches plugins:
dotnet add package DotBoxD.Plugins --prerelease

# Source generator + analyzer that turns [Plugin] kernels into package-backed plugins:
dotnet add package DotBoxD.Plugins.Analyzer --prerelease

# Preview pushdown IPC addon:
dotnet add package DotBoxD.Pushdown.Services --prerelease
```

After installing `DotBoxD.Plugins`, load a built plugin package with `PluginPackageJsonSerializer`,
which deserializes the plugin-package JSON envelope (manifest + module) so the host can install it.

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
and `DBXK###` (kernels/plugins). See [the docs overview](https://dotboxd.kamsker.at/overview/) for the full picture.

---

## Packages

| Package | Purpose | TFM | Stability |
|---------|---------|-----|-----------|
| [`DotBoxD`](https://www.nuget.org/packages/DotBoxD) | Meta-package: the full net10.0 stack (Services + Kernels + Pushdown) | net10.0 | Preview |
| [`DotBoxD.Services.All`](https://www.nuget.org/packages/DotBoxD.Services.All) | Meta-package: service + channel bundle | netstandard2.1 | Stable API · AOT configuration required |
| [`DotBoxD.Services`](https://www.nuget.org/packages/DotBoxD.Services) | Contract attributes, `RpcPeer`/`RpcHost`, dispatch, and bundled source generator | netstandard2.1 | Stable API |
| [`DotBoxD.Codecs.MessagePack`](https://www.nuget.org/packages/DotBoxD.Codecs.MessagePack) | MessagePack serializer for the wire format | netstandard2.1 | Stable API · generated resolver required for AOT |
| [`DotBoxD.Transports.Tcp`](https://www.nuget.org/packages/DotBoxD.Transports.Tcp) | TCP transport | netstandard2.1 | Stable API |
| [`DotBoxD.Transports.NamedPipes`](https://www.nuget.org/packages/DotBoxD.Transports.NamedPipes) | Named-pipe transport (local IPC) | netstandard2.1 | Stable API |
| [`DotBoxD.Abstractions`](https://www.nuget.org/packages/DotBoxD.Abstractions) | Plugin-to-host authoring contracts (`[Plugin]`, `IEventKernel<TEvent>`) | net10.0 | Preview |
| [`DotBoxD.Kernels`](https://www.nuget.org/packages/DotBoxD.Kernels) | IR model, policy model, resource metering, canonical hashing | net10.0 | Preview |
| [`DotBoxD.Kernels.Validation`](https://www.nuget.org/packages/DotBoxD.Kernels.Validation) | Structural, type, effect, policy, binding validation | net10.0 | Preview |
| [`DotBoxD.Kernels.Runtime`](https://www.nuget.org/packages/DotBoxD.Kernels.Runtime) | Safe host bindings (files, time, random, logging, strings, math) | net10.0 | Preview |
| [`DotBoxD.Kernels.Interpreter`](https://www.nuget.org/packages/DotBoxD.Kernels.Interpreter) | Direct IR execution backend | net10.0 | Preview |
| [`DotBoxD.Kernels.Compiler`](https://www.nuget.org/packages/DotBoxD.Kernels.Compiler) | Generated-runtime backend + persistent artifact cache | net10.0 | Preview |
| [`DotBoxD.Kernels.Verifier`](https://www.nuget.org/packages/DotBoxD.Kernels.Verifier) | Generated-assembly verifier | net10.0 | Preview |
| [`DotBoxD.Kernels.Serialization.Json`](https://www.nuget.org/packages/DotBoxD.Kernels.Serialization.Json) | JSON IR importer/exporter + schema | net10.0 | Preview |
| [`DotBoxD.Hosting`](https://www.nuget.org/packages/DotBoxD.Hosting) | Host-facing orchestration API (`SandboxHost`) | net10.0 | Preview |
| [`DotBoxD.Hosting.Http`](https://www.nuget.org/packages/DotBoxD.Hosting.Http) | HTTP GET binding, grant helpers, pinned transport | net10.0 | Preview |
| [`DotBoxD.Plugins`](https://www.nuget.org/packages/DotBoxD.Plugins) | Host runtime that loads/validates/dispatches plugins | net10.0 | Preview |
| [`DotBoxD.Plugins.Analyzer`](https://www.nuget.org/packages/DotBoxD.Plugins.Analyzer) | Generator + analyzer for local plugin packages | netstandard2.0 | Preview |
| [`DotBoxD.Pushdown.Services`](https://www.nuget.org/packages/DotBoxD.Pushdown.Services) | MessagePack IPC addon that composes kernels with services | net10.0 | **Preview / prerelease** |

`DotBoxD.Pushdown.Services` is published on a **prerelease** channel while its upstream net10.0
dependencies are prerelease; stable release gates fail if it is included in a stable package set.
`DotBoxD.Services.SourceGenerator` is bundled inside `DotBoxD.Services` as an analyzer asset, not
published as a standalone package.

### Common namespaces & key types

After installing, these are the entry points you'll reach for:

- `DotBoxD.Services`: `[RpcService]`, `RpcPeer` / `RpcHost`, and the generated `Provide{Service}` /
  `Get<TService>()` wiring.
- `DotBoxD.Hosting`: `SandboxHost`.
- `DotBoxD.Kernels.Serialization.Json`: `JsonImporter` / `JsonExporter`.
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

See [`SECURITY.md`](SECURITY.md) and [Sandbox caveats](https://dotboxd.kamsker.at/security/sandbox-caveats/) for the threat model,
the three execution modes, and the capabilities/bindings model.

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
[Migration from standalone repos](https://dotboxd.kamsker.at/contributing/migration-from-standalone-repos/).

## License

DotBoxD is [MIT licensed](LICENSE). It preserves the attribution of both original projects:
**Copyright (c) 2026 Danial Jumagaliyev** (ShaRPC, the Services/channels stack) and
**Copyright (c) 2026 Jonas Kamsker** (Safe-IR / DotBoxD, the Kernels/Pushdown stack).
