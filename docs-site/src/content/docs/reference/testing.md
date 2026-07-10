---
title: 'Consumer testing kit'
description: 'Public deterministic test primitives for RPC, sandbox bindings, audit, and contract compatibility.'
---

DotBoxD ships its test seams as public primitives. Consumer tests do not need real sockets, copied
internal channels, or reflection into a host.

| Scenario | Public primitive |
|---|---|
| End-to-end RPC without sockets | `InMemoryRpcChannel.CreatePair(capacity)` |
| Delay, cancellation, timeout, disconnect, and send failure | `FaultInjectingRpcChannel` operation hook |
| Truncated or otherwise malformed outbound frames | `FaultInjectingRpcChannel` send transform |
| Sandbox quota and capability assertions | a hand-written `BindingDescriptor` plus a restrictive `SandboxPolicy` |
| Audit assertions | `InMemoryAuditSink.Events` and `EventsWritten` |
| Generated contract compatibility | `RpcContractManifest.Diff` and `EnsureCompatibleWith` |

## Deterministic RPC and malformed responses

Create a pair and wrap either endpoint. Operation numbers start at one independently for send and
receive, so a fault plan is replayable:

```csharp
var (serverChannel, rawClientChannel) = InMemoryRpcChannel.CreatePair(capacity: 4);
var clientChannel = new FaultInjectingRpcChannel(
    rawClientChannel,
    beforeOperation: (operation, number, cancellationToken) =>
        operation == RpcChannelOperation.Receive && number == 2
            ? ValueTask.FromException(new TimeoutException("planned timeout"))
            : ValueTask.CompletedTask,
    transformSend: (frame, number, cancellationToken) =>
        number == 3
            ? ValueTask.FromResult(frame[..Math.Max(0, frame.Length - 1)])
            : ValueTask.FromResult(frame));
```

Dispose either endpoint to model disconnect. Pass a cancelled token to exercise cancellation. Use a
small capacity and a stalled peer for bounded-queue behavior. The transform receives the original
frame as read-only memory; return a new/truncated view, never mutate pooled memory.

## Recording sandbox audit and fake bindings

`InMemoryAuditSink` is thread-safe and returns owned snapshots. Configure it through the normal host
builder, execute, then assert exact effect, capability, error, byte, and run-summary fields. For host
behavior, add a narrow `BindingDescriptor` whose delegate returns fixed values or throws a planned
exception. See [Host bindings](/concepts/host-bindings/#explicit-binding-route) for the full
descriptor example. Keeping the fake behind the same public binding boundary also tests capability,
effect, and cost enforcement.

## Contract compatibility assertions

Persist `RpcContractManifest.Serialize()` as a CI artifact or reviewed baseline. The current generated
assembly can fail a test on removed methods, changed defaults/wire/streaming shapes, recursive DTO
member changes, or unsupported manifest versions:

```csharp
var previous = LoadReviewedManifest();
var current = RpcContractManifest.Create(typeof(IMyService).Assembly);

current.EnsureCompatibleWith(previous);
```

Use `Diff` when additions and approved breaks need custom policy. `RpcContractChange.IsBreaking`
distinguishes additive methods from removals, signature/DTO changes, and unsupported versions. The
`Fingerprint` can also participate in an application-level readiness exchange; it is optional sugar,
not a replacement for the public wire primitives.
