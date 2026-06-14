; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DBXS001 | DotBoxD.Services.SourceGenerator | Error | DotBoxD source generator failure
DBXS002 | DotBoxD.Services.SourceGenerator | Error | Unsupported DotBoxD method shape (e.g. ref/in/out parameter)
DBXS003 | DotBoxD.Services.SourceGenerator | Error | Unsupported DotBoxD service shape (e.g. generic or nested interface)
DBXS004 | DotBoxD.Services.SourceGenerator | Warning | Async sibling interface method name collides with another method
