<!-- Thanks for contributing to DotBoxD! -->

## Summary

<!-- What does this change do, and why? -->

## Area

- [ ] Services (RPC)
- [ ] Kernels (sandbox runtime)
- [ ] Pushdown
- [ ] Channels / Transports / Codecs
- [ ] Source generators / analyzers
- [ ] Docs / CI / tooling

## Checklist

- [ ] `dotnet build DotBoxD.slnx -c Release` is clean
- [ ] `dotnet test DotBoxD.slnx -c Release` passes
- [ ] New/changed behavior is covered by tests
- [ ] Security-sensitive changes (validation, verifier, bindings, capabilities) keep the
      security-boundary suite green
- [ ] Public API changes update the relevant `docs/api-baselines/*.txt`
- [ ] No new `ShaRPC`/`SafeIR` tokens in the active tree (the rebrand-completeness gate must pass)
