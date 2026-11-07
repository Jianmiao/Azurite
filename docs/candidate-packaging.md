# Candidate archive identity and provenance

`tools/Build-Candidate.ps1` forces a Release plugin rebuild from a clean Git commit against an explicitly supplied AA installation path. It records the commit, Git source tree, exact plugin and Core hashes, binary version/build markers, and hashes of the host reference DLLs in the ignored `artifacts/candidate-build-receipt.json`. It checks that the source commit/tree and host reference hashes remain unchanged after the build. The receipt is local evidence of a successful controlled build, not a cryptographic attestation.

`tools/Package-Release.ps1` requires that receipt to match the current clean commit/tree and exact plugin/Core payload before publishing. A source-only commit after the build, a missing receipt, or a changed DLL hash refuses the archive. Binary identity is read with Mono.Cecil; no plugin or AA/Unity assembly is loaded for execution. Supply either `-AAInstallPath` or `-CecilPath` explicitly for packaging. There is no machine-specific fallback.

Before creating the final ZIP, packaging compares the three-part `assets/manifest.json` version with the plugin assembly version (`version.0`), file version (`version.0`), informational version (`version-candidate.build`), `BepInPlugin` version, `Plugin.Version` constant, and `Plugin.CandidateBuild` constant. It requires the original Core DLL SHA-256 `76a46c4a896dfbdffd66c2be85e9a0e07d2d0523faaef0841763d997c65df1a5`. A dirty or untracked source tree, changed commit, missing or stale build receipt, missing metadata, or identity mismatch aborts publication. Existing ZIPs and checksum sidecars are never overwritten.

From the committed 1.0.40 source, with output in a new directory:

```powershell
$commit = (& git rev-parse HEAD).Trim()
$output = 'E:\aamod\releases\Azurite-1.0.40-candidate'
& .\tools\Build-Candidate.ps1 -AAInstallPath 'F:\AzureArchive_101'
& .\tools\Package-Release.ps1 -AAInstallPath 'F:\AzureArchive_101' -OutputDirectory $output
& .\tools\Test-ReleaseArchive.ps1 -ArchivePath (Join-Path $output 'Azurite-1.0.40.zip') -ExpectedVersion '1.0.40' -ExpectedSourceCommit $commit -AAInstallPath 'F:\AzureArchive_101' -RequireSidecar
```

Each new archive contains `Azurite/<version>/BUILD.json` with schema `1`, the 40-character source commit and tree, `sourceTreeClean: true`, version, candidate build, plugin and Core SHA-256 values, relative host reference paths and hashes, `coreMode: "original"`, and `runtimeAcceptance: "not-performed"`. Internal `SHA256SUMS.txt` covers BUILD.json and every other payload file; the `.zip.sha256` sidecar covers the exact archive bytes. The archive checker verifies BUILD.json against the contained manifest and binary payloads. An archive for 1.0.40 or later without BUILD.json is rejected. The existing 1.0.38 archive without BUILD.json remains readable by the checker.

Run the binary fixture suite without launching AA:

```powershell
$legacyArchive = '<absolute path to the validated 1.0.38 ZIP>'
& .\Tests\release\Test-CandidatePackaging.ps1 -CecilPath 'F:\AzureArchive_101\BepInEx\core\Mono.Cecil.dll' -LegacyArchivePath $legacyArchive
```

The legacy ZIP is a read-only input and is checked against its known SHA-256. Fixture tests create constrained receipt records for acceptance/refusal cases; they do not run a production candidate build. These checks establish archive identity and catch stale source or payload outputs in the controlled workflow. They do not perform native runtime acceptance, install a candidate, or claim a pass in AA. The archive validator needs an explicit Cecil path or AA installation path when BUILD.json is present; callers that validate a new candidate must forward one of those inputs.
