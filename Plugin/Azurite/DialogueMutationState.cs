using System;
using System.Threading;

namespace Azurite;

/// <summary>
/// Tracks the short period in which the native script editor is mutating its
/// dialogue list.  It deliberately does not defer or replace any native work.
/// Consumers use it to keep render/layout shortcuts disabled until the list
/// has settled after the outermost mutation.
/// </summary>
internal sealed class DialogueMutationState
{
	private int _depth;
	private long _generation;
	private double _lastCompletedAt = double.NegativeInfinity;

	public bool IsActive => Volatile.Read(ref _depth) > 0;

	public int Depth => Math.Max(0, Volatile.Read(ref _depth));

	public long Generation => Interlocked.Read(ref _generation);

	public double LastCompletedAt => Volatile.Read(ref _lastCompletedAt);

	public DialogueMutationLease Enter()
	{
		if (Interlocked.Increment(ref _depth) == 1)
		{
			Interlocked.Increment(ref _generation);
		}
		return new DialogueMutationLease(this);
	}

	internal void Complete(DialogueMutationLease lease, double now)
	{
		if (lease.Completed)
		{
			return;
		}
		lease.Completed = true;
		int depth = Interlocked.Decrement(ref _depth);
		if (depth <= 0)
		{
			Interlocked.Exchange(ref _depth, 0);
			Volatile.Write(ref _lastCompletedAt, double.IsFinite(now) ? now : double.PositiveInfinity);
		}
	}

	public bool IsBlocking(double now, double settleSeconds)
	{
		if (IsActive)
		{
			return true;
		}
		if (!double.IsFinite(now) || !double.IsFinite(settleSeconds) || settleSeconds < 0.0)
		{
			return true;
		}
		double completedAt = LastCompletedAt;
		if (double.IsNegativeInfinity(completedAt))
		{
			return false;
		}
		if (!double.IsFinite(completedAt))
		{
			return true;
		}
		return now < completedAt || now - completedAt < settleSeconds;
	}

	public void Reset(double now)
	{
		Interlocked.Exchange(ref _depth, 0);
		Interlocked.Increment(ref _generation);
		Volatile.Write(ref _lastCompletedAt, double.IsFinite(now) ? now : double.PositiveInfinity);
	}
}

internal sealed class DialogueMutationLease
{
	private readonly DialogueMutationState _owner;

	internal DialogueMutationLease(DialogueMutationState owner)
	{
		_owner = owner;
	}

	internal bool Completed { get; set; }

	public void Complete(double now)
	{
		_owner.Complete(this, now);
	}
}
