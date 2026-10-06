using System;
using System.Collections;

namespace Azurite;

// Used at the six native per-resource coroutine factories. Native asset acquisition,
// cache ownership, wait objects, and the parent WhenAll remain AA's responsibility.
internal sealed class ProgressivePreloadEnumerator : IEnumerator, IDisposable
{
	private readonly IEnumerator _inner;
	private readonly ProgressivePreloadBudget _budget;
	private readonly Func<object, object> _wrapChild;
	private readonly bool _resource;
	private bool _leased;
	private bool _disposed;
	private bool _completed;
	public object? Current { get; private set; }
	public ProgressivePreloadEnumerator(IEnumerator inner, ProgressivePreloadBudget budget,
		Func<object, object> wrapChild, bool resource = true)
	{
		_inner = inner;
		_budget = budget;
		_wrapChild = wrapChild;
		_resource = resource;
		if (resource) budget.Schedule();
	}
	public bool MoveNext()
	{
		if (_disposed) return false;
		if (_budget.Cancelled) { Dispose(); return false; }
		if (!_budget.TryStep(_resource && !_leased)) { Current = null; return true; }
		if (_resource) _leased = true;
		double started = _budget.Now;
		try
		{
			if (!_inner.MoveNext())
			{
				_completed = true;
				Dispose();
				return false;
			}
			Current = _wrapChild(_inner.Current);
			return true;
		}
		catch { Dispose(); throw; }
		finally { _budget.Record(started); }
	}
	public void Reset() => throw new NotSupportedException();
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		try { (_inner as IDisposable)?.Dispose(); }
		finally
		{
			if (_leased) { _budget.Release(_completed); _leased = false; }
			Current = null;
		}
	}
}
