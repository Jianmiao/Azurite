namespace Azurite.Core;

public sealed class AdaptivePolicy
{
	private bool _hasObservation;

	private bool _lastFocused;

	private double _lastNow;

	private double _idleSince;

	public void Reset(double nowSeconds)
	{
		_hasObservation = false;
		_lastFocused = false;
		_lastNow = nowSeconds;
		_idleSince = nowSeconds;
	}

	public RenderDecision Update(double nowSeconds, bool enabled, bool compatible, bool protectedWork, bool activity, bool focused, int idleInterval = 2, int deepIdleInterval = 4, double idleDelay = 2.0, double deepIdleDelay = 8.0)
	{
		if (!IsFinite(nowSeconds) || !IsFinite(idleDelay) || !IsFinite(deepIdleDelay) || idleInterval < 1 || deepIdleInterval < idleInterval || idleDelay < 0.0 || deepIdleDelay < idleDelay)
		{
			ClearWindow(nowSeconds, focused);
			return new RenderDecision(RenderMode.Uncertain, 1, "invalid-time-or-policy");
		}
		if (_hasObservation && nowSeconds < _lastNow)
		{
			ClearWindow(nowSeconds, focused);
			return new RenderDecision(RenderMode.Uncertain, 1, "non-monotonic-time");
		}
		_lastNow = nowSeconds;
		if (!enabled)
		{
			ClearWindow(nowSeconds, focused);
			return new RenderDecision(RenderMode.Disabled, 1, "disabled");
		}
		if (!compatible)
		{
			ClearWindow(nowSeconds, focused);
			return new RenderDecision(RenderMode.Incompatible, 1, "incompatible-host");
		}
		if (protectedWork)
		{
			ClearWindow(nowSeconds, focused);
			return new RenderDecision(RenderMode.Protected, 1, "protected-work");
		}
		if (!_hasObservation)
		{
			_hasObservation = true;
			_lastFocused = focused;
			_idleSince = nowSeconds;
			return new RenderDecision(RenderMode.Active, 1, "initial-observation");
		}
		bool flag = focused != _lastFocused;
		_lastFocused = focused;
		if (activity || flag)
		{
			_idleSince = nowSeconds;
			return new RenderDecision(RenderMode.Active, 1, activity ? "activity" : "focus-changed");
		}
		double num = nowSeconds - _idleSince;
		if (num < idleDelay)
		{
			return new RenderDecision(RenderMode.Active, 1, focused ? "idle-delay" : "unfocused-idle-delay");
		}
		if (num < deepIdleDelay)
		{
			return new RenderDecision(RenderMode.Idle, idleInterval, focused ? "idle" : "unfocused-idle");
		}
		return new RenderDecision(RenderMode.DeepIdle, deepIdleInterval, focused ? "deep-idle" : "unfocused-deep-idle");
	}

	private void ClearWindow(double nowSeconds, bool focused)
	{
		_hasObservation = false;
		_lastFocused = focused;
		_lastNow = nowSeconds;
		_idleSince = nowSeconds;
	}

	private static bool IsFinite(double value)
	{
		if (!double.IsNaN(value))
		{
			return !double.IsInfinity(value);
		}
		return false;
	}
}
