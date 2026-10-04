# Recovered ScrollActivityGuard behavior checks

Run `dotnet run --project ScrollTests/ScrollTests.csproj -c Release` from the recovered workspace.

The project links the actual recovered `Plugin/Azurite/ScrollActivityGuard.cs`; it does not copy or rewrite its logic. Policy composition uses the original managed `references/Azurite.Core.dll` included in this source snapshot. Only Unity/NGUI/IL2CPP boundaries are replaced with deterministic host doubles. No AA process or native DLL is loaded.

Covered: raw wheel, NGUI input delivered between Update and LateUpdate, one-second hold, independent momentum/drag/pending-wheel/spring signals, viewport movement/replacement, focus, scene changes, invalid values, native-read failures, callback coexistence/replacement/disposal, and the archived AdaptivePolicy at 60/120/320/800 measured FPS.

Limitations: the doubles do not verify IL2CPP delegate ABI/conversion, Unity destroyed-object null behavior, actual NGUI event ordering, visible control topology, native property getters, GPU clocks, GPU load, input latency or real scrolling smoothness. The integration check supplies the observer result as AdaptivePolicy's protection input; it does not execute the entire Plugin loop. Passing these tests only verifies that the recovered guard's managed control logic retains these behaviors.
