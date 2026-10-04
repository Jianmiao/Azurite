namespace Azurite.Core;

public sealed class FrameRateEstimator
{
	private const double SampleSeconds = 1.0;

	private const double MaximumTickGapSeconds = 0.5;

	private bool _hasObservation;

	private double _windowStarted;

	private double _lastObserved;

	private int _ticks;

	public double FramesPerSecond { get; private set; }

	public bool IsReady { get; private set; }

	public bool Observe(double now)
	{
		if (!double.IsFinite(now) || now < 0.0)
		{
			Reset();
			return false;
		}
		if (!_hasObservation)
		{
			BeginWindow(now);
			return false;
		}
		double num = now - _lastObserved;
		if (num < 0.0 || num > 0.5)
		{
			Reset();
			BeginWindow(now);
			return false;
		}
		if (num == 0.0)
		{
			return false;
		}
		_lastObserved = now;
		_ticks++;
		double num2 = now - _windowStarted;
		if (num2 + 1E-09 < 1.0)
		{
			return false;
		}
		FramesPerSecond = (double)_ticks / num2;
		IsReady = true;
		_ticks = 0;
		_windowStarted = now;
		return true;
	}

	public void Reset()
	{
		_hasObservation = false;
		_windowStarted = 0.0;
		_lastObserved = 0.0;
		_ticks = 0;
		FramesPerSecond = 0.0;
		IsReady = false;
	}

	private void BeginWindow(double now)
	{
		_hasObservation = true;
		_windowStarted = now;
		_lastObserved = now;
		_ticks = 0;
	}
}
