# Azurite Source staging candidate

This package is a local engineering candidate, not a release-qualified Mod.
Its canonical Azurite plugin and Azurite.Core are built together from the clean
source commit/tree in BUILD.json. Neither DLL is renamed from an experimental
assembly, and the preserved Original Core is not substituted into the pair.

The included authoring UI, five card categories and batch actions have source
and managed-test checkpoints. Ordinary-editor/native input, Apply, Undo/Redo,
save/reopen, lifecycle, actual AAVE RenderControlV1 handoff, installation and
whole-pair rollback have not been qualified for these exact bytes. Source
feature switches retain their existing defaults, including Authoring/CardPacks
false; this staging work does not change product configuration or capability
claims. All gates in QUALIFICATION-PENDING.json remain mandatory.

Build input provenance includes recovered/decompiled Azurite source plus later
source changes. The earlier source and third-party ownership/notice questions
remain subject to the repository's source review; this candidate does not grant
a new license or assert that public distribution has been approved. The package
does not contain AA, Unity, BepInEx or generated interop assemblies, user project
data, personal configuration or local execution evidence.

Do not install or publish this staging candidate as a verified release. A future
release must bind independently reviewed native execution records to these exact
DLL hashes, source tree, installed profile, host and enabled configuration.
Rebuilding or changing either DLL invalidates any prior byte-bound qualification.
There is deliberately no switch to waive or self-assert release qualification.

Receipt schema 3 also binds the SDK version embedded in both assemblies to the
producer's captured dotnet, MSBuild, compiler and SDK-property file hashes.
Validation uses those exact retained tools to evaluate both clean projects and
requires the complete reference identity/path/hash inventory, plus an exact
source-project dependency graph. It does not substitute the current default SDK.
The receipt cannot select a launcher outside the caller's trusted dotnet path.
Missing retained tools or mismatched inputs reject validation. These are unsigned
local consistency records; they are not independent or signed build attestation.
