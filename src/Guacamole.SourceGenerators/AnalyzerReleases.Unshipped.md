; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
; GUAC0001 and GUAC0002 were removed before the first release (they could never be reported); do not reuse these IDs.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
GUAC0003 | Guacamole.SourceGenerator | Error | Get-only IncrementalProvider property must be partial
GUAC0004 | Guacamole.Performance | Warning | Provider item type does not implement proper equality
GUAC0005 | Guacamole.SourceGenerator | Error | User-managed property (with setter) should not be partial
GUAC0006 | Guacamole.SourceGenerator | Error | Computing context and its containing types must be partial
GUAC0007 | Guacamole.SourceGenerator | Error | Computing context must derive from ComputingContextBase
GUAC0008 | Guacamole.SourceGenerator | Error | Computing context must not declare a parameterless constructor
GUAC0009 | Guacamole.SourceGenerator | Warning | Computing context constructor must chain to ': this()'
