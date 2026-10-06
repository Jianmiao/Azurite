# Progressive character entry and chooser scroll correction

Candidate: 1.0.19, progressive-character-entry-2.

CharacterExplorer and EmotionExplorer use opposite horizontal scroll conventions. The tuning keeps `direction=1` for CharacterExplorer and `direction=-1` for EmotionExplorer. Both use the migrated `11.25` multiplier with a `12` cap.

Progressive loading no longer patches `OpenCharacterSelector`, `OpenEmotionSelector`, or `SetCharacter`. Character and emotion model operations can happen while resources load; nested resource synchronization remains guarded. Environment/background entry remains gated.

LoadingIntegrationTests: 36/36. VirtualizationTests: 53/53. DriverTests: 12/12. AAVE 0.2.4 RenderControlV1: 11/11.
