---
title: 'Getting started'
description: >-
  Install DotBoxD, make your first typed RPC call in about ten minutes, run the full GameServer
  sample, and pick the learning track that matches what you're building.
---
DotBoxD in one line: **one C# contract, delivered three ways** — *call* the host (Services/RPC),
*react* to the host (event pipelines), *extend* the host (Pushdown) — with untrusted plugin logic
running in a validated kernel sandbox. New to the mental model? Skim
[What is DotBoxD?](/overview/) first — it's a two-minute read.

## Prerequisites

- .NET SDK **10.0.2xx** (pinned in `global.json`). The test suite also exercises the **.NET 8** and
  **.NET 9** runtimes, so install those runtimes if you intend to run all tests.
- Any OS: Windows, Linux, macOS.

## Install

```bash
# Full net10.0 stack — Services, the kernel sandbox, and Pushdown:
dotnet add package DotBoxD --prerelease

# Service / Unity (netstandard2.1) bundle only:
dotnet add package DotBoxD.Services.All --prerelease
```

> `--prerelease` is required while the net10.0 stack is in preview; drop it once a stable tag ships.

Or reference individual packages — see the package tables in the root
[README](https://github.com/JKamsker/DotBoxD/blob/main/README.md).

## First win: a typed RPC call in three steps

The fastest end-to-end result is mode 1, **Services (RPC)** — no sandbox, no sample repo, just one
interface and two processes. Two console apps (`dotnet new console`) with the `DotBoxD` package
added is all the setup this needs; [Tutorial 1](/tutorials/first-service/) does the same thing step
by step with full project layout. Prefer a ready-made project? The repo ships
[`dotnet new` templates](https://github.com/JKamsker/DotBoxD/tree/main/templates)
(`dotboxd-service`, `dotboxd-sidecar`, `dotboxd-kernel-host`) you can install from a checkout.

**Step 1 — define one contract**, shared by host and client:

```csharp
using DotBoxD.Services.Attributes;

[RpcService]
public interface ICatalogService
{
    ValueTask<int> GetUnitPriceAsync(string itemId, CancellationToken cancellationToken = default);
    ValueTask<CartTotal> ComputeCartTotalAsync(Cart cart, CancellationToken cancellationToken = default);
}
```

**Step 2 — host it.** Implement the interface and serve it on every accepted connection:

```csharp
using DotBoxD.Pushdown.Services;        // RpcMessagePackIpc helper (full-stack meta-package)
using DotBoxD.Services.Generated;       // generated ProvideCatalogService / Get<T>

await using var host = RpcMessagePackIpc.ListenNamedPipe(
    pipeName,
    peer => peer.ProvideCatalogService(new CatalogService(prices)));
await host.StartAsync();
```

**Step 3 — call it** from the client through the generated typed proxy:

```csharp
await using var connection = await RpcMessagePackIpc.ConnectNamedPipeAsync(pipeName);
var catalog = connection.Get<ICatalogService>();

var unitPrice = await catalog.GetUnitPriceAsync("sword"); // one remote round-trip
```

The `[RpcService]` attribute drives a source generator that emits the proxy, the dispatcher, and
the `ProvideCatalogService` / `Get<ICatalogService>()` wiring at compile time — no hand-written
marshaling, no runtime reflection on the hot path.

For the full version — project layout, MessagePack DTOs, the generated pieces, diagnostics, and
the explicit netstandard2.1/Unity setup — work through
[Tutorial 1: your first Service](/tutorials/first-service/).

## See all three modes running

The maintained GameServer sample ties everything together: service IPC, event kernels, live
settings, host bindings, policy-gated execution, server extensions (Pushdown), and
unload-on-disconnect. It lives in the repo, not the NuGet package — clone and run from the repo
root:

```bash
git clone https://github.com/JKamsker/DotBoxD
cd DotBoxD
dotnet run -c Release --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj
```

You should see three phases print — a baseline run, a with-plugins run, and a summary confirming
the plugin's kernels unloaded on disconnect. For the annotated output, see
[What the run prints](/examples/gameserver-walkthrough/#what-the-run-prints). Features no longer
covered by maintained samples are listed in [coverage gaps](/examples/coverage-gaps/).

## Pick your track

| You're building… | Do this next |
|------------------|--------------|
| **Typed RPC between processes** (services, desktop apps, Unity clients) | [Tutorial 1: your first Service](/tutorials/first-service/), then the [RPC & transports guide](/channels/quick-start/) — [Unity integration](/channels/unity-integration/), [transports](/concepts/channels-transports/), [performance](/channels/performance/). |
| **A host whose plugins react to events** (only matching data should leave the host) | [Tutorial 2: event pipelines](/tutorials/event-pipeline-runlocal/) (**clone the repo** — it builds on the GameServer sample), then the [event pipelines concept](/concepts/event-pipelines/) for Hooks vs Subscriptions and all five terminals. |
| **Plugins that ship server-side batch operations** against a host frozen at release | [Tutorial 3: Pushdown server extension](/tutorials/pushdown-server-extension/) (**clone the repo**), then the [Pushdown concept](/concepts/pushdown/) and [host bindings](/concepts/host-bindings/). |
| **Your own tooling on the raw sandbox** (hand-written IR, another language, custom fluent APIs) | The [kernels concept](/concepts/kernels/), the raw `SandboxHost` example in the [README](https://github.com/JKamsker/DotBoxD/blob/main/README.md), a real [JSON-IR example](https://github.com/JKamsker/DotBoxD/blob/main/docs/Specs/Initial/dotboxd-sandbox-spec/examples/example-ir.md), then [Tutorial 4: hand-written IR](/tutorials/handwritten-ir-hook-pipeline/) and the [schemas](/reference/schemas/). |
| **A security review** before running untrusted plugins | [Sandbox caveats](/security/sandbox-caveats/) — what is and isn't a boundary — plus the [kernel runtime](/concepts/runtime/) for fuel, quotas, and capabilities. |

Unsure which mode fits a call site? Use the decision table in
[Choosing a mode](/overview/#choosing-a-mode).

## Words you'll keep meeting

**Kernel** (the sandboxed unit of plugin logic), **IR** (the restricted instruction format kernels
are written in), **lowering** (compiling your C# down to IR), **fuel** (the execution budget),
**capability** (permission for a kernel to touch a host binding), **terminal** (the last stage of
an event pipeline — where the reaction runs). All defined with links in the
[glossary](/reference/glossary/).

## Next steps

Ready for an end-to-end walkthrough? Work through the [tutorials](/tutorials/) — one per mode, in
order.
