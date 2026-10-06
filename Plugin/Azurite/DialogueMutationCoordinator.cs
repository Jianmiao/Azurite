using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Studio.Scripts;

namespace Azurite;

/// <summary>
/// Harmony boundary around native script-list mutations.  This is a coordination
/// seam only: the original AA methods still execute synchronously and in full.
/// It prevents idle render/layout optimizations from observing a half-rebuilt
/// list and provides one settle hold after the outermost operation completes.
/// </summary>
internal sealed class DialogueMutationCoordinator : IDisposable
{
	private const string Owner = "halocue.azurite.dialogue-mutation";

	private static DialogueMutationCoordinator? _active;

	private readonly Harmony _harmony = new Harmony(Owner);
	private readonly Action<string>? _log;
	private readonly Action? _wake;
	private readonly DialogueMutationState _state = new DialogueMutationState();

	private bool _installed;
	private bool _disposed;

	public DialogueMutationCoordinator(Action<string>? log = null, Action? wake = null)
	{
		_log = log;
		_wake = wake;
	}

	public bool IsInstalled => _installed && !_disposed;

	public bool IsActive => IsInstalled && _state.IsActive;

	public int Depth => _state.Depth;

	public long Generation => _state.Generation;

	public double LastCompletedAt => _state.LastCompletedAt;

	public bool IsBlocking(double now, double settleSeconds)
	{
		if (!IsInstalled)
		{
			return false;
		}
		return _state.IsBlocking(now, settleSeconds);
	}

	public bool Install()
	{
		if (_disposed)
		{
			return false;
		}
		if (_installed)
		{
			return true;
		}
		if (_active != null && _active != this)
		{
			return false;
		}
		try
		{
			_active = this;
			Patch(typeof(ScriptNodeInspector), "InsertScript", Type.EmptyTypes);
			Patch(typeof(ScriptNodeInspector), "InsertScript", new[] { typeof(int) });
			Patch(typeof(ScriptNodeInspector), "DeleteScript", Type.EmptyTypes);
			Patch(typeof(ScriptNodeInspector), "SyncScriptList", new[] { typeof(bool), typeof(bool), typeof(bool) });
			_installed = true;
			_log?.Invoke("dialogue mutation gate enabled; native insert/delete/sync remain synchronous; settle hold protects render/layout shortcuts.");
			return true;
		}
		catch (Exception error)
		{
			try
			{
				_harmony.UnpatchSelf();
			}
			catch
			{
			}
			if (_active == this)
			{
				_active = null;
			}
			_log?.Invoke("dialogue mutation gate unavailable: " + error + ". Native editor behavior is unchanged.");
			return false;
		}
	}

	private void Patch(Type type, string name, Type[] parameters)
	{
		MethodInfo method = AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.FullName, name);
		_harmony.Patch(method,
			new HarmonyMethod(typeof(DialogueMutationCoordinator), nameof(Begin)),
			new HarmonyMethod(typeof(DialogueMutationCoordinator), nameof(Complete)),
			null,
			new HarmonyMethod(typeof(DialogueMutationCoordinator), nameof(AfterMutation)),
			null);
		Patches? patches = Harmony.GetPatchInfo(method);
		if (patches == null || !patches.Owners.Contains(Owner))
		{
			throw new InvalidOperationException("Dialogue mutation hook was not registered: " + name);
		}
	}

	private sealed class Scope
	{
		internal DialogueMutationLease? Lease;
		internal bool Ended;
	}

	private static void Begin(out IntPtr __state)
	{
		__state = IntPtr.Zero;
		DialogueMutationCoordinator? active = _active;
		if (active != null && active.IsInstalled)
		{
			Scope scope = new Scope { Lease = active._state.Enter() };
			__state = GCHandle.ToIntPtr(GCHandle.Alloc(scope));
			if (active._state.Depth == 1)
			{
				try
				{
					active._wake?.Invoke();
				}
				catch (Exception error)
				{
					active._log?.Invoke("dialogue mutation wake failed: " + error.GetType().Name + ". Native operation remains protected.");
				}
			}
		}
	}

	private static void Complete(IntPtr __state)
	{
		End(GetScope(__state), exception: null);
	}

	private static Exception? AfterMutation(Exception? __exception, IntPtr __state)
	{
		if (__state != IntPtr.Zero)
		{
			GCHandle handle = GCHandle.FromIntPtr(__state);
			try
			{
				End(handle.Target as Scope, __exception);
			}
			finally
			{
				handle.Free();
			}
		}
		return __exception;
	}

	private static Scope? GetScope(IntPtr state)
	{
		return state == IntPtr.Zero ? null : GCHandle.FromIntPtr(state).Target as Scope;
	}

	private static void End(Scope? scope, Exception? exception)
	{
		if (scope == null || scope.Ended)
		{
			return;
		}
		scope.Ended = true;
		scope.Lease?.Complete((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
		if (exception != null)
		{
			_active?._log?.Invoke("dialogue mutation failed; render/layout shortcuts remain protected until settle: " + exception.GetType().Name + ".");
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		_state.Reset((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
		if (_active == this)
		{
			_active = null;
		}
		if (_installed)
		{
			try
			{
				_harmony.UnpatchSelf();
			}
			catch
			{
			}
		}
		_installed = false;
	}
}
