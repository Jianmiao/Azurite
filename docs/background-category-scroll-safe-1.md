# Background category scroll safety

Candidate: 1.0.28, background-category-scroll-safe-1.

Background thumbnail scrolling and the folder-label list use different native layout behavior. The previous shared 4.5x multiplier could move the vertical category grid faster than its UIGrid reflow, producing overlapping labels. Azurite now leaves the category-label scroll at the native multiplier while retaining the thumbnail gallery multiplier.
