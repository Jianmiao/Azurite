using System;
using AzureArchive.Automation;
using Studio.Scripts;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Azurite;

internal sealed class EditorWorkProbe
{
	private readonly record struct WorkState(int Scene, IntPtr Inspector, bool Active, bool Loading, bool Unloading, bool Rearrange, bool SavePending, bool CompilePending, bool Saving, bool Applying, int LoadingCount, bool ResourcesKnown, bool AssetPreloading, bool DatabasesReady);

	private readonly Action<string> _log;

	private ScriptNodeInspector? _inspector;

	private StudioCommon? _studio;

	private Loading? _loading;

	private ScenarioResourceManager? _resources;

	private AuthoringEditorSession? _session;

	private int _scene = int.MinValue;

	private double _nextReferences;

	private bool _hasTick;

	private double _lastTick;

	private double _windowStarted;

	private int _gaps;

	private int _over16;

	private int _over33;

	private int _over100;

	private double _maximumGapMs;

	private bool _hasState;

	private WorkState _lastState;

	private double _nextError;

	public EditorWorkProbe(Action<string> log)
	{
		_log = log ?? throw new ArgumentNullException("log");
	}

	public void Update(double now)
	{
		if (!double.IsFinite(now) || now < 0.0)
		{
			_hasTick = false;
			return;
		}
		if (!_hasTick || now < _lastTick)
		{
			_hasTick = true;
			ResetWindow(now);
		}
		else
		{
			double num = (now - _lastTick) * 1000.0;
			_gaps++;
			if (num > 16.666666666666668)
			{
				_over16++;
			}
			if (num > 33.333333333333336)
			{
				_over33++;
			}
			if (num > 100.0)
			{
				_over100++;
			}
			if (num > _maximumGapMs)
			{
				_maximumGapMs = num;
			}
		}
		_lastTick = now;
		try
		{
			int handle = SceneManager.GetActiveScene().handle;
			if (handle != _scene)
			{
				_scene = handle;
				_nextReferences = 0.0;
				_inspector = null;
				_studio = null;
				_session = null;
				_loading = null;
				_resources = null;
			}
			if (now >= _nextReferences)
			{
				_nextReferences = now + 1.0;
				_inspector = ScriptNodeInspector.instance;
				_studio = StudioCommon.instance;
				_loading = Singleton<Loading>.Instance;
				_resources = Singleton<ScenarioResourceManager>.Instance;
				_session = AuthoringEditorSession.Current;
			}
			bool flag = _inspector != null;
			bool flag2 = _studio != null;
			bool flag3 = _resources != null;
			WorkState workState = new WorkState(_scene, flag ? _inspector.Pointer : IntPtr.Zero, flag && _inspector.isActiveAndEnabled, flag && _inspector.loading, flag && _inspector.unloading, flag && _inspector.rearrangeScheduled, flag2 && _studio.autoSavePending, flag2 && _studio.autoCompilePending, _session != null && _session.SaveInProgress, _session != null && _session.IsApplying, (_loading != null) ? _loading.loadingCount : (-1), flag3, flag3 && _resources.Preloading, flag3 && _resources.AreDbsLoaded);
			if (!_hasState || workState != _lastState)
			{
				_hasState = true;
				_lastState = workState;
				_log($"editor-work t={now:F3}s transition scene={workState.Scene} inspector={workState.Inspector} active={workState.Active} loading={workState.Loading} unloading={workState.Unloading} rearrange={workState.Rearrange} savePending={workState.SavePending} compilePending={workState.CompilePending} saving={workState.Saving} applying={workState.Applying} loadingCount={workState.LoadingCount} resourcesKnown={workState.ResourcesKnown} assetPreloading={workState.AssetPreloading} databasesReady={workState.DatabasesReady}; {ReadCounts()}.");
			}
			double num2 = now - _windowStarted;
			if (num2 >= 5.0)
			{
				_log($"editor-work t={now:F3}s Update gaps over={num2:F2}s samples={_gaps} max={_maximumGapMs:F2}ms gt16.7={_over16} gt33.3={_over33} gt100={_over100}; target={Application.targetFrameRate} vsync={QualitySettings.vSyncCount} focused={Application.isFocused}; {ReadCounts()}; {ReadScroll()}; {ReadGrid()}. Update gaps include frame pacing/waits; they are not CPU durations.");
				ResetWindow(now);
			}
		}
		catch (Exception ex)
		{
			_nextReferences = 0.0;
			_inspector = null;
			_studio = null;
			_session = null;
			_loading = null;
			_resources = null;
			if (now >= _nextError)
			{
				_nextError = now + 5.0;
				_log($"editor-work t={now:F3}s observation unavailable: {ex.GetType().Name}.");
			}
			if (now - _windowStarted >= 5.0)
			{
				ResetWindow(now);
			}
		}
	}

	private string ReadCounts()
	{
		if (_inspector == null)
		{
			return "lines=unavailable cache=unavailable";
		}
		int dataListLength = _inspector.DataListLength;
		string value = ((dataListLength < 0 || dataListLength == int.MaxValue) ? "unavailable" : dataListLength.ToString());
		return $"lines={value} cache={_inspector.scriptNodeListItemsCache?.Count ?? (-1)}";
	}

	private string ReadScroll()
	{
		if (_inspector == null)
		{
			return "scroll=unavailable";
		}
		return $"dialogue[{Describe(_inspector.scriptListScroll)}] character[{Describe(_inspector.characterTabScroll)}] environment[{Describe(_inspector.environmentTabScroll)}]";
	}

	private static string Describe(UIScrollView? scroll)
	{
		if (!(scroll == null))
		{
			return $"drag={scroll.isDragging} momentum2={scroll.currentMomentum.sqrMagnitude:F3} pendingWheel={scroll.mScroll:F3} factor={scroll.scrollWheelFactor:F3}";
		}
		return "unavailable";
	}

	private string ReadGrid()
	{
		UIGrid uIGrid = ((_inspector != null) ? _inspector.scriptList : null);
		if (uIGrid == null)
		{
			return "dialogueGrid=unavailable";
		}
		return $"dialogueGrid[smooth={uIGrid.animateSmoothly} fade={uIGrid.animateFadeIn} springs={uIGrid.mSprings?.Count ?? (-1)} enabled={uIGrid.enabled} active={uIGrid.isActiveAndEnabled}]";
	}

	private void ResetWindow(double now)
	{
		_windowStarted = now;
		_gaps = (_over16 = (_over33 = (_over100 = 0)));
		_maximumGapMs = 0.0;
	}
}
