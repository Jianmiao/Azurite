# Chooser scroll correction

Candidate: 1.0.18, chooser-scroll-direction-2-5x-1.

## Scroll mapping

`EditorScrollTuning` writes the native chooser `scrollWheelFactor` as `baseline * multiplier * direction`. The character and emotion descendants were using `direction=-1`, which inverted the requested horizontal movement. The candidate uses `direction=1`.

The previous chooser multiplier was `4.5`; the new default is `11.25` (2.5x). `MaximumMultiplier` is `12`, so the requested value is not clamped. Existing profiles with exactly the shipped `4.5` default migrate to `11.25`; other user values remain unchanged.

## Loading boundary

Progressive loading again patches `OpenCharacterSelector`, `OpenEmotionSelector`, and `SetCharacter` with the same resource-operation gate as environment/background operations. During import, these entry points return without opening or mutating data. After completion, the pending inspector refresh path remains native.

## Validation

LoadingIntegrationTests: 36/36. VirtualizationTests: 53/53. DriverTests: 12/12. AAVE 0.2.4 RenderControlV1 cross-mod compiled-binary tests: 11/11. Tests are source-linked simulated Unity/IL2CPP call chains and do not replace live wheel or AA interaction testing.
