# Schemas reference

Kernel and plugin payloads are validated against versioned JSON Schemas, which are also embedded in the
relevant NuGet packages and regression-tested for drift against the importer.

| Schema | File | Accepted by |
|--------|------|-------------|
| Kernel module envelope | [`schemas/v1/dotboxd-kernel-module.schema.json`](../../schemas/v1/dotboxd-kernel-module.schema.json) | the JSON IR importer in `DotBoxD.Kernels.Serialization.Json` |
| Plugin package envelope | [`schemas/v1/dotboxd-plugin-package.schema.json`](../../schemas/v1/dotboxd-plugin-package.schema.json) | the plugin-package importer in `DotBoxD.Plugins` |

The schemas are a versioned contract (the `v1/` directory). A regression test keeps each schema in sync
with the code that consumes it, so a schema change that diverges from the importer fails CI.
