using System;

namespace Azurite;

// Admission only. Already-running native tasks keep their normal player-loop
// and cancellation semantics; Unity objects are never moved to a worker.
internal sealed class ForegroundLoadBudget
{
    private bool _wasInteracting;
    private double _nextAdmission;
    public bool CanAdmit(double nowMilliseconds, bool interacting)
    {
        if (!double.IsFinite(nowMilliseconds)) return false;
        if (!interacting) { _wasInteracting = false; return true; }
        if (!_wasInteracting)
        {
            _wasInteracting = true;
            _nextAdmission = nowMilliseconds + 100;
        }
        return nowMilliseconds >= _nextAdmission;
    }
    public void Admitted(double nowMilliseconds, bool interacting)
    {
        if (interacting) _nextAdmission = nowMilliseconds + 125;
    }
}
