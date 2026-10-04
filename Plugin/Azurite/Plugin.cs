using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Azurite.Core;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Azurite;

[BepInPlugin("halocue.azurite", "Azurite · 蓝铜矿", "0.7.2")]
[BepInProcess("AzureArchive.exe")]
public sealed class Plugin : BasePlugin
{
	public const string Id = "halocue.azurite";

	public const string Version = "0.7.2";

	private static Plugin? _current;

	private ConfigEntry<bool> _enabled;

	private ConfigEntry<bool> _measuredCadence;

	private ConfigEntry<bool> _diagnostics;

	private ConfigEntry<bool> _frameTimings;

	private ConfigEntry<bool> _fpsMapping;

	private ConfigEntry<bool> _profileEditor;

	private ConfigEntry<bool> _profileOperations;

	private ConfigEntry<bool> _matchPreview;

	private ConfigEntry<bool> _limitPreview;

	private ConfigEntry<bool> _profileListItems;

	private ConfigEntry<bool> _adaptivePreview;

	private ConfigEntry<bool> _plainTextDispatch;

	private ConfigEntry<bool> _layoutCacheEnabled;

	private ConfigEntry<bool> _panelWorkEnabled;

	private ConfigEntry<bool> _panelWorkDryRun;

	private ConfigEntry<double> _previewIdleFps;

	private ConfigEntry<double> _previewFps;

	private ConfigEntry<bool> _repaintOnChange;

	private ConfigEntry<int> _idleInterval;

	private ConfigEntry<int> _deepInterval;

	private ConfigEntry<double> _idleDelay;

	private ConfigEntry<double> _deepDelay;

	private ConfigEntry<double> _idleFps;

	private ConfigEntry<double> _deepFps;

	private ConfigEntry<float> _previewScale;

	private ConfigEntry<float> _wheelMultiplier;

	private ConfigEntry<float> _dialogueWheelMultiplier;

	private ConfigEntry<float> _modManagerWheelMultiplier;

	private ConfigEntry<float> _backgroundWheelMultiplier;

	private ConfigEntry<double> _safetyRepaintFps;

	private ConfigEntry<double> _repaintSettleSeconds;

	private ConfigEntry<double> _idleAnimationFps;

	private Harmony? _harmony;

	private HostCompatibility? _host;

	private HostActivity? _activity;

	private FpsController? _fps;

	private EditorScrollTuning? _scroll;

	private ScrollActivityGuard? _scrollActivity;

	private bool _scrollProtected;

	private UiRepaintObserver? _repaint;

	private EditorWorkProbe? _editorWork;

	private ListViewportDiagnostics? _listViewport;

	private double _nextConfigProbe;

	private DateTime _configStamp;

	private EditorOperationProfiler? _operations;

	private PlainTextLayoutFastPath? _plainText;

	private DialogueTextLayoutCache? _layoutCache;

	private DialoguePanelWorkCulling? _panelWork;

	private PreviewTargetLease? _previewTarget;

	private PreviewCameraCadence? _previewCadence;

	private bool _previewAllowed;

	private readonly AdaptivePolicy _policy = new AdaptivePolicy();

	private readonly FrameRateEstimator _rate = new FrameRateEstimator();

	private readonly VisualCadence _visualCadence = new VisualCadence();

	private RenderIntervalLease? _intervalLease;

	private RenderScaleLease? _scaleLease;

	private GameObject? _driverObject;

	private AzuriteDriver? _driver;

	private bool _disabledForSession;

	private bool _exportSuspended;

	private bool _mappingAllowed;

	private bool _pipelineLogged;

	private bool _timingUnavailable;

	private bool _wasEnabled = true;

	private bool _labelsMapped;

	private bool _fpsLabelPostfixReady;

	private double _nextLabelProbe;

	private bool _usingRepaint;

	private double _nextPipelineProbe;

	private double _nextTiming;

	private double _nextSummary;

	private int _pipelineAttempts;

	private int _updates;

	private int _scheduledRenders;

	private RenderMode _mode;

	private string _reason = "starting";

	private int _actualInterval = 1;

	private double _summaryStarted;

	private static double Now => (double)Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

	private bool ShouldMapLabels
	{
		get
		{
			if (!_disabledForSession && _enabled.Value && _fpsMapping.Value)
			{
				return _fps?.IsMappingApplied ?? false;
			}
			return false;
		}
	}

	public override void Load()
	{
		_current = this;
		_enabled = base.Config.Bind("Adaptive Rendering", "Enabled", defaultValue: true, "Enable Azurite scheduling and frame-rate mapping. Disable restores owned settings.");
		_measuredCadence = base.Config.Bind("Adaptive Rendering", "UseMeasuredCadence", defaultValue: true, "Derive idle draw intervals from measured player-loop FPS. Works with 60, 120 and unlimited; never imposes a foreground cap.");
		_idleFps = base.Config.Bind("Adaptive Rendering", "IdleRenderFps", 30.0, "Approximate draw FPS during confirmed static editing. Integer frame intervals allow a 3% tolerance.");
		_deepFps = base.Config.Bind("Adaptive Rendering", "DeepIdleRenderFps", 15.0, "Approximate draw FPS after prolonged static editing. Must not exceed IdleRenderFps.");
		_idleInterval = base.Config.Bind("Adaptive Rendering", "IdleRenderInterval", 2, "Legacy manual interval, only used when UseMeasuredCadence=false.");
		_deepInterval = base.Config.Bind("Adaptive Rendering", "DeepIdleRenderInterval", 4, "Legacy manual deep interval, only used when UseMeasuredCadence=false.");
		_idleDelay = base.Config.Bind("Adaptive Rendering", "IdleDelaySeconds", 2.0, "Seconds without input or protected work before idle drawing.");
		_deepDelay = base.Config.Bind("Adaptive Rendering", "DeepIdleDelaySeconds", 8.0, "Seconds of confirmed static editing before deep idle.");
		_fpsMapping = base.Config.Bind("Frame Rate", "MapNativeTiers", defaultValue: true, "AA maximum-FPS choices: 60 / 120 / unlimited. Does not change monitor Hz. Export retains control.");
		_previewScale = base.Config.Bind("Preview", "RenderScale", 1f, "Optional experimental global URP quality reduction for embedded editor preview only. 1 preserves native quality. Restored before export.");
		_matchPreview = base.Config.Bind("Preview", "MatchDisplayResolution", defaultValue: false, "Use a separately owned preview texture matching its screen area. Experimental; original texture and export restored.");
		_limitPreview = base.Config.Bind("Preview", "LimitCameraCadence", defaultValue: false, "Reduce only embedded preview camera redraws. Editor UI and simulation retain normal updates; experimental until measured.");
		_previewFps = base.Config.Bind("Preview", "CameraFps", 60.0, "Embedded preview draw target, 30..120. Does not cap editor UI, logical time or export.");
		_adaptivePreview = base.Config.Bind("Preview", "AdaptiveEditorIdle", defaultValue: false, "Reduce whole editor drawing only while a verified embedded preview is idle. Any input/scroll/loading restores normal rendering; appreciation and export excluded.");
		_previewIdleFps = base.Config.Bind("Preview", "EditorIdleFps", 60.0, "Idle embedded-editor render target, 30..120. Input keeps the selected 60/120/unlimited tier.");
		_repaintOnChange = base.Config.Bind("On Change Rendering", "Enabled", defaultValue: true, "Wake static editor/catalog rendering on UI geometry, clipping, surface or input changes. Unverified dynamic work uses normal rendering.");
		_safetyRepaintFps = base.Config.Bind("On Change Rendering", "SafetyRepaintFps", 1.0, "Safety redraw rate for static surfaces: 1..15. Retained because custom shaders/textures do not expose complete invalidation. This is not zero rendering.");
		_idleAnimationFps = base.Config.Bind("On Change Rendering", "IdleAnimationFps", 30.0, "Draw FPS for changing UI while the user is idle. 15..60. Active input and protected previews retain selected 60/120/unlimited cadence.");
		_repaintSettleSeconds = base.Config.Bind("On Change Rendering", "SettleSeconds", 0.35, "Keep normal rendering briefly after visual changes so UI transitions finish. Range 0.1..5 seconds.");
		_wheelMultiplier = base.Config.Bind("Editor Scrolling", "WheelMultiplier", 3f, "Wheel travel multiplier for catalog and property tabs. 1 restores native behavior; capped at 6. Does not alter drag distance.");
		_dialogueWheelMultiplier = base.Config.Bind("Editor Scrolling", "DialogueWheelMultiplier", 4.5f, "Independent wheel travel multiplier for the left dialogue list. 1 restores native behavior; capped at 6.");
		_modManagerWheelMultiplier = base.Config.Bind("Editor Scrolling", "ModManagerWheelMultiplier", 4.5f, "Independent wheel travel multiplier for the Mod manager list. 1 restores native behavior; capped at 6.");
		_backgroundWheelMultiplier = base.Config.Bind("Editor Scrolling", "BackgroundWheelMultiplier", 4.5f, "Independent wheel travel for background thumbnails and categories. 1 restores native behavior; capped at 6.");
		_diagnostics = base.Config.Bind("Diagnostics", "LogStateChanges", defaultValue: false, "Every 5s record measured Update FPS, scheduled draw FPS, mode and actual settings. Scheduled draws are not completed GPU frames.");
		_frameTimings = base.Config.Bind("Diagnostics", "CollectFrameTimings", defaultValue: false, "Optional Unity CPU/GPU timing support; unsupported counters are reported, not treated as zero work.");
		_profileEditor = base.Config.Bind("Diagnostics", "ProfileEditor", defaultValue: false, "Read-only editor load, Update-gap, list and preview render-target diagnostics. Does not change project data.");
		_profileOperations = base.Config.Bind("Diagnostics", "ProfileOperations", defaultValue: false, "Opt-in native authoring method wall-time probes for a verified host. Diagnostic only; disabled by default.");
		_profileListItems = base.Config.Bind("Diagnostics", "ProfileListItems", defaultValue: false, "Additional opt-in timings for list item initialization, refresh and ordering. Disabled by default.");
		_plainTextDispatch = base.Config.Bind("Large Projects", "PlainTextDispatch", defaultValue: true, "Avoid size/ruby regex dispatch for plain dialogue-row text; native wrapping and text layout are preserved. Rich text uses the native parser.");
		_layoutCacheEnabled = base.Config.Bind("Large Projects", "ReuseUnchangedTextLayout", defaultValue: false, "Reuse an unchanged existing row layout only when all inputs, output objects and shared NGUI state match. Does not accelerate first layout.");
		_panelWorkEnabled = base.Config.Bind("Large Projects", "OffscreenTextPanelWork", defaultValue: false, "Experimental scoped CPU update reduction for empty offscreen dialogue text panels. Requires verified host and uniform geometry.");
		_panelWorkDryRun = base.Config.Bind("Large Projects", "OffscreenTextPanelDryRun", defaultValue: true, "Observe eligible text panel updates without skipping them. Use to verify the experiment before activation.");
		base.Config.Bind("Export Compatibility", "SkipDuplicateAutomaticCapture", defaultValue: false, "Retired experiment; ignored in 0.2.0.");
		_harmony = new Harmony("halocue.azurite");
		_intervalLease = new RenderIntervalLease(() => OnDemandRendering.renderFrameInterval, delegate(int value)
		{
			OnDemandRendering.renderFrameInterval = value;
		});
		_scaleLease = new RenderScaleLease();
		_activity = new HostActivity(delegate(string message)
		{
			base.Log.LogWarning(message);
		});
		_host = new HostCompatibility(base.Log, SuspendForExport, OnExportReleased);
		try
		{
			_fps = new FpsController(base.Log, Wake);
			_scroll = new EditorScrollTuning(delegate(string message)
			{
				base.Log.LogInfo(message);
			});
			_scrollActivity = new ScrollActivityGuard(WakeForScroll, delegate(string message)
			{
				base.Log.LogWarning(message);
			});
			_repaint = new UiRepaintObserver(delegate(string message)
			{
				base.Log.LogWarning(message);
			});
			_editorWork = new EditorWorkProbe(delegate(string message)
			{
				base.Log.LogInfo(message);
			});
			_listViewport = new ListViewportDiagnostics(delegate(string message)
			{
				base.Log.LogInfo(message);
			});
			_configStamp = File.GetLastWriteTimeUtc(base.Config.ConfigFilePath);
			_previewTarget = new PreviewTargetLease(delegate(string message)
			{
				base.Log.LogInfo(message);
			});
			_previewCadence = new PreviewCameraCadence(delegate(string message)
			{
				base.Log.LogInfo(message);
			});
			bool num = _host.Initialize(_harmony);
			_activity.AllowLegacyLoadingException = _host.Profile.LegacyLoadingException;
			bool nativePatches = num && _host.Profile.NativePatches;
			if (nativePatches && _plainTextDispatch.Value)
			{
				_plainText = new PlainTextLayoutFastPath(delegate(string message)
				{
					base.Log.LogInfo(message);
				});
				_plainText.Install();
			}
			if (nativePatches && _layoutCacheEnabled.Value)
			{
				_layoutCache = new DialogueTextLayoutCache(delegate(string message)
				{
					base.Log.LogInfo(message);
				});
				_layoutCache.Install();
			}
			if (nativePatches && _panelWorkEnabled.Value)
			{
				_panelWork = new DialoguePanelWorkCulling(delegate(string message)
				{
					base.Log.LogInfo(message);
				});
				_panelWork.Install();
			}
			if (nativePatches && _profileOperations.Value)
			{
				_operations = new EditorOperationProfiler(delegate(string message)
				{
					base.Log.LogInfo(message);
				});
				_operations.Install(_profileListItems.Value);
			}
			if (nativePatches)
			{
				MethodInfo updateWidgets = RequiredSettingsPostfixTarget("UpdateWidgets");
				_harmony.Patch(updateWidgets, null, new HarmonyMethod(typeof(Plugin), nameof(AfterSettingsUpdated)));
			}
			else if (_host.Profile.FpsLabelPostfix)
			{
				try
				{
					MethodInfo updateWidgets = RequiredSettingsPostfixTarget("UpdateWidgets");
					MethodInfo fpsChanged = RequiredSettingsPostfixTarget("OnFpsSliderChanged");
					var postfix = new HarmonyMethod(typeof(Plugin), nameof(AfterSettingsUpdated));
					_harmony.Patch(updateWidgets, null, postfix);
					_harmony.Patch(fpsChanged, null, postfix);
					_fpsLabelPostfixReady = HasOwnPostfix(updateWidgets) && HasOwnPostfix(fpsChanged);
					if (!_fpsLabelPostfixReady)
						base.Log.LogWarning("Frame-rate label callbacks were not registered; using the slower compatibility refresh.");
				}
				catch (Exception ex) { base.Log.LogWarning("Frame-rate label callbacks unavailable: " + ex.GetType().Name + "; using the slower compatibility refresh."); }
			}
			if (!ClassInjector.IsTypeRegisteredInIl2Cpp<AzuriteDriver>())
			{
				ClassInjector.RegisterTypeInIl2Cpp<AzuriteDriver>();
			}
			_driverObject = new GameObject("Azurite.Driver");
			UnityEngine.Object.DontDestroyOnLoad(_driverObject);
			_driver = _driverObject.AddComponent<AzuriteDriver>();
			_driver.Tick = Tick;
			_driver.LateTick = LateTick;
			_driver.Destroyed = delegate
			{
				DisableForSession("driver destroyed");
			};
			_summaryStarted = Now;
			ManualLogSource log = base.Log;
			bool isEnabled;
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(99, 4, out isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("Azurite ");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Version);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(": measured cadence enabled=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_measuredCadence.Value);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; active 60/120/unlimited; on-change=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_repaintOnChange.Value);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; static safety redraw=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_safetyRepaintFps.Value);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("fps.");
			}
			log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		}
		catch (Exception ex)
		{
			DisableForSession("load failed: " + ex);
		}
	}

	private void Tick(double now)
	{
		if (_disabledForSession || _host == null || _activity == null || _intervalLease == null)
		{
			return;
		}
		_previewCadence?.Restore();
		_previewAllowed = false;
		if (!_profileOperations.Value && _operations != null)
		{
			_operations.Dispose();
			_operations = null;
		}
		_operations?.Update(now);
		if (_profileEditor.Value)
		{
			if (now >= _nextConfigProbe)
			{
				_nextConfigProbe = now + 2.0;
				DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(base.Config.ConfigFilePath);
				if (lastWriteTimeUtc != _configStamp)
				{
					base.Config.Reload();
					_configStamp = lastWriteTimeUtc;
					base.Log.LogInfo("Editor diagnostic configuration reloaded.");
				}
			}
			_editorWork?.Update(now);
			EditorRenderProbe.TryLog(base.Log);
			_listViewport?.Update(now);
		}
		if (!_enabled.Value)
		{
			if (_wasEnabled)
			{
				if (_plainText != null)
				{
					_plainText.Enabled = false;
				}
				_layoutCache?.Suspend();
				_panelWork?.Restore();
				_fps?.Dispose();
				_fps = null;
				_scroll?.Restore();
				RestoreRendering();
				Wake();
				RefreshNativeLabels();
				_wasEnabled = false;
			}
			_mappingAllowed = false;
			_usingRepaint = false;
			_mode = RenderMode.Disabled;
			_reason = "disabled";
			return;
		}
		if (!_wasEnabled)
		{
			_wasEnabled = true;
			_fps = new FpsController(base.Log, Wake);
			Wake();
			RefreshNativeLabels();
		}
		HostCompatibilitySnapshot hostCompatibilitySnapshot = _host.Probe();
		if (_plainText != null)
		{
			_plainText.Enabled = _plainTextDispatch.Value && _host.Profile.NativePatches && !hostCompatibilitySnapshot.ExportActive;
			_plainText.Update(now);
		}
		_layoutCache?.Update(now, _layoutCacheEnabled.Value && _host.Profile.NativePatches && !hostCompatibilitySnapshot.ExportActive);
		if (_panelWork != null)
		{
			_panelWork.Enabled = _panelWorkEnabled.Value;
			_panelWork.DryRun = _panelWorkDryRun.Value;
			_panelWork.Update(now, _host.Profile.NativePatches && hostCompatibilitySnapshot.ProbeHealthy && !hostCompatibilitySnapshot.ExportActive);
		}
		_scroll?.Update(now, _host.HostSupported, _wheelMultiplier.Value, _dialogueWheelMultiplier.Value, _modManagerWheelMultiplier.Value, _backgroundWheelMultiplier.Value);
		_mappingAllowed = hostCompatibilitySnapshot.Supported && hostCompatibilitySnapshot.ProbeHealthy && !hostCompatibilitySnapshot.ExportActive;
		if (!_mappingAllowed)
		{
			_scrollProtected = false;
			_usingRepaint = false;
			RestoreRendering();
			_policy.Reset(now);
			_rate.Reset();
			_mode = ((!hostCompatibilitySnapshot.ExportActive) ? RenderMode.Incompatible : RenderMode.Protected);
			_reason = hostCompatibilitySnapshot.Reason;
			return;
		}
		_exportSuspended = false;
		_rate.Observe(now);
		_scrollProtected = _scrollActivity?.Observe(now, Application.isFocused, sampleViewportMotion: false) ?? false;
		bool allowPreviewOptimization = _host.Profile.PreviewOptimization;
		HostActivitySnapshot hostActivitySnapshot = _activity.Observe(allowPreviewOptimization && _adaptivePreview.Value, _scrollProtected);
		_previewAllowed = hostActivitySnapshot.HasPreview;
		_previewTarget?.Update(now, allowPreviewOptimization && _matchPreview.Value && _previewAllowed);
		if (_repaintOnChange.Value && (hostActivitySnapshot.CanThrottle || _profileEditor.Value))
		{
			_repaint?.Update(now);
		}
		bool dirty = _repaintOnChange.Value && (_repaint?.ConsumeDirty() ?? false);
		if (allowPreviewOptimization && _previewScale.Value < 0.999f && hostActivitySnapshot.HasPreview)
		{
			_scaleLease?.TryApply(_previewScale.Value);
		}
		else
		{
			_scaleLease?.Restore();
		}
		double value = _safetyRepaintFps.Value;
		double value2 = _repaintSettleSeconds.Value;
		VisualCadencePlan visualCadencePlan = _visualCadence.Resolve(now, dirty, value, _idleAnimationFps.Value, value2);
		int usingRepaint;
		if (_repaintOnChange.Value)
		{
			UiRepaintObserver? repaint = _repaint;
			if (repaint != null && repaint.IsReady)
			{
				usingRepaint = (visualCadencePlan.Valid ? 1 : 0);
				goto IL_052e;
			}
		}
		usingRepaint = 0;
		goto IL_052e;
		IL_052e:
		_usingRepaint = (byte)usingRepaint != 0;
		bool flag = allowPreviewOptimization && _adaptivePreview.Value && hostActivitySnapshot.HasPreview && hostActivitySnapshot.CanThrottle && double.IsFinite(_previewIdleFps.Value) && _previewIdleFps.Value >= 30.0 && _previewIdleFps.Value <= 120.0;
		CadencePlan cadencePlan = (flag ? CadenceResolver.Resolve(_rate.IsReady ? _rate.FramesPerSecond : double.NaN, _previewIdleFps.Value, _previewIdleFps.Value, 4096) : (_usingRepaint ? CadenceResolver.Resolve(_rate.IsReady ? _rate.FramesPerSecond : double.NaN, visualCadencePlan.RenderFps, visualCadencePlan.RenderFps, 4096) : (_measuredCadence.Value ? CadenceResolver.Resolve(_rate.IsReady ? _rate.FramesPerSecond : double.NaN, _idleFps.Value, _deepFps.Value) : new CadencePlan(_idleInterval.Value >= 1 && _deepInterval.Value >= _idleInterval.Value, _idleInterval.Value, _deepInterval.Value, "manual-intervals"))));
		bool flag2 = _scrollProtected || !hostActivitySnapshot.IsEditor || !hostActivitySnapshot.CanThrottle || !cadencePlan.Valid;
		RenderDecision renderDecision = _policy.Update(now, enabled: true, compatible: true, flag2, hostActivitySnapshot.HasInteraction, Application.isFocused, (!cadencePlan.Valid) ? 1 : cadencePlan.IdleInterval, (!cadencePlan.Valid) ? 1 : cadencePlan.DeepInterval, _usingRepaint ? value2 : _idleDelay.Value, _usingRepaint ? value2 : _deepDelay.Value);
		_mode = renderDecision.Mode;
		_reason = (_scrollProtected ? "scroll input, viewport motion or settle hold" : ((!cadencePlan.Valid) ? cadencePlan.Reason : (flag2 ? hostActivitySnapshot.Reason : ((flag && renderDecision.Interval > 1) ? "idle embedded editor preview" : ((_usingRepaint && renderDecision.Interval > 1) ? visualCadencePlan.Reason : renderDecision.Reason)))));
		if (renderDecision.Interval <= 1)
		{
			_intervalLease.Restore();
		}
		else if (!_intervalLease.TryApply(renderDecision.Interval))
		{
			DisableForSession("render interval changed externally; ownership released");
		}
	}

	private void LateTick(double now)
	{
		if (_disabledForSession)
		{
			return;
		}
		if (!_enabled.Value)
		{
			RecordDiagnostics(now);
			return;
		}
		if (_mappingAllowed && !_exportSuspended)
		{
			_scrollProtected = _scrollActivity?.Observe(now, Application.isFocused, sampleViewportMotion: true) ?? false;
		}
		if (_mappingAllowed && !_exportSuspended && _repaintOnChange.Value)
		{
			UiRepaintObserver? repaint = _repaint;
			if (repaint != null && repaint.ConsumeDirty())
			{
				VisualCadencePlan visualCadencePlan = _visualCadence.Resolve(now, dirty: true, _safetyRepaintFps.Value, _idleAnimationFps.Value, _repaintSettleSeconds.Value);
				if (_usingRepaint && !_previewAllowed && (_mode == RenderMode.Idle || _mode == RenderMode.DeepIdle))
				{
					CadencePlan cadencePlan = CadenceResolver.Resolve(_rate.FramesPerSecond, visualCadencePlan.RenderFps, visualCadencePlan.RenderFps, 4096);
					if (!visualCadencePlan.Valid || !cadencePlan.Valid)
					{
						_intervalLease?.Restore();
					}
					else
					{
						RenderIntervalLease? intervalLease = _intervalLease;
						if (intervalLease != null && !intervalLease.TryApply(cadencePlan.IdleInterval))
						{
							DisableForSession("render interval changed during visual wake");
						}
					}
					_reason = visualCadencePlan.Reason;
				}
			}
		}
		if (_fpsMapping.Value)
		{
			if (_fps == null)
			{
				_fps = new FpsController(base.Log, Wake);
			}
			_fps.Update(now, _mappingAllowed && !_exportSuspended, SceneManager.GetActiveScene().handle);
		}
		else if (_fps != null)
		{
			_fps.Dispose();
			_fps = null;
			Wake();
			RefreshNativeLabels();
		}
		bool shouldMapLabels = ShouldMapLabels;
		if (!_exportSuspended && _mappingAllowed && _labelsMapped != shouldMapLabels)
		{
			_labelsMapped = shouldMapLabels;
			RefreshNativeLabels();
		}
		if (_host != null && _host.HostSupported && _host.Profile.FpsLabelPostfix && !_fpsLabelPostfixReady && !_scrollProtected && now >= _nextLabelProbe)
		{
			_nextLabelProbe = now + 0.5;
			if (ShouldMapLabels) RefreshNativeLabels();
		}
		_previewCadence?.Update(now, _host != null && _host.Profile.PreviewOptimization && _mappingAllowed && !_exportSuspended && !_scrollProtected && _limitPreview.Value, _previewFps.Value);
		RecordDiagnostics(now);
	}

	private void Wake()
	{
		_intervalLease?.Restore();
		_visualCadence.Reset();
		_policy.Reset(Now);
		_rate.Reset();
	}

	private void WakeForScroll()
	{
		_intervalLease?.Restore();
		_previewCadence?.Restore();
		_policy.Reset(Now);
		_mode = RenderMode.Active;
		_reason = "scroll input, viewport motion or settle hold";
	}

	private bool RestoreRendering()
	{
		_previewAllowed = false;
		bool num = _previewCadence?.Restore() ?? true;
		bool flag = _previewTarget?.Restore() ?? true;
		bool flag2 = _intervalLease?.Restore() ?? true;
		bool flag3 = _scaleLease?.Restore() ?? true;
		return num && flag && flag2 && flag3;
	}

	private void SuspendForExport()
	{
		_panelWork?.Restore();
		_layoutCache?.Suspend();
		if (_plainText != null)
		{
			_plainText.Enabled = false;
		}
		_exportSuspended = true;
		_mappingAllowed = false;
		_scroll?.Restore();
		bool num = RestoreRendering();
		bool flag = _fps?.SuspendForExport() ?? true;
		_policy.Reset(Now);
		_rate.Reset();
		if (!num || !flag)
		{
			throw new InvalidOperationException("Azurite could not fully restore its rendering settings before export acquisition.");
		}
	}

	private void OnExportReleased()
	{
		_exportSuspended = false;
		_policy.Reset(Now);
		_rate.Reset();
		_visualCadence.Reset();
	}

	private void RecordDiagnostics(double now)
	{
		_actualInterval = OnDemandRendering.renderFrameInterval;
		if (!_diagnostics.Value && !_frameTimings.Value)
		{
			return;
		}
		_updates++;
		if (OnDemandRendering.willCurrentFrameRender)
		{
			_scheduledRenders++;
		}
		bool isEnabled;
		if (_diagnostics.Value && now >= _nextSummary)
		{
			double num = Math.Max(0.001, now - _summaryStarted);
			ManualLogSource log = base.Log;
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(124, 12, out isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("cadence mode=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_mode);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" reason=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_reason);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; onChange=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_usingRepaint);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; updateFps=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted((double)_updates / num, "F1");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; scheduledDrawFps=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted((double)_scheduledRenders / num, "F1");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; measuredFps=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_rate.FramesPerSecond, "F1");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; interval=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_actualInterval);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; target=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Application.targetFrameRate);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; vsync=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(QualitySettings.vSyncCount);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; size=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Screen.width);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("x");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Screen.height);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; focused=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Application.isFocused);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(".");
			}
			log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			if (_repaint != null)
			{
				ManualLogSource log2 = base.Log;
				bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(34, 3, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("repaint totals=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_repaint.Metrics);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; ready=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_repaint.IsReady);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; healthy=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_repaint.IsHealthy);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(".");
				}
				log2.LogInfo(bepInExInfoLogInterpolatedStringHandler);
				base.Log.LogInfo("repaint panels=" + _repaint.SummarizeDirtyPanels());
			}
			if (_previewCadence != null && _limitPreview.Value)
			{
				ManualLogSource log3 = base.Log;
				bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(73, 5, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("preview camera totals: scheduled=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_previewCadence.ScheduledFrames);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; skipped=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_previewCadence.SkippedFrames);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; target=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_previewFps.Value);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; reason=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_previewCadence.Reason);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; textures=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_previewCadence.TextureCount);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(".");
				}
				log3.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}
			if (_previewTarget != null && _matchPreview.Value)
			{
				ManualLogSource log4 = base.Log;
				bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(35, 2, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("preview target: reason=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_previewTarget.Reason);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; textures=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_previewTarget.TextureCount);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(".");
				}
				log4.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}
			_updates = 0;
			_scheduledRenders = 0;
			_summaryStarted = now;
			_nextSummary = now + 5.0;
		}
		if (!_pipelineLogged && _pipelineAttempts < 4 && now >= _nextPipelineProbe)
		{
			_pipelineAttempts++;
			_nextPipelineProbe = now + 5.0;
			_pipelineLogged = PipelineDiagnostics.TryLog(base.Log);
		}
		if (!_frameTimings.Value || !(now >= _nextTiming))
		{
			return;
		}
		_nextTiming = now + 5.0;
		if (PipelineDiagnostics.TrySample(out var sample))
		{
			ManualLogSource log5 = base.Log;
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(42, 3, out isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("frame timing cpuMain=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(sample.CpuMainThreadMs, "F2");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("ms gpu=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(sample.GpuMs, "F2");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("ms present=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(sample.PresentWaitMs, "F2");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("ms.");
			}
			log5.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		}
		else if (!_timingUnavailable)
		{
			_timingUnavailable = true;
			base.Log.LogInfo("FrameTiming returned no usable sample; GPU timing is unverified. Cadence uses the measured Update loop.");
		}
	}

	private static void AfterSettingsUpdated(SettingPanel __instance)
	{
		Plugin current = _current;
		if (current == null || !current.ShouldMapLabels)
		{
			return;
		}
		try
		{
			SettingPanel.SettingWidgets widgets = __instance.widgets;
			if (widgets != null)
			{
				var mapped = new FpsLabelValues(widgets.fps30Label?.text,
					widgets.fps60Label?.text, widgets.fpsInfLabel?.text).Map(current.ShouldMapLabels);
				if (widgets.fps30Label != null && mapped.Thirty != widgets.fps30Label.text) widgets.fps30Label.text = mapped.Thirty;
				if (widgets.fps60Label != null && mapped.Sixty != widgets.fps60Label.text) widgets.fps60Label.text = mapped.Sixty;
				if (widgets.fpsInfLabel != null && mapped.Infinity != widgets.fpsInfLabel.text) widgets.fpsInfLabel.text = mapped.Infinity;
			}
		}
		catch (Exception ex)
		{
			current.Log.LogWarning("FPS labels unavailable: " + ex.GetType().Name);
		}
	}

	private static MethodInfo RequiredSettingsPostfixTarget(string name)
	{
		MethodInfo method = AccessTools.Method(typeof(SettingPanel), name, Type.EmptyTypes)
			?? throw new MissingMethodException("SettingPanel." + name + "()");
		if (method.IsStatic || method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
			throw new MissingMethodException("SettingPanel." + name + "() has an unsupported signature.");
		return method;
	}

	private bool HasOwnPostfix(MethodInfo method)
	{
		Patches? patches = Harmony.GetPatchInfo(method);
		if (patches == null || _harmony == null) return false;
		foreach (Patch patch in patches.Postfixes)
			if (patch.owner == _harmony.Id) return true;
		return false;
	}

	private static void RefreshNativeLabels()
	{
		try
		{
			foreach (SettingPanel item in UnityEngine.Object.FindObjectsOfType<SettingPanel>())
			{
				if (item != null)
				{
					if (_current?.ShouldMapLabels == true && _current._host?.Profile.NativePatches == false && !_current._fpsLabelPostfixReady)
						AfterSettingsUpdated(item);
					else
						item.UpdateWidgets();
				}
			}
		}
		catch
		{
		}
	}

	private void DisableForSession(string reason)
	{
		if (!_disabledForSession)
		{
			_disabledForSession = true;
			_mappingAllowed = false;
			RestoreRendering();
			_fps?.Dispose();
			_fps = null;
			_scroll?.Dispose();
			_scroll = null;
			_scrollActivity?.Dispose();
			_scrollActivity = null;
			_operations?.Dispose();
			_operations = null;
			_plainText?.Dispose();
			_plainText = null;
			_layoutCache?.Dispose();
			_layoutCache = null;
			_panelWork?.Dispose();
			_panelWork = null;
			_previewCadence?.Dispose();
			_previewCadence = null;
			_previewTarget?.Dispose();
			_previewTarget = null;
			_repaint?.Dispose();
			_repaint = null;
			RefreshNativeLabels();
			base.Log.LogWarning("Azurite scheduling disabled for this session: " + reason);
		}
	}

	public override bool Unload()
	{
		DisableForSession("unload");
		if (_driver != null)
		{
			_driver.Tick = null;
			_driver.LateTick = null;
			_driver.Destroyed = null;
		}
		if (_driverObject != null)
		{
			UnityEngine.Object.Destroy(_driverObject);
		}
		_driver = null;
		_driverObject = null;
		_host?.Dispose();
		_harmony?.UnpatchSelf();
		_current = null;
		return true;
	}
}
