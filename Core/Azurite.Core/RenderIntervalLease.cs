using System;

namespace Azurite.Core;

public sealed class RenderIntervalLease
{
	private readonly Func<int> _read;

	private readonly Action<int> _write;

	private bool _owned;

	private bool _blocked;

	private int _lastWritten;

	public bool IsBlocked => _blocked;

	public bool IsOwned => _owned;

	public int? LastWrittenInterval
	{
		get
		{
			if (!_owned)
			{
				return null;
			}
			return _lastWritten;
		}
	}

	public RenderIntervalLease(Func<int> read, Action<int> write)
	{
		_read = read ?? throw new ArgumentNullException("read");
		_write = write ?? throw new ArgumentNullException("write");
	}

	public bool TryApply(int interval)
	{
		if (interval < 1 || _blocked)
		{
			return false;
		}
		int num;
		try
		{
			num = _read();
		}
		catch
		{
			Block();
			return false;
		}
		if (!_owned)
		{
			if (num != 1)
			{
				Block();
				return false;
			}
			if (!WriteAndVerify(interval))
			{
				return false;
			}
			_owned = true;
			_lastWritten = interval;
			return true;
		}
		if (num != _lastWritten)
		{
			Block();
			return false;
		}
		if (interval == _lastWritten)
		{
			return true;
		}
		if (!WriteAndVerify(interval))
		{
			return false;
		}
		_lastWritten = interval;
		return true;
	}

	public bool Restore()
	{
		if (_blocked)
		{
			return false;
		}
		if (!_owned)
		{
			return true;
		}
		int num;
		try
		{
			num = _read();
		}
		catch
		{
			Block();
			return false;
		}
		if (num != _lastWritten)
		{
			Block();
			return false;
		}
		if (!WriteAndVerify(1))
		{
			return false;
		}
		_owned = false;
		return true;
	}

	private bool WriteAndVerify(int interval)
	{
		try
		{
			_write(interval);
			if (_read() != interval)
			{
				Block();
				return false;
			}
			return true;
		}
		catch
		{
			Block();
			return false;
		}
	}

	private void Block()
	{
		_blocked = true;
		_owned = false;
	}
}
