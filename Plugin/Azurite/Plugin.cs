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

[BepInPlugin("halocue.azurite", "Azurite · 蓝铜矿", "1.0.20")]
[BepInProcess("AzureArchive.exe")]
public sealed class Plugin : BasePlugin
{
	public const string Id = "halocue.azurite";

	public const string Version = "1.0.20";

	public const string CandidateBuild = "chooser-multiplier-cap-12-1";

	private static Plugin? _current;

	private ConfigEntry<bool> _enabled;

	private ConfigEntry<bool> _measuredCadence;

	private ConfigEntry<bool> _diagnostics;

	private ConfigEntry<bool> _frameTimings;

	private ConfigEntry<bool> _fpsMapping;

	private ConfigEntry<bool> _cpuPowerSaving;
	private ConfigEntry<int> _cpuForegroundFps;
	private ConfigEntry<int> _cpuBackgroundFps;
	private ConfigEntry<double> _cpuIdleDelay;
	private readonly CpuIdlePolicy _cpuIdle = new();
	private int _requestedCpuFps;

	private ConfigEntry<bool> _profileEditor;

	private ConfigEntry<bool> _profileOperations;

	private ConfigEntry<bool> _matchPreview;

	private ConfigEntry<bool> _experimentalPreviewOwnership;

	private ConfigEntry<bool> _disableEditorBloom;

	private EditorBloomLease? _editorBloom;

	private ConfigEntry<bool> _limitPreview;
	private ConfigEntry<bool> _cacheStaticPreview;
	private bool _previewCacheStatic;
	private long _previewContentGeneration;

	private ConfigEntry<bool> _profileListItems;

	private ConfigEntry<bool> _adaptivePreview;

	private ConfigEntry<bool> _plainTextDispatch;

	private ConfigEntry<bool> _layoutCacheEnabled;

	private ConfigEntry<bool> _panelWorkEnabled;

	private ConfigEntry<bool> _panelWorkDryRun;

	private ConfigEntry<bool> _rowReuseEnabled;

	private ConfigEntry<bool> _virtualizationEnabled;
	private DialogueVirtualization? _virtualization;
	private DeferredEditorSelectors? _deferredSelectors;

	private ConfigEntry<bool> _progressiveEditorLoading;
	private ProgressiveEditorLoading? _progressiveLoading;
	private ConfigEntry<bool> _ambientCharacterPreview;
	private ConfigEntry<double> _ambientCharacterFps;
	private CharacterAnimationMeshCadence? _characterMeshes;
	private bool _ambientObserved;
	private bool _characterMeshAllowed;

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

	private ConfigEntry<float> _settingsWheelMultiplier;

	private ConfigEntry<float> _characterWheelMultiplier;

	private ConfigEntry<float> _emotionWheelMultiplier;

	private ConfigEntry<double> _safetyRepaintFps;

	private ConfigEntry<double> _repaintSettleSeconds;

	private ConfigEntry<double> _mutationSettleSeconds;

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

	private DialogueRowReuse? _rowReuse;

	private DialogueMutationCoordinator? _mutation;

	private PreviewTargetLease? _previewTarget;

	private PreviewCameraCadence? _previewCadence;
	private readonly PreviewTextureRegistry _previewTextures = new();

	private bool _previewAllowed;

	private bool _previewCadenceAllowed;

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

	private int _mappedDisplayRefreshRate;

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
		_measuredCadence = base.Config.Bind("Adaptive Rendering", "UseMeasuredCadence", defaultValue: true, "Derive idle draw intervals from measured player-loop FPS. Works with 60, 120 and unlimited; CPU Power Saving independently lowers the player-loop cap only after confirmed quiet.");
		_idleFps = base.Config.Bind("Adaptive Rendering", "IdleRenderFps", 30.0, "Approximate draw FPS during confirmed static editing. Integer frame intervals allow a 3% tolerance.");
		_deepFps = base.Config.Bind("Adaptive Rendering", "DeepIdleRenderFps", 15.0, "Approximate draw FPS after prolonged static editing. Must not exceed IdleRenderFps.");
		_idleInterval = base.Config.Bind("Adaptive Rendering", "IdleRenderInterval", 2, "Legacy manual interval, only used when UseMeasuredCadence=false.");
		_deepInterval = base.Config.Bind("Adaptive Rendering", "DeepIdleRenderInterval", 4, "Legacy manual deep interval, only used when UseMeasuredCadence=false.");
		_idleDelay = base.Config.Bind("Adaptive Rendering", "IdleDelaySeconds", 2.0, "Seconds without input or protected work before idle drawing.");
		_deepDelay = base.Config.Bind("Adaptive Rendering", "DeepIdleDelaySeconds", 8.0, "Seconds of confirmed static editing before deep idle.");
		_fpsMapping = base.Config.Bind("Frame Rate", "MapNativeTiers", defaultValue: true, "Below 120Hz, use half-rate/full-rate/unlimited; at 120Hz and above, use 60/120/unlimited. Unlimited synchronizes to the display refresh. Export retains control.");
		_cpuPowerSaving = base.Config.Bind("CPU Power Saving", "Enabled", true, "Reduce the player-loop FPS only on confirmed quiet editor surfaces. Input, scrolling, loading, playback and export restore the selected tier.");
		_cpuForegroundFps = base.Config.Bind("CPU Power Saving", "ForegroundIdleFps", 30, "Player-loop FPS while a confirmed static editor is focused. Range 15..60; never raises the selected frame-rate tier.");
		_cpuBackgroundFps = base.Config.Bind("CPU Power Saving", "BackgroundIdleFps", 15, "Player-loop FPS while a confirmed static editor is unfocused. Range 10..ForegroundIdleFps; playback and export excluded.");
		_cpuIdleDelay = base.Config.Bind("CPU Power Saving", "IdleDelaySeconds", 2.0, "Quiet period before lowering CPU update FPS. Range 0.5..30 seconds.");
		_ambientCharacterPreview = base.Config.Bind("Preview", "LimitAmbientCharacterRendering", false, "Experimental editor-only cadence for verified Spine idle/blink animation. Keeps logical animation updates and event timing; input, actions, unknown dynamics and export use normal rendering.");
		_ambientCharacterFps = base.Config.Bind("Preview", "AmbientCharacterFps", 60.0, "Rendered editor animation rate while idle, 30..120. Does not slow logical animation time or apply to export.");
		_virtualizationEnabled = base.Config.Bind("Large Projects", "VirtualizeDialogueList", false, "Experimental verified-host dialogue list virtualization. Maintain a bounded visible row pool; restore the native list before unsupported editor actions and export. Requires matching AA native implementation.");
		_progressiveEditorLoading = base.Config.Bind("Large Projects", "ProgressiveEditorLoading", false, "Experimental verified-host editor loading. Unlocks structure, dialogue editing, and character/emotion selector entry while project assets continue loading; resource-dependent synchronization, environment selectors, preview and export remain gated until ready.");
		_previewScale = base.Config.Bind("Preview", "RenderScale", 1f, "Optional experimental global URP quality reduction for embedded editor preview only. 1 preserves native quality. Restored before export.");
		_disableEditorBloom = base.Config.Bind("Preview", "DisableEditorBloom", defaultValue: false, "Optional editor quality tradeoff: disable the shared UI bloom effect only while the script editor is active. Restored before export and on editor exit. Changes editor appearance; default off.");
		_matchPreview = base.Config.Bind("Preview", "MatchDisplayResolution", defaultValue: false, "Use a separately owned preview texture matching its screen area. Experimental; original texture and export restored.");
		_experimentalPreviewOwnership = base.Config.Bind("Preview", "ExperimentalPreviewOwnership", defaultValue: false, "Opt in to scoped preview texture/camera experiments on a verified 1.0.0-fix host. Also enable MatchDisplayResolution or LimitCameraCadence. Live PreviewScene, URP Base/empty-stack and consumer binding checks still apply; global RenderScale is not enabled by this switch.");
		_limitPreview = base.Config.Bind("Preview", "LimitCameraCadence", defaultValue: false, "Reduce only embedded preview camera redraws. Editor UI and simulation retain normal updates; experimental until measured.");
		_cacheStaticPreview = base.Config.Bind("Preview", "CacheStaticPreview", defaultValue: false, "Experimental: keep a confirmed static embedded preview texture while editor UI scrolls. Content callbacks, camera changes and a safety refresh invalidate the cache. Requires ExperimentalPreviewOwnership and a ready repaint observer; running character animation is excluded.");
		_previewFps = base.Config.Bind("Preview", "CameraFps", 60.0, "Embedded preview draw target, 30..120. Does not cap editor UI, logical time or export.");
		_adaptivePreview = base.Config.Bind("Preview", "AdaptiveEditorIdle", defaultValue: false, "Reduce whole editor drawing only while a verified embedded preview is idle. Any input/scroll/loading restores normal rendering; appreciation and export excluded.");
		_previewIdleFps = base.Config.Bind("Preview", "EditorIdleFps", 60.0, "Idle embedded-editor render target, 30..120. Input keeps the selected 60/120/unlimited tier.");
		_repaintOnChange = base.Config.Bind("On Change Rendering", "Enabled", defaultValue: true, "Wake static editor/catalog rendering on UI geometry, clipping, surface or input changes. Unverified dynamic work uses normal rendering.");
		_safetyRepaintFps = base.Config.Bind("On Change Rendering", "SafetyRepaintFps", 1.0, "Safety redraw rate for static surfaces: 1..15. Retained because custom shaders/textures do not expose complete invalidation. This is not zero rendering.");
		_idleAnimationFps = base.Config.Bind("On Change Rendering", "IdleAnimationFps", 30.0, "Draw FPS for changing UI while the user is idle. 15..60. Active input and protected previews retain selected 60/120/unlimited cadence.");
		_repaintSettleSeconds = base.Config.Bind("On Change Rendering", "SettleSeconds", 0.35, "Keep normal rendering briefly after visual changes so UI transitions finish. Range 0.1..5 seconds.");
		_mutationSettleSeconds = base.Config.Bind("Large Projects", "DialogueMutationSettleSeconds", 0.35, "Keep normal rendering after native dialogue add/delete/sync completes so list layout and selection settle. Native operations remain synchronous.");
		_wheelMultiplier = base.Config.Bind("Editor Scrolling", "WheelMultiplier", 3f, "Wheel travel multiplier for catalog and property tabs. 1 restores native behavior; capped at 6. Does not alter drag distance.");
		_dialogueWheelMultiplier = base.Config.Bind("Editor Scrolling", "DialogueWheelMultiplier", 4.5f, "Independent wheel travel multiplier for the left dialogue list. 1 restores native behavior; capped at 6.");
		_modManagerWheelMultiplier = base.Config.Bind("Editor Scrolling", "ModManagerWheelMultiplier", 4.5f, "Independent wheel travel multiplier for the Mod manager list. 1 restores native behavior; capped at 6.");
		_backgroundWheelMultiplier = base.Config.Bind("Editor Scrolling", "BackgroundWheelMultiplier", 4.5f, "Independent wheel travel for background thumbnails and categories. 1 restores native behavior; capped at 6.");
		_settingsWheelMultiplier = base.Config.Bind("Editor Scrolling", "SettingsWheelMultiplier", 4.5f, "Independent wheel travel for the AA settings panel. 1 restores native behavior; capped at 6.");
		_characterWheelMultiplier = base.Config.Bind("Editor Scrolling", "CharacterWheelMultiplier", 9.0f, "Independent wheel travel for the character chooser. Down scroll moves right; up scroll moves left; capped at 12.");
		_emotionWheelMultiplier = base.Config.Bind("Editor Scrolling", "EmotionWheelMultiplier", 11.25f, "Independent wheel travel for the emotion chooser. Down scroll moves right; up scroll moves left; capped at 12.");
		// Migrate the previous shipped default so an existing profile receives
		// the requested 2.5x chooser sensitivity without manual config editing.
		if (Math.Abs(_characterWheelMultiplier.Value - 4.5f) < 0.001f || Math.Abs(_characterWheelMultiplier.Value - 11.25f) < 0.001f) _characterWheelMultiplier.Value = 9.0f;
		if (Math.Abs(_emotionWheelMultiplier.Value - 4.5f) < 0.001f) _emotionWheelMultiplier.Value = 11.25f;
		_diagnostics = base.Config.Bind("Diagnostics", "LogStateChanges", defaultValue: false, "Every 5s record measured Update FPS, scheduled draw FPS, mode and actual settings. Scheduled draws are not completed GPU frames.");
		_frameTimings = base.Config.Bind("Diagnostics", "CollectFrameTimings", defaultValue: false, "Optional Unity CPU/GPU timing support; unsupported counters are reported, not treated as zero work.");
		_profileEditor = base.Config.Bind("Diagnostics", "ProfileEditor", defaultValue: false, "Read-only editor load, Update-gap, list and preview render-target diagnostics. Does not change project data.");
		_profileOperations = base.Config.Bind("Diagnostics", "ProfileOperations", defaultValue: false, "Opt-in read-only authoring and project-file stage wall-time probes for a verified host. Diagnostic only; disabled by default.");
		_profileListItems = base.Config.Bind("Diagnostics", "ProfileListItems", defaultValue: false, "Additional opt-in timings for list item initialization, refresh and ordering. Disabled by default.");
		_plainTextDispatch = base.Config.Bind("Large Projects", "PlainTextDispatch", defaultValue: true, "Use the verified plain-text dialogue fast path on supported hosts; native wrapping and text layout are preserved. Rich text uses the native parser.");
		_layoutCacheEnabled = base.Config.Bind("Large Projects", "ReuseUnchangedTextLayout", defaultValue: false, "Opt-in experiment: reuse an unchanged existing row layout only when all inputs, output objects and shared NGUI state match. On the 1.0.0-fix host it also requires the mutation gate. Does not accelerate first layout.");
		_panelWorkEnabled = base.Config.Bind("Large Projects", "OffscreenTextPanelWork", defaultValue: false, "Experimental scoped CPU update reduction for empty offscreen dialogue text panels. Requires verified host and uniform geometry.");
		_panelWorkDryRun = base.Config.Bind("Large Projects", "OffscreenTextPanelDryRun", defaultValue: true, "Observe eligible text panel updates without skipping them. Use to verify the experiment before activation.");
		_rowReuseEnabled = base.Config.Bind("Large Projects", "ReuseUnchangedDialogueRows", defaultValue: false, "Experimental: reuse unchanged dialogue row GameObjects during a one-row insert/delete. Requires a verified host, uniform rows and exact native parent/prefab identity; disables itself on any postcondition failure.");
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
			}, _previewTextures);
			_previewCadence = new PreviewCameraCadence(delegate(string message)
			{
				base.Log.LogInfo(message);
			}, _previewTextures);
			bool num = _host.Initialize(_harmony);
			_editorBloom = new EditorBloomLease(message => base.Log.LogInfo(message));
			base.Log.LogInfo("Azurite local candidate build=" + CandidateBuild + "; performance acceptance pending.");
			if (_experimentalPreviewOwnership.Value)
				base.Log.LogInfo("Experimental preview ownership opt-in=" + PreviewOptimizationPolicy.AllowsScopedOwnership(_host.Profile, true) + "; live binding checks remain required; global URP renderScale and renderer features are unchanged.");
			_activity.AllowLegacyLoadingException = _host.Profile.LegacyLoadingException;
			if (num)
			{
				_mutation = new DialogueMutationCoordinator(delegate(string message)
				{
					base.Log.LogInfo(message);
				}, Wake);
				_mutation.Install();
			}
			bool nativePatches = num && _host.Profile.NativePatches;
			if (num && !_host.Profile.LegacyExporter)
			{
				_deferredSelectors = new DeferredEditorSelectors(message => base.Log.LogInfo(message));
				if (!_deferredSelectors.Install())
				{
					_deferredSelectors.Dispose();
					_deferredSelectors = null;
				}
				if (_virtualizationEnabled.Value)
				{
					_virtualization = new DialogueVirtualization(message => base.Log.LogInfo(message));
					_virtualization.Install();
				}
				if (_progressiveEditorLoading.Value && _host.Profile.ProgressiveEditorLoading)
				{
					_progressiveLoading = new ProgressiveEditorLoading(message => base.Log.LogInfo(message));
					if (!_progressiveLoading.Install())
					{
						_progressiveLoading.Dispose();
						_progressiveLoading = null;
					}
				}
				if (_ambientCharacterPreview.Value)
					_characterMeshes = new CharacterAnimationMeshCadence(_harmony,
						() => _characterMeshAllowed && _enabled.Value && _mappingAllowed && !_exportSuspended && !_scrollProtected,
						message => base.Log.LogInfo(message));
			}
			bool plainTextPatch = num && _host.Profile.PlainTextPatch;
			if (plainTextPatch && _plainTextDispatch.Value)
			{
				_plainText = new PlainTextLayoutFastPath(delegate(string message)
				{
					base.Log.LogInfo(message);
				});
				_plainText.Install();
			}
			bool layoutCacheHostAllowed = LargeProjectOptimizationPolicy.CanEnableLayoutCache(num && _host.Profile.Supported, nativePatches, _mutation?.IsInstalled == true, _layoutCacheEnabled.Value, exporting: false);
			if (layoutCacheHostAllowed)
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
				}, _repaint);
				_panelWork.Install();
			}
			if (num && _host.Profile.Supported && _mutation?.IsInstalled == true && _rowReuseEnabled.Value)
			{
				_rowReuse = new DialogueRowReuse(delegate(string message)
				{
					base.Log.LogInfo(message);
				});
				_rowReuse.Install();
			}
			if (num && _host.Profile.Supported && _profileOperations.Value)
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
			_driver.Quitting = delegate
			{
				_virtualization?.AbandonForShutdown();
				_deferredSelectors?.AbandonForShutdown();
			};
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
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; display-relative tiers; unlimited follows display refresh; on-change=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_repaintOnChange.Value);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; static safety redraw=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_safetyRepaintFps.Value);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("fps.");
			}
			log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			if (_characterMeshes != null)
				log.LogInfo($"character mesh cadence installed={_characterMeshes.IsInstalled} ambient={_ambientObserved} skippedMeshes={_characterMeshes.SkippedMeshes}; logical animation remains native.");
		}
		catch (Exception ex)
		{
			DisableForSession("load failed: " + ex);
		}
	}

	private void Tick(double now)
	{
		_characterMeshAllowed = false;
		_ambientObserved = false;
		_requestedCpuFps = 0;
		_previewCacheStatic = false;
		if (_disabledForSession || _host == null || _activity == null || _intervalLease == null)
		{
			return;
		}
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
			// A project already admitted to the editor still needs its resources and
			// native preview guards. Disable future admissions, finish existing work.
			if (_progressiveLoading != null)
			{
				_progressiveLoading.Enabled = false;
				_progressiveLoading.Update(_driver, _exportSuspended);
			}
			if (_wasEnabled)
			{
				if (_plainText != null)
				{
					_plainText.Enabled = false;
				}
				_layoutCache?.Suspend();
				_panelWork?.Restore();
				_rowReuse?.Suspend();
				_virtualization?.Suspend();
				_characterMeshes?.Suspend();
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
		// Progressive loading is editor-only and does not wait for AAVideoExport to
		// bind. Structure/dialogue operations unlock at the editor shell; the host
		// resource probe keeps preview-dependent work conservative until preload is
		// complete. RenderControlV1 ownership pauses the handoff without cancelling
		// the background preload.
		if (_progressiveLoading != null)
		{
			// Turning off the experiment prevents new deferrals; an already deferred
			// project must finish safely rather than abandoning its resource guards.
			_progressiveLoading.Enabled = _progressiveEditorLoading.Value;
			_progressiveLoading.Update(_driver, hostCompatibilitySnapshot.ExportActive || _exportSuspended);
		}
		bool progressiveBusy = _progressiveLoading?.BlocksEditorOperations == true;
		bool mutationProtected = _mutation?.IsBlocking(now, Math.Clamp(_mutationSettleSeconds.Value, 0.1, 5.0)) ?? false;
		if (_virtualization != null)
		{
			_virtualization.Enabled = _virtualizationEnabled.Value;
			_virtualization.Update(now, hostCompatibilitySnapshot.Supported && hostCompatibilitySnapshot.ProbeHealthy && !hostCompatibilitySnapshot.ExportActive && !_exportSuspended && !progressiveBusy);
		}
		bool virtualListActive = _virtualization?.IsActive == true;
		if (_plainText != null)
		{
			_plainText.Enabled = _plainTextDispatch.Value && (_host.Profile.NativePatches || _host.Profile.PlainTextPatch) && !hostCompatibilitySnapshot.ExportActive && !progressiveBusy;
			_plainText.Update(now);
		}
		// Mutations are precisely where existing row layouts can be reused. The
		// cache validates row identity, style and output on each scoped Initialize.
		bool layoutCacheAllowed = !progressiveBusy && LargeProjectOptimizationPolicy.CanEnableLayoutCache(hostCompatibilitySnapshot.Supported, _host.Profile.NativePatches, _mutation?.IsInstalled == true, _layoutCacheEnabled.Value, hostCompatibilitySnapshot.ExportActive);
		_layoutCache?.Update(now, layoutCacheAllowed);
		if (_panelWork != null)
		{
			_panelWork.Enabled = _panelWorkEnabled.Value;
			_panelWork.DryRun = _panelWorkDryRun.Value;
			_panelWork.Update(now, _host.Profile.NativePatches && hostCompatibilitySnapshot.ProbeHealthy && !hostCompatibilitySnapshot.ExportActive && !mutationProtected && !virtualListActive && !progressiveBusy);
		}
		if (_rowReuse != null)
		{
			_rowReuse.Enabled = _rowReuseEnabled.Value && !virtualListActive;
			_rowReuse.Update(now, hostCompatibilitySnapshot.Supported && _mutation?.IsInstalled == true && hostCompatibilitySnapshot.ProbeHealthy && !hostCompatibilitySnapshot.ExportActive && !_exportSuspended && !progressiveBusy);
		}
		_scroll?.Update(now, _host.HostSupported, _wheelMultiplier.Value, _dialogueWheelMultiplier.Value, _modManagerWheelMultiplier.Value, _backgroundWheelMultiplier.Value, _settingsWheelMultiplier.Value, _characterWheelMultiplier.Value, _emotionWheelMultiplier.Value);
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
		HostActivitySnapshot hostActivitySnapshot = _activity.Observe(allowPreviewOptimization && _adaptivePreview.Value, _scrollProtected,
			_ambientCharacterPreview.Value && _characterMeshes?.IsInstalled == true);
		bool progressiveResourcesReady = _progressiveLoading?.ResourceDependentWorkAllowed ?? true;
		_ambientObserved = hostActivitySnapshot.AmbientPreview;
		_previewAllowed = hostActivitySnapshot.HasPreview;
		bool scopedPreviewOwnership = PreviewOptimizationPolicy.AllowsScopedOwnership(_host.Profile, _experimentalPreviewOwnership.Value);
		_editorBloom?.Update(now, _host.Profile.Supported && _disableEditorBloom.Value && !_exportSuspended);
		_previewCacheStatic = _cacheStaticPreview.Value && hostActivitySnapshot.CanCachePreview && _repaintOnChange.Value;
		_previewCadenceAllowed = progressiveResourcesReady && scopedPreviewOwnership && _previewAllowed && (hostActivitySnapshot.CanThrottle || _previewCacheStatic) && !mutationProtected;
		// Resolution ownership is independent of input cadence: restoring and
		// reallocating the target on every wheel event would add avoidable work.
		// The lease validates that the embedded editor preview is still bound.
		_previewTarget?.Update(now, progressiveResourcesReady && scopedPreviewOwnership && _matchPreview.Value && !_exportSuspended && !mutationProtected, _previewCadence?.SuspendedCamera);
		if (_repaintOnChange.Value && (hostActivitySnapshot.CanThrottle || _previewCacheStatic || _profileEditor.Value))
		{
			_repaint?.Update(now);
		}
		bool dirty = _repaintOnChange.Value && (_repaint?.ConsumeDirty() ?? false);
		_requestedCpuFps = _cpuIdle.Resolve(now,
			_cpuPowerSaving.Value && _fpsMapping.Value && _repaintOnChange.Value && _repaint?.IsReady == true && hostActivitySnapshot.IsEditor && hostActivitySnapshot.CanThrottle &&
			!hostActivitySnapshot.HasInteraction && !hostActivitySnapshot.HasDynamicPreview && !_scrollProtected && !mutationProtected && !dirty,
			Application.isFocused, _cpuIdleDelay.Value, _cpuForegroundFps.Value, _cpuBackgroundFps.Value);
		if (progressiveResourcesReady && _host.Profile.PreviewOwnership && _previewScale.Value < 0.999f && hostActivitySnapshot.HasPreview)
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
		// A verified on-change plan already covers a static embedded preview.
		// The 60 FPS preview setting is only a fallback while observation is unavailable.
		bool flag = EditorCadencePolicy.UsePreviewFallback(_usingRepaint, allowPreviewOptimization && _adaptivePreview.Value, hostActivitySnapshot, _previewIdleFps.Value);
		CadencePlan cadencePlan = EditorCadencePolicy.Resolve(_rate.IsReady ? _rate.FramesPerSecond : double.NaN,
			_usingRepaint, visualCadencePlan, flag, _previewIdleFps.Value, _measuredCadence.Value,
			_idleFps.Value, _deepFps.Value, _idleInterval.Value, _deepInterval.Value, _ambientObserved, _ambientCharacterFps.Value);
		bool flag2 = EditorCadencePolicy.IsProtected(hostActivitySnapshot, _scrollProtected, mutationProtected, cadencePlan);
		RenderDecision renderDecision = _policy.Update(now, enabled: true, compatible: true, flag2, hostActivitySnapshot.HasInteraction, Application.isFocused, (!cadencePlan.Valid) ? 1 : cadencePlan.IdleInterval, (!cadencePlan.Valid) ? 1 : cadencePlan.DeepInterval, _usingRepaint ? value2 : _idleDelay.Value, _usingRepaint ? value2 : _deepDelay.Value);
		_mode = renderDecision.Mode;
		_characterMeshAllowed = progressiveResourcesReady && _ambientObserved && !flag2 && renderDecision.Interval > 1;
		_characterMeshes?.Update(_characterMeshAllowed);
		_reason = (_scrollProtected ? "scroll input, viewport motion or settle hold" : (mutationProtected ? "dialogue mutation or layout settle" : ((!cadencePlan.Valid) ? cadencePlan.Reason : (flag2 ? hostActivitySnapshot.Reason : ((flag && renderDecision.Interval > 1) ? "idle embedded editor preview" : ((_usingRepaint && renderDecision.Interval > 1) ? visualCadencePlan.Reason : renderDecision.Reason))))));
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
		if (_scrollProtected) { _characterMeshAllowed = false; _characterMeshes?.Suspend(); }
		if (_mappingAllowed && !_exportSuspended && _repaintOnChange.Value)
		{
			UiRepaintObserver? repaint = _repaint;
			if (repaint != null && repaint.ConsumeDirty())
			{
				_requestedCpuFps = 0;
				_cpuIdle.Reset();
				VisualCadencePlan visualCadencePlan = _visualCadence.Resolve(now, dirty: true, _safetyRepaintFps.Value, _idleAnimationFps.Value, _repaintSettleSeconds.Value);
				if (_usingRepaint && !_ambientObserved && (_mode == RenderMode.Idle || _mode == RenderMode.DeepIdle))
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
			int previousIdleFps = _fps.IdleFrameRate;
			_fps.SetIdleFrameRate(_mappingAllowed && !_exportSuspended && !_scrollProtected ? _requestedCpuFps : 0);
			if (previousIdleFps != _fps.IdleFrameRate)
			{
				_intervalLease?.Restore();
				_rate.Reset();
			}
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
		if (_host != null && _host.HostSupported && _host.Profile.FpsLabelPostfix && !_scrollProtected && now >= _nextLabelProbe)
		{
			_nextLabelProbe = now + 1.0;
			if (CurrentDisplayRate.TryRead(out DisplayRatePolicy display) && ShouldMapLabels &&
				(display.RefreshRate != _mappedDisplayRefreshRate || !_fpsLabelPostfixReady))
			{
				RefreshNativeLabels();
			}
		}
		bool cachePreview = _previewCacheStatic && _repaint?.IsReady == true;
		long previewGeneration = _repaint?.PreviewGeneration ?? 0;
		if (previewGeneration != _previewContentGeneration)
		{
			_previewContentGeneration = previewGeneration;
			_previewCadence?.Invalidate();
		}
		_previewCadence?.Update(now, _previewCadenceAllowed && (_progressiveLoading?.ResourceDependentWorkAllowed ?? true) && _mappingAllowed && !_exportSuspended &&
			((cachePreview) || (!_scrollProtected && _limitPreview.Value)), _previewFps.Value, cachePreview, _safetyRepaintFps.Value);
		RecordDiagnostics(now);
	}

	private void Wake()
	{
		_characterMeshAllowed = false;
		_characterMeshes?.Suspend();
		_previewCadence?.Invalidate();
		_cpuIdle.Reset();
		_requestedCpuFps = 0;
		_fps?.RestoreIdleFrameRate();
		_intervalLease?.Restore();
		_visualCadence.Reset();
		_policy.Reset(Now);
		_rate.Reset();
	}

	private void WakeForScroll()
	{
		_characterMeshAllowed = false;
		_characterMeshes?.Suspend();
		_cpuIdle.Reset();
		_requestedCpuFps = 0;
		bool wasCpuIdle = (_fps?.IdleFrameRate ?? 0) != 0;
		_fps?.RestoreIdleFrameRate();
		if (wasCpuIdle) _rate.Reset();
		_intervalLease?.Restore();
		_previewCadence?.Restore();
		_policy.Reset(Now);
		_mode = RenderMode.Active;
		_reason = "scroll input, viewport motion or settle hold";
	}

	private bool RestoreRendering()
	{
		_characterMeshAllowed = false;
		_characterMeshes?.Suspend();
		_previewAllowed = false;
		_previewCadenceAllowed = false;
		bool num = _previewCadence?.Restore() ?? true;
		bool flag = _previewTarget?.Restore() ?? true;
		bool flag2 = _intervalLease?.Restore() ?? true;
		bool flag3 = _scaleLease?.Restore() ?? true;
		bool bloomRestored = _editorBloom?.Restore() ?? true;
		_previewTextures.Clear();
		return num && flag && flag2 && flag3 && bloomRestored;
	}

	private void SuspendForExport()
	{
		_progressiveLoading?.SuspendForExport();
		_virtualization?.Suspend();
		_cpuIdle.Reset();
		_requestedCpuFps = 0;
		_panelWork?.Restore();
		_rowReuse?.Suspend();
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
		_progressiveLoading?.ResumeAfterExport();
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
			log.LogInfo("CPU idle override FPS=" + (_fps?.IdleFrameRate ?? 0) + "; zero means the selected active frame plan.");
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
			if (!CurrentDisplayRate.TryRead(out DisplayRatePolicy display)) return;
			SettingPanel.SettingWidgets widgets = __instance.widgets;
			if (widgets != null)
			{
				var mapped = new FpsLabelValues(widgets.fps30Label?.text,
					widgets.fps60Label?.text, widgets.fpsInfLabel?.text).Map(current.ShouldMapLabels, display);
				if (widgets.fps30Label != null && mapped.Thirty != widgets.fps30Label.text) widgets.fps30Label.text = mapped.Thirty;
				if (widgets.fps60Label != null && mapped.Sixty != widgets.fps60Label.text) widgets.fps60Label.text = mapped.Sixty;
				if (widgets.fpsInfLabel != null && mapped.Infinity != widgets.fpsInfLabel.text) widgets.fpsInfLabel.text = mapped.Infinity;
				if (current.ShouldMapLabels) current._mappedDisplayRefreshRate = display.RefreshRate;
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
			_rowReuse?.Dispose();
			_rowReuse = null;
			_virtualization?.Dispose();
			_virtualization = null;
			_deferredSelectors?.Dispose();
			_deferredSelectors = null;
			_progressiveLoading?.Dispose();
			_progressiveLoading = null;
			_characterMeshes?.Dispose();
			_characterMeshes = null;
			_mutation?.Dispose();
			_mutation = null;
			_previewCadence?.Dispose();
			_previewCadence = null;
			_previewTarget?.Dispose();
			_previewTarget = null;
			_editorBloom?.Dispose();
			_editorBloom = null;
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
			_driver.Quitting = null;
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
