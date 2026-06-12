# Release Readiness Checklist

## MVP release

- [ ] Restricted IR implemented.
- [ ] Canonical hashing implemented.
- [ ] Type checker implemented.
- [ ] Effect analyzer implemented.
- [ ] Capability policy implemented.
- [ ] Binding registry validation implemented.
- [ ] Interpreted mode implemented.
- [ ] Fuel limits implemented.
- [ ] Safe error model implemented.
- [ ] Basic audit implemented.
- [ ] At least one safe file binding implemented and tested.
- [ ] Path traversal tests pass.
- [ ] Binding security checklist passes.

## Compiled-mode release

- [ ] Compiler emits valid managed assemblies.
- [ ] Generated assemblies use runtime stubs only.
- [ ] Verifier implemented.
- [ ] Verifier malicious fixtures pass.
- [ ] Compiled/interpreted differential tests pass.
- [ ] DLL cache manifest implemented.
- [ ] Cache invalidation tests pass.
- [ ] Cache corruption tests pass.
- [x] `AssemblyLoadContext` lifecycle tested.
- [ ] Fallback behavior documented.

## Production hardening

- [ ] Worker process/container mode available for high-risk tenants.
- [ ] Worker has no secrets by default.
- [ ] Worker resource limits configured.
- [ ] Audit retention configured.
- [ ] Metrics dashboards configured.
- [ ] Security alerting configured.
- [ ] Binding review process documented.
- [ ] Capability grant process documented.
- [ ] Red-team scenarios run.
- [ ] Incident response for verifier/cache failures documented.

## Documentation

- [ ] User-facing language docs.
- [ ] Host binding author guide.
- [ ] Security model docs.
- [ ] Capability catalog.
- [ ] Error code reference.
- [ ] Debugging guide.
- [ ] Operational runbook.
