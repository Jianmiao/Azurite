# Preview optimization checks

`dotnet run --project PreviewTests/PreviewTests.csproj -c Release` validates the
production host gate, resolution plan and camera schedule without loading Unity.
In particular, the cadence regression sends 320 updates/s with only 10 actual
player render opportunities/s at an offset phase. All 10 opportunities must
refresh the preview; skipped player frames cannot consume a camera deadline.

These checks do not prove GPU savings, Unity camera ownership, UITexture lifetime,
or export image fidelity. Those require a real AA run with the experimental
preview switch and inspection of its binding/restore diagnostics.
