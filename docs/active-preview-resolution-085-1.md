# Active preview resolution

Candidate: 1.0.33, active-preview-resolution-085-1.

`PreviewTargetLease` now accepts a scoped interaction resolution scale. When HostActivity reports active interaction, the embedded preview target uses `ActiveInteractionResolutionScale` (default 0.85) while the main editor UI stays at the selected display tier. The target lease retains aspect ratio, hysteresis, foreign-consumer checks and export restoration. Once interaction settles, the target returns to full viewport matching.
