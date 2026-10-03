# Platform maturity implementation

Tracks the combined implementation of #1398 and the remaining work in #670.

- Compatibility: explicit optional connection preamble over the frozen legacy frame layout;
  codec, feature, frame limit and contract fingerprint negotiation; golden/legacy interoperability.
- Provenance: signed package envelope, publisher/key trust policy, explicit unsigned policy,
  capability upgrade diff. Signatures cover metadata and exact package bytes.
- Replay: opt-in binding recording, portable execution trace, strict offline replay and redaction.
- Administration: capability explanations and kernel execution inspection using public primitives.
- Adoption: one documented Contracts / Host / Plugin path using the existing facade packages.
- Secure transport: authenticated TLS stream composition and tested current-user pipes; remove
  unshipped transport walkthroughs.
- Release integrity: transitive audit, binary compatibility baseline, dependency license policy,
  SBOM + attestation, OIDC migration, required checks, support policy and version discovery.

Validation: strict Release build, relevant regression tests, architecture/API/size/docs gates,
formatting, then complete PR checks and review conversations on the final commit.

External prerequisites must be recorded truthfully: repository settings and NuGet publisher
policies are separate from a passing PR. No release should be published just to test credentials.

## Child workstreams

- [Freeze wire compatibility and support boundaries](https://github.com/JKamsker/DotBoxD/issues/1399)
- [Add signed plugin artifacts and capability upgrade review](https://github.com/JKamsker/DotBoxD/issues/1400)
- [Add deterministic kernel execution recording and offline replay](https://github.com/JKamsker/DotBoxD/issues/1401)
- [Add capability and execution inspection with an adoption path](https://github.com/JKamsker/DotBoxD/issues/1402)
- [Verify secure transport composition and correct transport documentation](https://github.com/JKamsker/DotBoxD/issues/1403)
- [Complete release and supply-chain maturity gates](https://github.com/JKamsker/DotBoxD/issues/1404)
