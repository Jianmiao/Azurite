using System;
using System.Diagnostics;
using AzureArchive.Automation;
using Il2CppSystem.Collections.Generic;
using Studio.Scripts;
using Studio.Scripts.Window;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Azurite;

internal sealed class HostActivity
{
	private readonly Action<string>? _report;

	private long _lastDiscoveryTicks;

	private long _lastFailureReportTicks;

	private StudioCommon? _studio;

	private Test? _test;

	private ScriptNodeInspector? _inspector;

	private Catalog? _catalog;

	private Loading? _loading;

	private WindowManager? _windowManager;

	private ScenarioResourceManager? _resources;

	private bool _hasRawInputSample;

	private Vector3 _lastMousePosition;

	private Vector2 _lastScroll;

	private int _lastScreenWidth;

	private int _lastScreenHeight;

	private int _lastSceneHandle = -1;

	private bool _lastFocus;

	internal bool AllowLegacyLoadingException { get; set; }

	public HostActivity(Action<string>? report = null)
	{
		_report = report;
	}

	public HostActivitySnapshot Observe(bool allowPreviewIdle = false)
	{
		bool hasEmbeddedPreview = false;
		try
		{
			string reason;
			bool flag = HasRawInputOrSurfaceChange(out reason);
			RefreshReferencesAtMostOncePerSecond();
			if (HasPreviewOrAnimation(out string reason2, out hasEmbeddedPreview) && (!allowPreviewIdle || !hasEmbeddedPreview))
			{
				return HostActivitySnapshot.Blocked(reason2, isEditor: true, hasEmbeddedPreview);
			}
			if (hasEmbeddedPreview && (_studio == null || !_studio.isActiveAndEnabled || _studio.entryNode == null || !_studio.entryNode.gameObject.activeInHierarchy || _inspector == null || _inspector.preview == null || !_inspector.preview.ready))
			{
				return HostActivitySnapshot.Blocked("embedded preview editor is not ready", isEditor: true, hasPreview: true);
			}
			if (!TryIdentifyStaticSurface(out string reason3, out UIScrollView catalogScroll, out UITable catalogTable))
			{
				return HostActivitySnapshot.Unknown(reason3, hasEmbeddedPreview);
			}
			if (flag)
			{
				return HostActivitySnapshot.Blocked(reason, isEditor: true, hasEmbeddedPreview);
			}
			if (HasInputOrIme(out string reason4))
			{
				return HostActivitySnapshot.Blocked(reason4, isEditor: true, hasEmbeddedPreview);
			}
			if (HasBlockingWindow(out string reason5))
			{
				return HostActivitySnapshot.Blocked(reason5, isEditor: true, hasEmbeddedPreview);
			}
			if (HasLoadingOrSaving(catalogTable, out string reason6))
			{
				return HostActivitySnapshot.Blocked(reason6, isEditor: true, hasEmbeddedPreview);
			}
			if (HasScrollMotion(catalogScroll, out string reason7))
			{
				return HostActivitySnapshot.Blocked(reason7, isEditor: true, hasEmbeddedPreview);
			}
			return HostActivitySnapshot.Safe(hasEmbeddedPreview ? "idle embedded editor preview observed" : reason3, hasEmbeddedPreview);
		}
		catch (Exception error)
		{
			ReportFailure("activity", error);
			return HostActivitySnapshot.Unknown("activity probe threw", hasEmbeddedPreview);
		}
	}

	private void RefreshReferencesAtMostOncePerSecond()
	{
		long timestamp = Stopwatch.GetTimestamp();
		if (_lastDiscoveryTicks != 0L && timestamp - _lastDiscoveryTicks < Stopwatch.Frequency)
		{
			return;
		}
		_lastDiscoveryTicks = timestamp;
		try
		{
			_studio = StudioCommon.instance;
		}
		catch (Exception error)
		{
			_studio = null;
			ReportFailure("studio reference", error);
		}
		try
		{
			_inspector = ScriptNodeInspector.instance;
		}
		catch (Exception error2)
		{
			_inspector = null;
			ReportFailure("inspector reference", error2);
		}
		try
		{
			_catalog = Singleton<Catalog>.Instance;
		}
		catch (Exception error3)
		{
			_catalog = null;
			ReportFailure("catalog reference", error3);
		}
		try
		{
			_loading = Singleton<Loading>.Instance;
		}
		catch (Exception error4)
		{
			_loading = null;
			ReportFailure("loading reference", error4);
		}
		try
		{
			_windowManager = Singleton<WindowManager>.Instance;
		}
		catch (Exception error5)
		{
			_windowManager = null;
			ReportFailure("window reference", error5);
		}
		try
		{
			_resources = Singleton<ScenarioResourceManager>.Instance;
		}
		catch (Exception error6)
		{
			_resources = null;
			ReportFailure("resource reference", error6);
		}
	}

	private bool TryIdentifyStaticSurface(out string reason, out UIScrollView? catalogScroll, out UITable? catalogTable)
	{
		catalogScroll = null;
		catalogTable = null;
		if (_studio != null && _studio.isActiveAndEnabled)
		{
			if (_studio.entryNode == null || !string.IsNullOrEmpty(_studio.sessionInitializationError))
			{
				reason = "node editor is not ready";
				return false;
			}
			reason = "static node editor surface observed";
			return true;
		}
		if (_catalog == null)
		{
			reason = "catalog instance unavailable";
			return false;
		}
		if (!_catalog.isActiveAndEnabled)
		{
			reason = "catalog controller inactive";
			return false;
		}
		List<Catalog.UIProfile> uiProfiles = _catalog.uiProfiles;
		int currentProfileIndex = _catalog.currentProfileIndex;
		if (uiProfiles == null)
		{
			reason = "catalog profile list unavailable";
			return false;
		}
		if (currentProfileIndex < 0 || currentProfileIndex >= uiProfiles.Count)
		{
			reason = "catalog profile index outside list";
			return false;
		}
		Catalog.UIProfile uIProfile = uiProfiles[currentProfileIndex];
		if (uIProfile == null)
		{
			reason = "catalog selected profile unavailable";
			return false;
		}
		GameObject root = uIProfile.root;
		if (root == null)
		{
			reason = "catalog profile root unavailable";
			return false;
		}
		if (!root.activeInHierarchy)
		{
			reason = "catalog profile root inactive";
			return false;
		}
		catalogScroll = uIProfile.scroll;
		if (catalogScroll == null)
		{
			reason = "catalog scroll unavailable";
			return false;
		}
		if (!catalogScroll.isActiveAndEnabled)
		{
			reason = "catalog scroll inactive or disabled";
			return false;
		}
		catalogTable = uIProfile.table;
		if (catalogTable == null)
		{
			reason = "catalog table unavailable";
			return false;
		}
		GameObject gameObject = catalogTable.gameObject;
		if (gameObject == null || !gameObject.activeInHierarchy)
		{
			reason = "catalog table object inactive";
			return false;
		}
		reason = "static catalog surface observed";
		return true;
	}

	private bool HasInputOrIme(out string reason)
	{
		reason = string.Empty;
		try
		{
			if (!Application.isFocused)
			{
				return false;
			}
			try
			{
				if (UICamera.inputHasFocus || UICamera.isDragging)
				{
					reason = "UI text input focus or drag is active";
					return true;
				}
			}
			catch
			{
				reason = "UI input state is unknown";
				return true;
			}
			UIInput current = UIInput.current;
			UIInput selection = UIInput.selection;
			if ((current != null && current.isSelected) || (selection != null && selection.isSelected))
			{
				reason = "text input or IME composition is active";
				return true;
			}
			if (!string.IsNullOrEmpty(UIInput.mLastIME))
			{
				reason = "IME composition is active";
				return true;
			}
			return false;
		}
		catch
		{
			reason = "input state is unknown";
			return true;
		}
	}

	private bool HasRawInputOrSurfaceChange(out string reason)
	{
		reason = string.Empty;
		try
		{
			Vector3 mousePosition = Input.mousePosition;
			Vector2 mouseScrollDelta = Input.mouseScrollDelta;
			int width = Screen.width;
			int height = Screen.height;
			bool isFocused = Application.isFocused;
			int handle = SceneManager.GetActiveScene().handle;
			if (handle != _lastSceneHandle)
			{
				InvalidateReferences();
			}
			bool flag = !_hasRawInputSample || isFocused != _lastFocus || width != _lastScreenWidth || height != _lastScreenHeight || handle != _lastSceneHandle || (isFocused && (mousePosition != _lastMousePosition || mouseScrollDelta != _lastScroll));
			bool flag2 = isFocused && (Input.anyKey || Input.anyKeyDown || Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2) || mouseScrollDelta.sqrMagnitude > 0.0001f);
			_hasRawInputSample = true;
			_lastMousePosition = mousePosition;
			_lastScroll = mouseScrollDelta;
			_lastScreenWidth = width;
			_lastScreenHeight = height;
			_lastSceneHandle = handle;
			_lastFocus = isFocused;
			if (flag2 || flag)
			{
				reason = (flag2 ? "raw input is active" : "window, scene, focus, or pointer surface changed");
				return true;
			}
			return false;
		}
		catch (Exception error)
		{
			InvalidateReferences();
			ReportFailure("raw input", error);
			reason = "raw input or surface state is unknown";
			return true;
		}
	}

	private bool HasBlockingWindow(out string reason)
	{
		reason = string.Empty;
		try
		{
			if (_windowManager == null)
			{
				if (_studio != null && _studio.isActiveAndEnabled)
				{
					reason = "editor window manager state is unknown";
					return true;
				}
				return false;
			}
			if (_windowManager.activeWindow != null)
			{
				reason = "an editor window is active";
				return true;
			}
			GameObject blocking = _windowManager.blocking;
			if (blocking != null && blocking.activeInHierarchy)
			{
				reason = "a modal editor blocker is active";
				return true;
			}
			return false;
		}
		catch
		{
			reason = "window state is unknown";
			return true;
		}
	}

	private bool HasLoadingOrSaving(UITable? catalogTable, out string reason)
	{
		reason = string.Empty;
		try
		{
			if (_loading == null)
			{
				reason = "global loading state is unknown";
				return true;
			}
			int loadingCount = _loading.loadingCount;
			List<string> loadingTags = Loading.LoadingTags;
			if (loadingTags == null || loadingTags.Count != 0)
			{
				reason = "global loading tags are active or unknown";
				return true;
			}
			if (loadingCount != 0)
			{
				if (!AllowLegacyLoadingException)
				{
					reason = "global loading is active or unknown";
					return true;
				}
				GameObject gameObject = _loading.gameObject;
				if (loadingCount != 1 || !(catalogTable != null) || (!(_studio == null) && _studio.isActiveAndEnabled) || _loading.isActiveAndEnabled || !(gameObject != null) || gameObject.activeInHierarchy || loadingTags == null || loadingTags.Count != 0)
				{
					reason = "global loading is active or unknown";
					return true;
				}
			}
			if (_catalog != null && _catalog.isActiveAndEnabled && (_catalog.searchPending || (catalogTable != null && catalogTable.mReposition)))
			{
				reason = "catalog search or layout is pending";
				return true;
			}
			if (_inspector != null && (_inspector.loading || _inspector.unloading || _inspector.rearrangeScheduled))
			{
				reason = "script inspector is loading, unloading, or rearranging";
				return true;
			}
			if (_studio != null && (_studio.autoSavePending || _studio.autoCompilePending))
			{
				reason = "auto-save or auto-compile is pending";
				return true;
			}
			AuthoringEditorSession current = AuthoringEditorSession.Current;
			if (current != null && (current.SaveInProgress || current.IsApplying))
			{
				reason = "authoring save or apply is in progress";
				return true;
			}
			if (_resources == null)
			{
				reason = "resource manager state is unknown";
				return true;
			}
			if (_resources.Preloading || !_resources.AreDbsLoaded)
			{
				reason = "scenario resources or databases are loading";
				return true;
			}
			return false;
		}
		catch
		{
			reason = "loading or save state is unknown";
			return true;
		}
	}

	private bool HasScrollMotion(UIScrollView? catalogScroll, out string reason)
	{
		reason = string.Empty;
		try
		{
			bool flag = ScrollIsMoving(catalogScroll);
			if (_inspector != null && _inspector.isActiveAndEnabled)
			{
				flag |= CenterableScrollIsMoving(_inspector.scriptListScroll) || CenterableScrollIsMoving(_inspector.characterTabScroll) || CenterableScrollIsMoving(_inspector.environmentTabScroll);
			}
			if (!flag)
			{
				return false;
			}
			reason = "editor scroll motion is active";
			return true;
		}
		catch (Exception error)
		{
			ReportFailure("scroll motion", error);
			reason = "editor scroll motion is unknown";
			return true;
		}
	}

	private static bool ScrollIsMoving(UIScrollView? scroll)
	{
		if (scroll == null || !scroll.isActiveAndEnabled)
		{
			return false;
		}
		float mScroll = scroll.mScroll;
		float sqrMagnitude = scroll.currentMomentum.sqrMagnitude;
		if (!scroll.isDragging && float.IsFinite(mScroll) && float.IsFinite(sqrMagnitude) && !(Math.Abs(mScroll) > 0.0001f))
		{
			return sqrMagnitude > 0.0001f;
		}
		return true;
	}

	private static bool CenterableScrollIsMoving(CenterableUIScrollView? scroll)
	{
		if (scroll == null || !scroll.isActiveAndEnabled)
		{
			return false;
		}
		if (ScrollIsMoving(scroll))
		{
			return true;
		}
		SpringPanel spring = scroll.spring;
		if (spring != null)
		{
			return spring.isActiveAndEnabled;
		}
		return false;
	}

	private bool HasPreviewOrAnimation(out string reason, out bool hasEmbeddedPreview)
	{
		reason = string.Empty;
		hasEmbeddedPreview = false;
		try
		{
			_test = Singleton<Test>.Instance;
			Test test = ((_inspector != null && _inspector.isActiveAndEnabled) ? _inspector.preview : null);
			bool num = _test != null && _test.isActiveAndEnabled;
			bool flag = test != null && test.isActiveAndEnabled;
			if (!num && !flag)
			{
				return false;
			}
			hasEmbeddedPreview = flag && test.previewMode && (_test == null || _test.Pointer == test.Pointer);
			reason = (hasEmbeddedPreview ? "embedded editor preview is active" : "scenario playback controller is active");
			return true;
		}
		catch (Exception error)
		{
			hasEmbeddedPreview = false;
			ReportFailure("preview", error);
			reason = "preview or animation state is unknown";
			return true;
		}
	}

	private void InvalidateReferences()
	{
		_lastDiscoveryTicks = 0L;
		_studio = null;
		_test = null;
		_inspector = null;
		_catalog = null;
		_loading = null;
		_windowManager = null;
		_resources = null;
	}

	private void ReportFailure(string context, Exception error)
	{
		long timestamp = Stopwatch.GetTimestamp();
		if (_lastFailureReportTicks == 0L || timestamp - _lastFailureReportTicks >= 5 * Stopwatch.Frequency)
		{
			_lastFailureReportTicks = timestamp;
			_report?.Invoke(context + " probe failed closed: " + error.GetType().Name);
		}
	}
}
