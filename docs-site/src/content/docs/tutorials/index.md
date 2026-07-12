---
title: 'Tutorials'
description: >-
  Three end-to-end tracks — call the host (Services), react to the host (event pipelines), extend
  the host (Pushdown) — plus an advanced hand-written IR path.
---
Four walkthroughs, in recommended order. Tutorials 1–3 map one-to-one onto DotBoxD's three modes —
*call*, *react*, *extend* — and each builds on vocabulary from the one before. Tutorial 4 is the
advanced, no-generator path.

| # | Tutorial | You build | Starts from |
|---|----------|-----------|-------------|
| 1 | [First Service (RPC)](/tutorials/first-service/) — *call the host* | A contract, a host, and a client with a generated typed proxy | An empty project |
| 2 | [Event pipelines (RunLocal)](/tutorials/event-pipeline-runlocal/) — *react to the host* | A plugin whose `Where`/`Select` filter runs server-side | The [GameServer sample](https://github.com/JKamsker/DotBoxD/tree/main/samples/GameServer) (**clone the repo**) |
| 3 | [Pushdown server extension](/tutorials/pushdown-server-extension/) — *extend the host* | A plugin-shipped, server-side batch operation | The GameServer sample (**clone the repo**) |
| 4 | [Hand-written IR hook pipeline](/tutorials/handwritten-ir-hook-pipeline/) — *advanced* | The same shapes from public primitives, with no generator | Public primitives; snippets use GameServer sample types (read along) |

> **Clone first for 2–4.** Tutorial 1 builds from an empty project. Pushdown and event pipelines
> instead run against the maintained
> [GameServer sample](https://github.com/JKamsker/DotBoxD/tree/main/samples/GameServer):
> Pushdown's final step installs into the sample's `PluginServer`, and the event-pipeline snippets
> reference sample-only types that will not compile in an empty project — so clone the repo and
> read/run along.

## Why each tutorial exists

1. **[First Service (RPC)](/tutorials/first-service/)** — define a `[RpcService]` contract, host it, and
   call it from a client over a typed proxy. *Why this mode:* easy interop — one C# contract compiles
   to a typed proxy + dispatcher, so there is no hand-written marshaling and no runtime reflection on
   the hot path; the interface is the single source of truth. Unity/IL2CPP additionally requires
   generated codec formatters and validation in the consumer's own build.
2. **[Event pipelines (RunLocal)](/tutorials/event-pipeline-runlocal/)** — subscribe to a server event, push the
   `Where`/`Select` filter server-side, and react locally with `RunLocal` so only matching, projected
   data crosses the pipe. *Why this mode:* efficient server-side filtering + projection — `Where`/`Select`
   lower to verified restricted IR (intermediate representation), so only matching, projected values
   cross the pipe (fewer bytes, fewer wake-ups, one-way push, no round-trips), and that filter logic is
   safe to accept from untrusted plugins because it runs as validated, fuel-metered IR.
3. **[Pushdown server extension](/tutorials/pushdown-server-extension/)** — ship a plugin-supplied, server-side
   batch operation that collapses N remote calls into one. *Why this mode:* reduce round-trips — move the
   loop/aggregation next to the data (N calls → 1 server-side batch); the host stays frozen/minimal while
   plugins add batch ops without recompiling it, and the batch runs as verified, capability-gated,
   fuel-metered IR.
4. **[Hand-written IR hook pipeline](/tutorials/handwritten-ir-hook-pipeline/)** — build or load a
   `PluginPackage`, install it under policy, and wire it to hooks/subscriptions with public primitives.
   *Why this path:* no lock-in — use it when another language, build step, or custom fluent API emits the IR
   instead of DotBoxD's generator. Skip it until you need it.

New to the project? Read [Getting started](/getting-started/) first, then come back here.
For the mental model behind the three modes, see [What is DotBoxD?](/overview/), and for the
deep-dives, the [concept pages](/concepts/services/).
