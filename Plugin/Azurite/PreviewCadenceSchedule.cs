using System;

namespace Azurite;

// A whole-player skipped frame cannot consume a preview render deadline. Doing
// so lets the two independent schedules alias and leave the preview stale.
internal sealed class PreviewCadenceSchedule
{
	private double _nextRender;
	private double _lastNow = double.NaN;
	private double _lastRate;

	internal bool ShouldRender(double now, double targetFps, bool playerWillRender)
	{
		if (!double.IsFinite(now) || !double.IsFinite(targetFps) || targetFps < 0.1 || targetFps > 120.0)
		{
			Reset();
			return true;
		}
		if (!double.IsFinite(_lastNow) || now < _lastNow || targetFps != _lastRate)
			_nextRender = now;
		_lastNow = now;
		_lastRate = targetFps;
		if (!playerWillRender || now + 1E-06 < _nextRender) return false;
		double period = 1.0 / targetFps;
		_nextRender = now - _nextRender >= period ? now + period : _nextRender + period;
		return true;
	}

	internal void Reset()
	{
		_nextRender = 0.0;
		_lastNow = double.NaN;
		_lastRate = 0.0;
	}
}
