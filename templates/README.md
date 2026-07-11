# DotBoxD project templates

Install one template directly from a checkout, then create a project:

```powershell
dotnet new install ./templates/service-only
dotnet new dotboxd-service -n MyService
```

The templates intentionally stay small enough to act as debugging oracles:

- `dotboxd-service`: generated service proxy/dispatcher over the shipped in-memory test channel.
- `dotboxd-sidecar`: named-pipe sidecar host with an unguessable local pipe name.
- `dotboxd-kernel-host`: hand-written public IR prepared and executed by `SandboxHost`.

Package version range `0.1.0-*` follows the current preview line in `Directory.Build.props`; update
template package references whenever the repository version line changes.
