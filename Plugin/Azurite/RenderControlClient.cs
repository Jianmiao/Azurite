using System;
using System.Reflection;

namespace Azurite;

internal sealed class RenderControlClient : IDisposable
{
	internal const string ContractName = "AAVideoExport.Integration.RenderControlV1";

	private readonly Func<bool> _ready;

	private readonly Func<bool> _owned;

	private readonly Action<Action> _removeBefore;

	private readonly Action<Action> _removeAfter;

	private readonly Action _beforeHandler;

	private readonly Action _afterHandler;

	private readonly Action _beforeAcquire;

	private readonly Action? _afterRelease;

	private readonly Action<string>? _report;

	private bool _beforeSubscribed;

	private bool _afterSubscribed;

	private bool _disposed;

	private bool _failed;

	private string _failureReason = string.Empty;

	private RenderControlClient(Type contract, Action beforeAcquire, Action? afterRelease, Action<string>? report)
	{
		_beforeAcquire = beforeAcquire;
		_afterRelease = afterRelease;
		_report = report;
		if (!contract.IsPublic || !contract.IsAbstract || !contract.IsSealed)
		{
			throw new MissingMemberException("RenderControlV1 must be a public static class.");
		}
		if (ReadGetter<int>(contract, "ProtocolVersion")() != 1)
		{
			throw new NotSupportedException("Unsupported render-control protocol version.");
		}
		_ready = ReadGetter<bool>(contract, "IsReady");
		_owned = ReadGetter<bool>(contract, "IsRenderingOwned");
		EventInfo eventInfo = ReadEvent(contract, "BeforeAcquire");
		EventInfo eventInfo2 = ReadEvent(contract, "AfterRelease");
		Action<Action> action = (Action<Action>)eventInfo.GetAddMethod().CreateDelegate(typeof(Action<Action>));
		Action<Action> action2 = (Action<Action>)eventInfo2.GetAddMethod().CreateDelegate(typeof(Action<Action>));
		_removeBefore = (Action<Action>)eventInfo.GetRemoveMethod().CreateDelegate(typeof(Action<Action>));
		_removeAfter = (Action<Action>)eventInfo2.GetRemoveMethod().CreateDelegate(typeof(Action<Action>));
		_beforeHandler = OnBeforeAcquire;
		_afterHandler = OnAfterRelease;
		try
		{
			_beforeSubscribed = true;
			action(_beforeHandler);
			_afterSubscribed = true;
			action2(_afterHandler);
			_ready();
			_owned();
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	public static RenderControlBinding TryBind(Assembly assembly, Action beforeAcquire, Action? afterRelease, Action<string>? report, out RenderControlClient? client, out string reason)
	{
		client = null;
		reason = string.Empty;
		if (beforeAcquire == null)
		{
			throw new ArgumentNullException("beforeAcquire");
		}
		try
		{
			Type type = assembly.GetType("AAVideoExport.Integration.RenderControlV1", throwOnError: false, ignoreCase: false);
			if (type == null)
			{
				reason = "public render-control API absent";
				return RenderControlBinding.Absent;
			}
			client = new RenderControlClient(type, beforeAcquire, afterRelease, report);
			reason = "public render-control protocol v1 negotiated";
			return RenderControlBinding.Bound;
		}
		catch (Exception ex)
		{
			reason = "public render-control API invalid: " + ex.GetType().Name + "; " + ex.Message;
			return RenderControlBinding.Invalid;
		}
	}

	public RenderControlState Probe()
	{
		if (_disposed)
		{
			return new RenderControlState(Healthy: false, Ready: false, Owned: true, "render-control client disposed");
		}
		if (_failed)
		{
			return new RenderControlState(Healthy: false, Ready: false, Owned: true, _failureReason);
		}
		try
		{
			bool flag = _ready();
			bool flag2 = _owned();
			return new RenderControlState(Healthy: true, flag, flag2, (!flag) ? "render-control provider not ready" : (flag2 ? "AAVideoExport owns rendering via public protocol" : "public render-control protocol is idle"));
		}
		catch (Exception error)
		{
			Fail("render-control state read failed", error);
			return new RenderControlState(Healthy: false, Ready: false, Owned: true, _failureReason);
		}
	}

	private void OnBeforeAcquire()
	{
		if (_disposed)
		{
			return;
		}
		try
		{
			if (_failed)
			{
				throw new InvalidOperationException(_failureReason);
			}
			if (!_ready() || !_owned())
			{
				throw new InvalidOperationException("BeforeAcquire arrived outside ready/owned state.");
			}
			_beforeAcquire();
		}
		catch (Exception error)
		{
			Fail("render-control handoff failed", error);
			throw;
		}
	}

	private void OnAfterRelease()
	{
		if (_disposed || _failed)
		{
			return;
		}
		try
		{
			if (_owned())
			{
				throw new InvalidOperationException("AfterRelease arrived before ownership cleared.");
			}
			if (_ready())
			{
				_afterRelease?.Invoke();
			}
		}
		catch (Exception error)
		{
			Fail("render-control release notification failed", error);
			throw;
		}
	}

	private void Fail(string reason, Exception error)
	{
		if (!_failed)
		{
			_failed = true;
			_failureReason = reason + ": " + error.GetType().Name;
			_report?.Invoke(_failureReason);
		}
	}

	private static Func<T> ReadGetter<T>(Type contract, string name)
	{
		PropertyInfo property = contract.GetProperty(name, BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Public);
		MethodInfo methodInfo = property?.GetGetMethod();
		if (property == null || property.PropertyType != typeof(T) || property.GetIndexParameters().Length != 0 || methodInfo == null || !methodInfo.IsPublic || !methodInfo.IsStatic || methodInfo.GetParameters().Length != 0 || methodInfo.ReturnType != typeof(T) || methodInfo.IsGenericMethod)
		{
			throw new MissingMemberException("Invalid public render-control property: " + name);
		}
		return (Func<T>)methodInfo.CreateDelegate(typeof(Func<T>));
	}

	private static EventInfo ReadEvent(Type contract, string name)
	{
		EventInfo eventInfo = contract.GetEvent(name, BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Public);
		if (eventInfo?.EventHandlerType != typeof(Action) || !ValidAccessor(eventInfo.GetAddMethod()) || !ValidAccessor(eventInfo.GetRemoveMethod()))
		{
			throw new MissingMemberException("Invalid public render-control event: " + name);
		}
		return eventInfo;
	}

	private static bool ValidAccessor(MethodInfo? method)
	{
		if (method != null && method.IsPublic && method.IsStatic && !method.IsGenericMethod && method.ReturnType == typeof(void))
		{
			ParameterInfo[] parameters = method.GetParameters();
			if (parameters != null && parameters.Length == 1)
			{
				return parameters[0].ParameterType == typeof(Action);
			}
		}
		return false;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		try
		{
			if (_beforeSubscribed)
			{
				_removeBefore(_beforeHandler);
			}
		}
		catch (Exception ex)
		{
			_report?.Invoke("render-control BeforeAcquire unsubscribe failed: " + ex.GetType().Name);
		}
		try
		{
			if (_afterSubscribed)
			{
				_removeAfter(_afterHandler);
			}
		}
		catch (Exception ex2)
		{
			_report?.Invoke("render-control AfterRelease unsubscribe failed: " + ex2.GetType().Name);
		}
		_beforeSubscribed = (_afterSubscribed = false);
	}
}
