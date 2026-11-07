# Active preview cadence

Candidate: 1.0.27, active-preview-cadence-60-1.

The editor UI and player cadence remain at the selected tier. When HostActivity reports active interaction and a visible embedded preview, Azurite schedules only the PreviewScene camera at `ActiveInteractionCameraFps` (default 60). Whole-player input, UI layout and logical animation are not throttled. Static cache and export restoration retain their existing lifecycle guards.
