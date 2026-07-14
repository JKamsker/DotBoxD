---
title: 'API reference'
description: >-
  Generated API reference for every published DotBoxD package. The per-type pages are produced from
  source by DocFX during deployment.
editUrl: false
prev: false
next: false
---
The API reference is **generated from source** (via `dotnet docfx metadata` plus
`docs-site/scripts/postprocess-api.mjs`) when the site is deployed, so the published site at
[dotboxd.kamsker.at/api/](https://dotboxd.kamsker.at/api/) always lists every namespace and type
of every published package.

If you are reading a locally built, content-only version of this site, the per-type pages are
absent. To generate them locally, run from the repository root:

```bash
dotnet tool restore
dotnet docfx metadata docfx.json
```

then, inside `docs-site/`:

```bash
npm run postprocess-api
npm run build
```

Until then, these are the entry points you will reach for most often:

- `DotBoxD.Services` - `[RpcService]`, `RpcPeer` / `RpcHost`, and the generated
  `Provide{Service}` / `Get<TService>()` wiring.
- `DotBoxD.Hosting` - `SandboxHost` (import, prepare, execute kernels under policy).
- `DotBoxD.Kernels.Serialization.Json` - `JsonImporter` / `JsonExporter`.
- `DotBoxD.Pushdown.Services` - the MessagePack IPC bridge that runs kernels next to host services.

The package-to-purpose map lives in the root
[README](https://github.com/JKamsker/DotBoxD/blob/main/README.md).
