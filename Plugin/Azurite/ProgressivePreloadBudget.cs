using System;

namespace Azurite;

// Cooperative budget: a native asset decode cannot be preempted halfway through a step.
internal sealed class ProgressivePreloadBudget
{
	private readonly Func<int> _frame;
	private readonly Func<double> _milliseconds;
	private int _lastFrame = int.MinValue;
	private int _starts;
	private int _steps;
	private double _spent;
	public bool Cancelled { get; private set; }
	public bool Paused { get; set; }
	public int Active { get; private set; }
	public int Completed { get; private set; }
	public int Scheduled { get; private set; }
	public double MaximumStepMilliseconds { get; private set; }
	public int OverBudgetFrames { get; private set; }
	public double Now => _milliseconds();
	public ProgressivePreloadBudget(Func<int> frame, Func<double> milliseconds)
	{
		_frame = frame;
		_milliseconds = milliseconds;
	}
	public void Schedule() => Scheduled++;
	public void Cancel() => Cancelled = true;
	public bool TryStep(bool acquireResource)
	{
		if (Cancelled || Paused) return false;
		int frame = _frame();
		if (frame != _lastFrame)
		{
			_lastFrame = frame;
			_spent = 0;
			_steps = 0;
			_starts = 0;
		}
		if (_steps > 0 && _spent >= 4.0) return false;
		if (acquireResource && (Active >= 4 || _starts >= 4)) return false;
		_steps++;
		if (acquireResource) { Active++; _starts++; }
		return true;
	}
	public void Record(double started)
	{
		double duration = Math.Max(0, Now - started);
		if (_spent < 4.0 && _spent + duration >= 4.0) OverBudgetFrames++;
		_spent += duration;
		MaximumStepMilliseconds = Math.Max(MaximumStepMilliseconds, duration);
	}
	public void Release(bool completed)
	{
		if (Active > 0) Active--;
		if (completed) Completed++;
	}
}
