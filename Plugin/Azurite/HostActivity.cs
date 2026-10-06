using System;
using System.Diagnostics;
using AzureArchive.Automation;
using Il2CppSystem.Collections.Generic;
using Studio.Scripts;
using Studio.Scripts.Window;
using BackgroundExplorerWindow = Studio.Scripts.Window.BackgroundExplorer.BackgroundExplorer;
using BgmExplorerWindow = Studio.Scripts.Window.BGMExplorer.BGMExplorer;
using CharacterExplorerWindow = Studio.Scripts.Window.CharacterExplorer.CharacterExplorer;
using EmotionExplorerWindow = Studio.Scripts.Window.EmotionExplorer.EmotionExplorer;
using PopupImageExplorerWindow = Studio.Scripts.Window.PopupImageExplorer.PopupImageExplorer;
using SoundExplorerWindow = Studio.Scripts.Window.SoundExplorer.SoundExplorer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Azurite;

internal sealed class HostActivity
{
	private readonly Action<string>? _report;

	private long _lastDiscoveryTicks;

	private long _lastFailureReportTicks;

	private long _inactiveLoadingSinceTicks;

	private int _inactiveLoadingCount = -1;

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

	public HostActivitySnapshot Observe(bool allowPreviewIdle = false, bool scrollProtected = false, bool allowAmbientPreview = false)
	{
		bool hasEmbeddedPreview = false;
		try
		{
			string reason;
			bool flag = HasRawInputOrSurfaceChange(out reason);
			RefreshReferencesAtMostOncePerSecond();
			if (!TryIdentifyStaticSurface(out string reason3, out UIScrollView catalogScroll, out UITable catalogTable, out bool editorSurface))
			{
				return HostActivitySnapshot.Unknown(reason3, hasEmbeddedPreview);
			}
			if (HasPreviewOrAnimation(editorSurface, allowAmbientPreview, out string reason2, out hasEmbeddedPreview, out bool ambientPreview))
			{
				return HostActivitySnapshot.Blocked(reason2, isEditor: true, hasPreview: hasEmbeddedPreview, hasDynamicPreview: hasEmbeddedPreview);
			}
			if (hasEmbeddedPreview && (_studio == null || !_studio.isActiveAndEnabled || _studio.entryNode == null))
			{
				return HostActivitySnapshot.Blocked("embedded preview studio or entry node is unavailable", isEditor: true, hasPreview: true);
			}
			if (hasEmbeddedPreview && (_inspector == null || !_inspector.isActiveAndEnabled || _inspector.preview == null))
			{
				return HostActivitySnapshot.Blocked("embedded preview inspector is unavailable", isEditor: true, hasPreview: true);
			}
			if (HasBlockingWindow(out string reason5))
			{
				return HostActivitySnapshot.Blocked(reason5, isEditor: true, hasEmbeddedPreview);
			}
			if (HasLoadingOrSaving(catalogTable, out string reason6))
			{
				return HostActivitySnapshot.Blocked(reason6, isEditor: true, hasEmbeddedPreview);
			}
			if (flag)
			{
				return new HostActivitySnapshot(true, false, true, reason, hasEmbeddedPreview, ambientPreview, hasEmbeddedPreview && !ambientPreview);
			}
			if (HasInputOrIme(out string reason4))
			{
				return HostActivitySnapshot.Blocked(reason4, isEditor: true, hasEmbeddedPreview);
			}
			if (scrollProtected) return new HostActivitySnapshot(true, false, true, "editor scroll protection is active", hasEmbeddedPreview, ambientPreview, hasEmbeddedPreview && !ambientPreview);
			if (HasScrollMotion(catalogScroll, out string reason7))
			{
				return new HostActivitySnapshot(true, false, true, reason7, hasEmbeddedPreview, ambientPreview, hasEmbeddedPreview && !ambientPreview);
			}
			if (ambientPreview) return HostActivitySnapshot.Ambient("embedded preview ambient character animation");
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

	private bool TryIdentifyStaticSurface(out string reason, out UIScrollView? catalogScroll, out UITable? catalogTable, out bool editorSurface)
	{
		catalogScroll = null;
		catalogTable = null;
		editorSurface = false;
		if (_studio != null && _studio.isActiveAndEnabled)
		{
			if (_studio.entryNode == null || !string.IsNullOrEmpty(_studio.sessionInitializationError))
			{
				reason = "node editor is not ready";
				return false;
			}
			reason = "static node editor surface observed";
			editorSurface = true;
			return true;
		}
		if (HasKnownStaticEditorWindow())
		{
			reason = "known static editor window observed";
			editorSurface = true;
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
		editorSurface = true;
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
				if (IsKnownStaticEditorWindow(_windowManager.activeWindow))
				{
					reason = "known static editor window is active";
					return false;
				}
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

	private bool HasKnownStaticEditorWindow()
	{
		try
		{
			return _windowManager?.activeWindow != null &&
				IsKnownStaticEditorWindow(_windowManager.activeWindow);
		}
		catch
		{
			return false;
		}
	}

	private static bool IsKnownStaticEditorWindow(IWindow window)
	{
		// IWindow is an IL2CPP interface wrapper. GetType() reports the managed
		// wrapper type, so name matching can miss the concrete active window.
		IntPtr pointer = window.Pointer;
		if (pointer == IntPtr.Zero)
		{
			return false;
		}
		return Matches(pointer, Singleton<BackgroundExplorerWindow>.Instance) ||
			Matches(pointer, Singleton<PopupImageExplorerWindow>.Instance) ||
			Matches(pointer, Singleton<SoundExplorerWindow>.Instance) ||
			Matches(pointer, Singleton<BgmExplorerWindow>.Instance) ||
			Matches(pointer, Singleton<EmotionExplorerWindow>.Instance) ||
			Matches(pointer, Singleton<CharacterExplorerWindow>.Instance);
	}

	private static bool Matches(IntPtr pointer, Component? window)
	{
		return pointer != IntPtr.Zero && window != null && window.Pointer == pointer &&
			window.gameObject.activeInHierarchy;
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
				ResetInactiveLoadingObservation();
				reason = "global loading tags are active or unknown";
				return true;
			}
			if (loadingCount != 0)
			{
				GameObject loadingObject = _loading.gameObject;
				bool loadingComponentActive = _loading.isActiveAndEnabled;
				bool loadingObjectActive = loadingObject != null && loadingObject.activeInHierarchy;
				bool inactiveCounterStale = IsInactiveLoadingCounterStale(loadingCount, loadingComponentActive, loadingObjectActive);
				if (!AllowLegacyLoadingException && !inactiveCounterStale)
				{
					reason = $"global loading count={loadingCount}; componentActive={loadingComponentActive}; objectActive={loadingObjectActive}";
					return true;
				}
				if (AllowLegacyLoadingException && !inactiveCounterStale && (loadingCount != 1 || !(catalogTable != null) || (!(_studio == null) && _studio.isActiveAndEnabled) || loadingComponentActive || loadingObject == null || loadingObjectActive))
				{
					reason = $"global loading count={loadingCount}; componentActive={loadingComponentActive}; objectActive={loadingObjectActive}";
					return true;
				}
			}
			else
			{
				ResetInactiveLoadingObservation();
			}
			if (_catalog != null && _catalog.isActiveAndEnabled && (OptionalHostActivity.SearchPending(_catalog) || (catalogTable != null && catalogTable.mReposition)))
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

	private bool IsInactiveLoadingCounterStale(int loadingCount, bool loadingComponentActive, bool loadingObjectActive)
	{
		if (loadingCount != 1 || loadingComponentActive || loadingObjectActive || _resources == null || _resources.Preloading || !_resources.AreDbsLoaded)
		{
			ResetInactiveLoadingObservation();
			return false;
		}
		long timestamp = Stopwatch.GetTimestamp();
		if (_inactiveLoadingCount != loadingCount || _inactiveLoadingSinceTicks == 0L)
		{
			_inactiveLoadingCount = loadingCount;
			_inactiveLoadingSinceTicks = timestamp;
			return false;
		}
		return timestamp - _inactiveLoadingSinceTicks >= Stopwatch.Frequency;
	}

	private void ResetInactiveLoadingObservation()
	{
		_inactiveLoadingCount = -1;
		_inactiveLoadingSinceTicks = 0L;
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

	private bool HasPreviewOrAnimation(bool editorSurface, bool allowAmbient, out string reason, out bool hasEmbeddedPreview, out bool ambientPreview)
	{
		reason = string.Empty;
		hasEmbeddedPreview = false;
		ambientPreview = false;
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
			hasEmbeddedPreview = flag && test.previewMode;
			// The singleton and inspector preview can be different native instances.
			// Inspect both; a resident idle singleton cannot hide a playing preview.
			var activity = num ? ObserveProducers(_test, editorSurface, allowAmbient && hasEmbeddedPreview && _test.Pointer == test.Pointer) : CharacterAnimationActivity.Static;
			if (flag && (!num || _test.Pointer != test.Pointer))
			{
				var previewActivity = ObserveProducers(test, editorSurface, allowAmbient && hasEmbeddedPreview);
				if ((int)previewActivity > (int)activity) activity = previewActivity;
			}
			ambientPreview = hasEmbeddedPreview && activity == CharacterAnimationActivity.Ambient;
			if (activity != CharacterAnimationActivity.Protected)
			{
				reason = hasEmbeddedPreview ? "static embedded editor preview" : "idle editor controller";
				return false;
			}
			reason = (hasEmbeddedPreview ? "embedded editor preview has active producers" : "scenario playback controller is active");
			return true;
		}
		catch (Exception error)
		{
			hasEmbeddedPreview = false;
			ambientPreview = false;
			ReportFailure("preview", error);
			reason = "preview or animation state is unknown";
			return true;
		}
	}

	private static CharacterAnimationActivity ObserveProducers(Test controller, bool editorSurface, bool allowAmbient)
	{
		// Read cheap active flags before walking resident native animation lists.
		if ((!editorSurface && !controller.previewMode) || controller.auto || controller.hasVoice ||
			controller.delayedAdvanceTask != null || OptionalHostActivity.CurrentEffectActive(controller) || OptionalHostActivity.CustomEffectActive(controller)) return CharacterAnimationActivity.Protected;
		if (AnimationActivity.CountPending(controller.currentAnims) != 0 ||
			AnimationActivity.CountPending(controller.backgroundAnimations) != 0 || AnimationActivity.CountPending(controller.currentSTs) != 0) return CharacterAnimationActivity.Protected;
		return CharacterActivity.Observe(controller, allowAmbient);
	}

	private void InvalidateReferences()
	{
		ResetInactiveLoadingObservation();
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
