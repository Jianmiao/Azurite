namespace Azurite.Core;

public sealed class VisualCadence
{
	private bool _observed;

	private double _lastNow;

	private double _lastDirty;

	public VisualCadencePlan Resolve(double now, bool dirty, double safetyFps = 1.0, double animationFps = 30.0, double settleSeconds = 0.35)
	{
		if (!double.IsFinite(now) || now < 0.0 || !double.IsFinite(safetyFps) || safetyFps < 1.0 || safetyFps > 15.0 || !double.IsFinite(animationFps) || animationFps < 15.0 || animationFps > 60.0 || animationFps < safetyFps || !double.IsFinite(settleSeconds) || settleSeconds < 0.1 || settleSeconds > 5.0)
		{
			Reset();
			return new VisualCadencePlan(Valid: false, 0.0, "invalid-visual-cadence");
		}
		if (_observed && now < _lastNow)
		{
			Reset();
			return new VisualCadencePlan(Valid: false, 0.0, "non-monotonic-visual-clock");
		}
		if (!_observed || dirty)
		{
			_lastDirty = now;
		}
		_observed = true;
		_lastNow = now;
		bool flag = now - _lastDirty < settleSeconds;
		return new VisualCadencePlan(Valid: true, flag ? animationFps : safetyFps, flag ? "idle visual changes" : "static safety redraw");
	}

	public void Reset()
	{
		_observed = false;
		_lastNow = 0.0;
		_lastDirty = 0.0;
	}
}
