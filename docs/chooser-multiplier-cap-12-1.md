# Chooser multiplier cap fix

Candidate: 1.0.20, chooser-multiplier-cap-12-1.

The previous `NormalizeMultiplier` still returned `Math.Min(multiplier, 6f)` even after the chooser limit was raised. The runtime log confirmed character `1 -> 6` and emotion `0.25 -> -1.5`; configured `11.25` never reached the control. It now uses the shared `MaximumMultiplier=12`.

Character default is `9.0` (2x the original 4.5). Emotion remains `11.25` with native reverse direction. Existing profiles migrate character values of 4.5 or the prior migrated 11.25 to 9.0; emotion values of 4.5 migrate to 11.25.
