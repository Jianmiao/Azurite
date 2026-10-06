using System;
using System.Threading;
using Azurite.Core;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using Il2CppSystem;
using UnityEngine;

namespace Azurite;

internal sealed class FpsController : System.IDisposable
{
	private const int NoTier = int.MinValue;

	private readonly ManualLogSource _log;

	private readonly System.Action _wake;

	private readonly System.Action<int> _managedHandler;

	private readonly Il2CppSystem.Action<int> _nativeHandler;

	private UserSettings? _settings;

	private System.IntPtr _settingsPointer;

	private bool _subscribed;

	private int _eventTier = int.MinValue;

	private int _observedTier = int.MinValue;

	private int _pendingTier = int.MinValue;

	private bool _pendingIsTierChange;

	private long _update;

	private long _applyAfterUpdate;

	private double _applyAfterTime;

	private double _nextProbe;

	private int _displayRefreshRate;

	private bool _displayRateWarningLogged;

	private bool _allowed;

	private bool _disposed;

	private bool _failed;

	private bool _blocked;

	private bool _owned;

	private bool _everBound;

	private bool _nativeStartupRetryUsed;

	private bool _hasScene;

	private int _sceneHandle;

	private bool _resumePending;

	private bool _resumeExpectedValid;

	private bool _exportSuspended;

	private bool _exportHandoffSucceeded = true;

	private bool _resumePairAuthorized;

	private FpsPlan _beforeOverride;

	private FpsPlan _lastWritten;

	private FpsPlan _resumeExpected;

	private FpsPlan _normalPlan;

	private bool _idleOwned;
	private bool _idleWritePending;
	private FpsPlan _idleWriteBefore;
	private FpsPlan _idleWriteMiddle;

	internal int IdleFrameRate => _idleOwned ? _lastWritten.TargetFrameRate : 0;

	public bool IsOperational
	{
		get
		{
			if (!_disposed && !_failed)
			{
				return !_blocked;
			}
			return false;
		}
	}

	public bool IsMappingApplied
	{
		get
		{
			if (IsOperational)
			{
				return _owned;
			}
			return false;
		}
	}

	public FpsController(ManualLogSource log, System.Action wake)
	{
		_log = log ?? throw new System.ArgumentNullException("log");
		_wake = wake ?? throw new System.ArgumentNullException("wake");
		_managedHandler = OnTierChanged;
		_nativeHandler = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>(_managedHandler) ?? throw new System.InvalidOperationException("FPS event delegate conversion returned null");
	}

	public void Update(double now, bool allowed, int sceneHandle)
	{
		if (allowed && (!_hasScene || sceneHandle != _sceneHandle))
		{
			_hasScene = true;
			_sceneHandle = sceneHandle;
			_nativeStartupRetryUsed = false;
			_nextProbe = 0.0;
		}
		Update(now, allowed);
	}

	public void Update(double now, bool allowed)
	{
		if (_disposed || _failed)
		{
			return;
		}
		_update++;
		if (!allowed)
		{
			RestoreIdleFrameRate();
			if (_allowed && !_exportSuspended)
			{
				_resumePending = true;
				_resumeExpectedValid = _owned;
				_resumeExpected = _lastWritten;
			}
			_allowed = false;
			_pendingTier = int.MinValue;
			_pendingIsTierChange = false;
			_resumePairAuthorized = false;
			Interlocked.Exchange(ref _eventTier, int.MinValue);
			return;
		}
		_allowed = true;
		try
		{
			if (now >= _nextProbe)
			{
				_nextProbe = now + 1.0;
				ProbeSettings(now);
				ProbeDisplayRate(now);
			}
			if (_settings == null || _resumePending)
			{
				return;
			}
			if (Interlocked.Exchange(ref _eventTier, int.MinValue) != int.MinValue)
			{
				RestoreIdleFrameRate(allowNativeWrite: true);
				if (!_settings.isReady)
				{
					Unsubscribe();
					_pendingTier = int.MinValue;
					return;
				}
				int fpsTier = _settings.fpsTier;
				bool flag = fpsTier != _observedTier;
				_observedTier = fpsTier;
				if (flag)
				{
					_blocked = false;
				}
				Queue(fpsTier, flag, now, deferOneUpdate: false);
			}
			if (_pendingTier != int.MinValue && _update >= _applyAfterUpdate && now >= _applyAfterTime)
			{
				int pendingTier = _pendingTier;
				bool pendingIsTierChange = _pendingIsTierChange;
				_pendingTier = int.MinValue;
				Apply(pendingTier, pendingIsTierChange, now);
			}
		}
		catch (System.Exception ex)
		{
			_failed = true;
			_pendingTier = int.MinValue;
			ManualLogSource log = _log;
			bool isEnabled;
			BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(30, 1, out isEnabled);
			if (isEnabled)
			{
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("Azurite FPS mapping stopped: ");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(ex.GetType().Name);
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral(".");
			}
			log.LogWarning(bepInExWarningLogInterpolatedStringHandler);
		}
	}

	public bool SuspendForExport()
	{
		if (_disposed || _exportSuspended)
		{
			return _exportHandoffSucceeded;
		}
		bool idleRestored = RestoreIdleFrameRate();
		_exportSuspended = true;
		_allowed = false;
		_resumePending = _everBound;
		_resumeExpectedValid = false;
		_resumePairAuthorized = false;
		_pendingTier = int.MinValue;
		Interlocked.Exchange(ref _eventTier, int.MinValue);
		_exportHandoffSucceeded = !_owned && !_idleOwned;
		try
		{
			if (idleRestored && _owned && ReleaseOwned())
			{
				_resumeExpected = Read();
				_resumeExpectedValid = true;
				_exportHandoffSucceeded = true;
			}
			else if (!_everBound)
			{
				_resumePending = false;
			}
		}
		catch (System.Exception ex)
		{
			_exportHandoffSucceeded = false;
			_owned = false;
			_blocked = true;
			ManualLogSource log = _log;
			bool isEnabled;
			BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(36, 1, out isEnabled);
			if (isEnabled)
			{
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("Azurite FPS export handoff failed: ");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(ex.GetType().Name);
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral(".");
			}
			log.LogWarning(bepInExWarningLogInterpolatedStringHandler);
		}
		return _exportHandoffSucceeded;
	}

	private void ProbeSettings(double now)
	{
		UserSettings instance = Singleton<UserSettings>.Instance;
		if (instance == null || !instance.isReady)
		{
			Unsubscribe();
			_pendingTier = int.MinValue;
			return;
		}
		int fpsTier = instance.fpsTier;
		bool flag = _observedTier != int.MinValue && fpsTier != _observedTier;
		if (instance.Pointer != _settingsPointer)
		{
			Unsubscribe();
			_settings = instance;
			_settingsPointer = instance.Pointer;
			instance.add_OnFpsTierChanged(_nativeHandler);
			_subscribed = true;
			_everBound = true;
			_nativeStartupRetryUsed = false;
			_applyAfterTime = now + 1.0;
			Queue(fpsTier, flag, _applyAfterTime, deferOneUpdate: true);
		}
		else if (flag)
		{
			Queue(fpsTier, changedTier: true, now, deferOneUpdate: true);
		}
		_observedTier = fpsTier;
		if (flag)
		{
			_blocked = false;
		}
		FpsPlan fpsPlan = Read();
		if (_resumePending)
		{
			_resumePending = false;
			_exportSuspended = false;
			if ((_resumeExpectedValid && fpsPlan == _resumeExpected) || (flag && IsKnownTierPair(fpsTier, fpsPlan)))
			{
				_blocked = false;
				_resumePairAuthorized = true;
				_resumeExpected = fpsPlan;
				Queue(fpsTier, flag, now, deferOneUpdate: true);
			}
			else
			{
				Relinquish("settings changed while Azurite was suspended");
			}
			_resumeExpectedValid = false;
			return;
		}
		_exportSuspended = false;
		if (_owned && fpsPlan != _lastWritten && !flag && _pendingTier == int.MinValue && Volatile.Read(ref _eventTier) == int.MinValue)
		{
			if (!_nativeStartupRetryUsed && IsNativeWrite(fpsTier, fpsPlan))
			{
				_nativeStartupRetryUsed = true;
				Queue(fpsTier, changedTier: false, now, deferOneUpdate: true);
			}
			else
			{
				Relinquish("another writer changed the frame cap");
			}
		}
	}

	private void ProbeDisplayRate(double now)
	{
		if (!CurrentDisplayRate.TryRead(out DisplayRatePolicy display))
		{
			if (!_displayRateWarningLogged)
			{
				_displayRateWarningLogged = true;
				_log.LogWarning("Display refresh rate is unavailable; preserving the current frame cap until it can be measured.");
			}
			return;
		}
		bool rateNeedsReapply = _displayRefreshRate == 0 || _displayRefreshRate != display.RefreshRate || _displayRateWarningLogged;
		_displayRateWarningLogged = false;
		_displayRefreshRate = display.RefreshRate;
		if (rateNeedsReapply && _owned && _allowed && !_exportSuspended && _settings != null && _pendingTier == int.MinValue &&
				display.PlanForTier(_settings.fpsTier) != (_idleOwned ? _normalPlan : _lastWritten))
			Queue(_settings.fpsTier, changedTier: false, now, deferOneUpdate: true);
	}

	private void Queue(int tier, bool changedTier, double time, bool deferOneUpdate)
	{
		if ((tier >= 0 && tier <= 2) || 1 == 0)
		{
			_pendingTier = tier;
			_pendingIsTierChange |= changedTier;
			_applyAfterUpdate = _update + (deferOneUpdate ? 1 : 0);
			_applyAfterTime = System.Math.Max(_applyAfterTime, time);
		}
	}

	private void OnTierChanged(int tier)
	{
		Interlocked.Exchange(ref _eventTier, tier);
	}

	private void Apply(int tier, bool changedTier, double now)
	{
		if (!RestoreIdleFrameRate(allowNativeWrite: true)) return;
		_pendingIsTierChange = false;
		bool isEnabled = _blocked;
		if (!isEnabled)
		{
			bool flag = ((tier < 0 || tier > 2) ? true : false);
			isEnabled = flag;
		}
		if (isEnabled)
		{
			return;
		}
		bool hasDisplayRate = CurrentDisplayRate.TryRead(out DisplayRatePolicy display);
		if (!hasDisplayRate && tier != 2)
		{
			if (!_displayRateWarningLogged)
			{
				_displayRateWarningLogged = true;
				_log.LogWarning("Display refresh rate is unavailable; preserving the current frame cap until it can be measured.");
			}
			_pendingTier = tier;
			_pendingIsTierChange |= changedTier;
			_applyAfterUpdate = _update + 1;
			_applyAfterTime = System.Math.Max(_applyAfterTime, now + 1.0);
			return;
		}
		if (hasDisplayRate)
		{
			_displayRateWarningLogged = false;
			_displayRefreshRate = display.RefreshRate;
		}
		FpsPlan fpsPlan = Read();
		bool flag2 = _resumePairAuthorized && fpsPlan == _resumeExpected;
		_resumePairAuthorized = false;
		if ((!_owned || (!(fpsPlan == _lastWritten) && !IsNativeWrite(tier, fpsPlan))) && !IsKnownTierPair(tier, fpsPlan) && !flag2)
		{
			Relinquish("the current frame cap does not match AA's selected tier");
			return;
		}
		if (changedTier || !_owned || fpsPlan != _lastWritten)
		{
			int targetFrameRate = ((_owned && fpsPlan.TargetFrameRate == _lastWritten.TargetFrameRate) ? _beforeOverride.TargetFrameRate : fpsPlan.TargetFrameRate);
			int vSyncCount = ((_owned && changedTier && fpsPlan == _lastWritten) ? (2 - tier) : fpsPlan.VSyncCount);
			_beforeOverride = new FpsPlan(targetFrameRate, vSyncCount);
		}
		FpsPlan fpsPlan2 = hasDisplayRate ? display.PlanForTier(tier) : DisplayRatePolicy.ConservativeFallback(tier);
		_wake();
		if (fpsPlan != fpsPlan2)
		{
			Write(fpsPlan2);
			if (Read() != fpsPlan2)
			{
				Relinquish("frame-cap write did not remain in effect");
				return;
			}
		}
		_owned = true;
		_lastWritten = fpsPlan2;
		ManualLogSource log = _log;
		BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(92, 7, out isEnabled);
		if (isEnabled)
		{
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral("Azurite FPS tier=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(tier);
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; target=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(fpsPlan2.TargetFrameRate);
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; vSync=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(fpsPlan2.VSyncCount);
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; displayHz=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(hasDisplayRate ? display.RefreshRate : _displayRefreshRate);
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; previousTarget=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_beforeOverride.TargetFrameRate);
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; previousVSync=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_beforeOverride.VSyncCount);
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; source=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(changedTier ? "tier change" : "binding/resume");
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral(".");
		}
		log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
	}

	private static bool IsNativeTierPair(int tier, FpsPlan pair)
	{
		if (tier >= 0 && tier <= 2 && pair.TargetFrameRate == -1)
		{
			return pair.VSyncCount == 2 - tier;
		}
		return false;
	}

	private bool IsNativeWrite(int tier, FpsPlan pair)
	{
		if (!IsNativeTierPair(tier, pair))
		{
			if (tier >= 0 && tier <= 2 && pair.VSyncCount == 2 - tier)
			{
				return pair.TargetFrameRate == _lastWritten.TargetFrameRate;
			}
			return false;
		}
		return true;
	}

	private static bool IsKnownTierPair(int tier, FpsPlan pair)
	{
		if (IsNativeTierPair(tier, pair)) return true;
		return tier >= 0 && tier <= 2 && CurrentDisplayRate.TryRead(out DisplayRatePolicy display) &&
			pair == display.PlanForTier(tier);
	}

	private static FpsPlan Read()
	{
		return new FpsPlan(Application.targetFrameRate, QualitySettings.vSyncCount);
	}

	internal void SetIdleFrameRate(int requestedFps)
	{
		if (requestedFps == 0 || !_allowed || _exportSuspended || !_owned || !IsOperational || _pendingTier != NoTier)
		{
			RestoreIdleFrameRate();
			return;
		}
		try
		{
			if (requestedFps < 10 || requestedFps > 60) { RestoreIdleFrameRate(); return; }
			FpsPlan current = Read();
			if (current != _lastWritten) { _idleOwned = false; Relinquish("another writer changed the idle frame cap"); return; }
			FpsPlan normal = _idleOwned ? _normalPlan : _lastWritten;
			double normalFps = normal.VSyncCount > 0 ?
				(CurrentDisplayRate.TryRead(out DisplayRatePolicy display) ? display.ReportedRate / normal.VSyncCount : double.NaN) : normal.TargetFrameRate;
			if (!double.IsFinite(normalFps) || normalFps <= 0 || requestedFps >= normalFps) { RestoreIdleFrameRate(); return; }
			FpsPlan desired = new(requestedFps, 0);
			if (desired == current) return;
			_normalPlan = normal;
			_idleOwned = true;
			WriteIdle(desired);
			if (Read() != desired) { Relinquish("idle frame-cap write did not remain in effect"); }
			else _idleWritePending = false;
		}
		catch (System.Exception error)
		{
			_failed = true;
			RestoreIdleFrameRate();
			_log.LogWarning("Azurite idle frame cap failed: " + error.GetType().Name);
		}
	}

	internal bool RestoreIdleFrameRate(bool allowNativeWrite = false)
	{
		if (!_idleOwned) return true;
		try
		{
			FpsPlan current = Read();
			if (current != _lastWritten && !(_idleWritePending && (current == _idleWriteBefore || current == _idleWriteMiddle)))
			{
				_idleOwned = false;
				if (allowNativeWrite && _settings != null && IsNativeWrite(_settings.fpsTier, current)) return true;
				Relinquish("another writer owns the idle frame cap at restore");
				return false;
			}
			WriteIdle(_normalPlan);
			if (Read() != _normalPlan) return false;
			_lastWritten = _normalPlan;
			_idleOwned = false;
			_idleWritePending = false;
			return true;
		}
		catch (System.Exception error) { _failed = true; _log.LogWarning("Azurite idle frame-cap restore failed: " + error.GetType().Name); return false; }
	}

	private void WriteIdle(FpsPlan pair)
	{
		_idleWriteBefore = Read();
		_idleWriteMiddle = new FpsPlan(_idleWriteBefore.TargetFrameRate, pair.VSyncCount);
		_idleWritePending = true;
		_lastWritten = pair;
		QualitySettings.vSyncCount = pair.VSyncCount;
		Application.targetFrameRate = pair.TargetFrameRate;
	}

	private static void Write(FpsPlan pair)
	{
		QualitySettings.vSyncCount = pair.VSyncCount;
		Application.targetFrameRate = pair.TargetFrameRate;
	}

	private bool ReleaseOwned()
	{
		if (!_owned)
		{
			return false;
		}
		if (Read() != _lastWritten)
		{
			Relinquish("another writer owns the frame cap at release");
			return false;
		}
		if (_beforeOverride != _lastWritten)
		{
			Write(_beforeOverride);
		}
		_owned = false;
		return Read() == _beforeOverride;
	}

	private void Relinquish(string reason)
	{
		_idleOwned = false;
		_idleWritePending = false;
		_owned = false;
		_pendingTier = int.MinValue;
		if (!_blocked)
		{
			_log.LogWarning("Azurite FPS mapping yielded: " + reason + ".");
		}
		_blocked = true;
	}

	private void Unsubscribe()
	{
		if (_settings != null && _subscribed)
		{
			try
			{
				_settings.remove_OnFpsTierChanged(_nativeHandler);
			}
			catch
			{
			}
		}
		_subscribed = false;
		_settings = null;
		_settingsPointer = System.IntPtr.Zero;
		Interlocked.Exchange(ref _eventTier, int.MinValue);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		Unsubscribe();
		try
		{
			RestoreIdleFrameRate();
			ReleaseOwned();
		}
		catch (System.Exception ex)
		{
			_owned = false;
			ManualLogSource log = _log;
			bool isEnabled;
			BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(29, 1, out isEnabled);
			if (isEnabled)
			{
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("Azurite FPS restore failed: ");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(ex.GetType().Name);
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral(".");
			}
			log.LogWarning(bepInExWarningLogInterpolatedStringHandler);
		}
		System.GC.KeepAlive(_nativeHandler);
		System.GC.KeepAlive(_managedHandler);
	}
}
