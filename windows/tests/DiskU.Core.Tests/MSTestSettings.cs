// Every test works in its own temp folder, so methods can run in parallel.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
// DiskU.Core is Windows-only.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]
