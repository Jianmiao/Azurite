# Background gallery admission regression tests

Run:

```powershell
dotnet run --project BackgroundGalleryTests/BackgroundGalleryTests.csproj -c Release
```

The tests compile `Plugin/Azurite/BackgroundGalleryLoadCulling.cs` directly.
`GallerySource` can point to an earlier copy for differential verification.
The initial 12 cases were run before the production change: 1.0.33 passed 3/12,
and the replacement passed 12/12. Additional failure/re-enable cases cover the
replacement's async lifecycle.

The simulated native call chain is based on the installed
`F:\AzureArchive_100_fix\GameAssembly.dll`, inspected on 2026-10-10:

| Method token | RVA | Relevant native behavior |
| --- | --- | --- |
| 100690445 / 100690446 | `00813b30` | `Update` and `UpdateLoadState` share a body. Active-in-hierarchy and `UIWidget.isVisible` select load versus unload. |
| 100690447 | `00813740` | `LoadBGAsync` allocates its callback display class before its `unloaded` guard at `008137d6`. Accepted loads set `unloaded=false`, increment generation, and request a preferred thumbnail. |
| 100690448 | `00813a20` | `UnloadBg` increments generation, releases the current texture, clears the texture, and sets `unloaded=true`. |
| 100690449 | `008139b0` | `OnDisable` jumps directly to `UnloadBg`. |
| 100690452 | `0082a880` | The async callback rejects cancelled or stale generations and releases the returned asset; otherwise it sizes and assigns the texture. A null result does not reset `unloaded`. |

The old controller added six native `worldCorners` reads per active item per
frame and toggled `UITexture.enabled`, despite native visibility already
controlling resource acquisition. The replacement does neither. It bounds new
request submission to two per frame, with eight outstanding requests; loaded
and loading items do not re-enter the allocating native load method. Native
callback, cancellation, visibility, layout, ordering and asset release remain
authoritative.

The ten-second admission watchdog only forgets stalled bookkeeping. It does
not cancel or retry native requests, mutate resources or unblock a permanently
failed thumbnail. More than eight actual underlying requests may exist after
that watchdog or a suspend/resume transition. This is a responsiveness fallback
for lost callbacks, not a guarantee about the resource manager's queue size.

Limit: the Harmony, IL2CPP and NGUI host surfaces are stubbed. These tests prove
admission/lifecycle behavior at the source-linked native-call seam, not Unity
GPU timing, image decode cost, visual layout, or the user's scroll frame rate.
The actual plug-in is separately built against the installed AA assemblies.
