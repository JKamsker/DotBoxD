# Changelog

## Unreleased

- Peer wait-mode inbound queues now bound retained request frames instead of staging
  excess requests in an unbounded intake queue.
- TCP tests and callers can bind `TcpServerTransport` to port `0` and read the assigned
  port from `LocalEndpoint` after start.
- `RpcPeerOptions.InboundQueueCapacity`, `ShaRpcPeerOptions.InboundQueueCapacity`, and
  `DuplexConnectionSplitterOptions.QueueCapacity` docs now call out that `null` means
  unbounded queues and should be reserved for trusted or externally bounded peers.
- Server-side exceptions that are not `ShaRpcException` now return a sanitized
  `Internal error.` / `ShaRpcInternalError` error payload instead of exposing the raw
  exception message and CLR exception type to remote callers.
