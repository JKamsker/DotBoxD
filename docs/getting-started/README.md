# Getting started

## Prerequisites

- .NET SDK **10.0.2xx** (pinned in `global.json`). The test suite also exercises the **.NET 8** and
  **.NET 9** runtimes, so install those runtimes if you intend to run all tests.
- Any OS: Windows, Linux, macOS.

## Install

```bash
# Full net10.0 stack (Services + Kernels + Pushdown):
dotnet add package DotBoxd

# Service / Unity (netstandard2.1) bundle only:
dotnet add package DotBoxd.Services.All
```

Or reference individual packages — see the table in the root [README](../../README.md).

## First Service (RPC)

1. Define a contract and annotate it with `[DotBoxdService]`.
2. Implement it on the host and `Provide…` it on each accepted peer.
3. Connect from the client and call the generated typed proxy.

The complete, compiling pattern is in [`samples/Services/GameService`](../../samples/Services/GameService)
(TCP) and in the [end-to-end sample](../../samples/Pushdown/DotBoxd.EndToEnd) (named pipes). See
[concepts/services.md](../concepts/services.md).

## First Kernel (sandbox)

1. Create a `SandboxHost` with the bindings you want to expose.
2. Build a `SandboxPolicy` (fuel, loop, list, capability budgets).
3. Import the kernel JSON IR, `PrepareAsync`, then `ExecuteAsync`.

See [`samples/Kernels`](../../samples/Kernels) and [concepts/kernels.md](../concepts/kernels.md).

## Pushdown quickstart

Expose a contract method that composes host data and runs a validated kernel server-side, so the client
submits work in one round-trip instead of N. See [`samples/Pushdown`](../../samples/Pushdown) and
[concepts/pushdown.md](../concepts/pushdown.md).

## Run the acceptance sample

```bash
dotnet run -c Release --project samples/Pushdown/DotBoxd.EndToEnd
```

It demonstrates all three modes and prints the round-trip win.
