# Core migration precheck — 2026-11-08

This is offline preparation, not a Core deployment or native AA/AAVE acceptance.

This record binds the exact assemblies listed below. Later source-only fixes for native nullable-curve/empty-face compatibility do not replace this evidence with a check of their new binary. Recheck the final rebuilt artifact when actually migrating the Plugin; the preserved reference remains unchanged.

| Assembly | SHA-256 |
| --- | --- |
| Preserved reference | `76a46c4a896dfbdffd66c2be85e9a0e07d2d0523faaef0841763d997c65df1a5` |
| Rebuilt authoring Core | `197599dadd788b3cfd468bc8bc3feeab1d706020a48ef0db00de73bbb4af6f52` |

Both identify as `Azurite.Core, Version=1.0.0.0`. Metadata comparison found no removal or change among 111 original public/protected ABI entries; the rebuilt assembly adds 869 entries, including 56 types. This verifies exposed metadata continuity, not every method body's semantics.

Four precompiled test suites with verified direct Core member references were copied into isolated original/rebuilt pairs. Only Core differed between each pair. Preview (35), Scroll (16), HostProfile (77), and CPU (181) passed on both assemblies, with identical output: 309 assertions per variant. These totals include linked plugin-policy assertions and are not 309 distinct Core behaviors.

Detailed scripts, input/output hashes, ABI records and process logs are retained locally under `E:\aamod\diagnostics\azurite-plan-20261108\core-compatibility`. No repository reference, Plugin output or installed binary was substituted.

Before migration, the native adapter must pass the separate ownership/Undo/save/effects gates; Plugin compilation and packaging must explicitly consume the same rebuilt Core artifact and validate its identity/hash. The 1.0.40 stable candidate still ships the preserved original reference. Reflection consumers, native interop, serialization and live loading remain outside this precheck.
