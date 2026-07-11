; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DBXK100 | DotBoxD.Kernels.Generation | Error | Plugin kernel shape is not supported; string interpolation holes may be strings or supported invariant string-convertible numeric types
DBXK111 | DotBoxD.Kernels.Generation | Error | Remote RunLocal chain could not be lowered; generation fails closed before the runtime terminal can throw
DBXK112 | DotBoxD.Kernels.Generation | Error | A [HookResult] record must declare a bool Success and a string? Reason field
DBXK113 | DotBoxD.Kernels.Generation | Error | Result hook Register/RegisterLocal chain could not be lowered; generation fails closed before the runtime terminal can throw
DBXK114 | DotBoxD.Kernels.Generation | Error | Run chain could not be lowered; generation fails closed before the runtime terminal can throw DBXK062
DBXK115 | DotBoxD.Kernels.Generation | Error | Duplicate generated server-extension graft signatures are rejected
DBXK116 | DotBoxD.Kernels.Generation | Error | [NativeOnly] context helpers are rejected outside declared contexts and from lowered server-side IR
DBXK117 | DotBoxD.Kernels.Generation | Error | Unexpected plugin source generator failures are reported without failing the whole generation pass
