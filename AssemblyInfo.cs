using System.Runtime.Versioning;

// GenerateTargetFrameworkAttribute is disabled in the csproj, which also suppresses the SDK's automatic
// platform-support attribute the CA1416 analyzer needs to know this whole assembly is Windows-only.
[assembly: SupportedOSPlatform("windows")]
