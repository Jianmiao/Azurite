using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace Azurite;

internal sealed class HostCompatibility : IDisposable
{
	private readonly record struct ExportBuild(Guid Mvid, bool HasPanelBudget, bool CaptureOptimizationVerified);

	private static readonly IReadOnlyDictionary<string, ExportBuild> VerifiedExportBuilds = new Dictionary<string, ExportBuild>(StringComparer.Ordinal)
	{
		["EFC7AF02853020EB2AE1AFF6B4557B490D498226104B3A9B3C587E22C7868E40"] = new ExportBuild(new Guid("e94e93dd-5d53-4a22-ad63-3b676302b6f2"), HasPanelBudget: true, CaptureOptimizationVerified: true),
		["7BEF58C64B949C3E8074EF265132E4A71078D1280269FAEB1587E8D1B1E5FEF5"] = new ExportBuild(new Guid("2f2606bc-4aac-48b0-9dea-bf615513a585"), HasPanelBudget: true, CaptureOptimizationVerified: false),
		["912B9556E822F3E1EA5FD8124E968D5B191D06DFD3A0D00FA85C8FA0FA568C58"] = new ExportBuild(new Guid("136e0846-cc7e-449e-8256-f9c7ffadbed1"), HasPanelBudget: true, CaptureOptimizationVerified: false),
		["4E7D884615A2595D1005A333080A200E5749BB81E51A10CC82D54431A90823B4"] = new ExportBuild(new Guid("0ace405c-e153-4bed-a534-988fa397e4bb"), HasPanelBudget: true, CaptureOptimizationVerified: false),
		["AF7474C20F262A38428105B223AF4E69239B68EFD08B87BDF6DB1DD018BB84AE"] = new ExportBuild(new Guid("622921bd-0b77-4290-bc8a-ace7b960aa28"), HasPanelBudget: true, CaptureOptimizationVerified: false)
	};

	private static HostCompatibility? _activeAdapter;

	private readonly ManualLogSource _log;

	private readonly Action _restoreControl;

	private readonly Action? _renderReleased;

	private RenderControlClient? _publicRenderControl;

	private Harmony? _harmony;

	private IL2CPPChainloader? _chainloader;

	private bool _chainloaderFinished;

	private bool _hostChecked;

	private bool _hostSupported;

	private string _hostReason = "host identity not checked";

	private string _gameRoot = string.Empty;

	private bool _bindingAttempted;

	private bool _bindingHealthy;

	private bool _noExportConfirmed;

	private string _bindingReason = "waiting for AAVideoExport chainloader state";

	private Type? _exportHostType;

	private Func<object?>? _currentHost;

	private Func<object, bool>? _capturing;

	private Func<object, bool>? _busy;

	private Func<bool>? _clockIsActive;

	private Func<object, object?>? _panelForHost;

	private Func<object, bool>? _panelVisible;

	private Func<object, bool>? _panelBudget;

	private MethodInfo? _prepare;

	private MethodInfo? _begin;

	private MethodInfo? _applyPanelBudget;

	private long _lastAssemblyCheckTicks;

	private long _lastWarningTicks;

	private long _entryHoldUntil;

	private bool _disposed;

	private bool _captureOptimizationEnabled;

	internal bool HostSupported => _hostSupported;

	internal HostProfile Profile { get; private set; } = HostProfile.Unsupported;

	public HostCompatibility(ManualLogSource log, Action restoreControl, Action? renderReleased = null)
	{
		_log = log;
		_restoreControl = restoreControl;
		_renderReleased = renderReleased;
	}

	public bool Initialize(Harmony harmony)
	{
		_harmony = harmony;
		_activeAdapter = this;
		_chainloader = IL2CPPChainloader.Instance;
		if (_chainloader != null)
		{
			_chainloader.Finished += OnChainloaderFinished;
		}
		EnsureHostIdentity();
		EnsureVideoExportBinding();
		return _hostSupported;
	}

	public void SetCaptureOptimization(bool enabled)
	{
		_captureOptimizationEnabled = enabled;
	}

	public HostCompatibilitySnapshot Probe()
	{
		if (_disposed)
		{
			return HostCompatibilitySnapshot.Unsupported("adapter disposed");
		}
		EnsureHostIdentity();
		if (!_hostSupported)
		{
			return HostCompatibilitySnapshot.Unsupported(_hostReason);
		}
		EnsureVideoExportBinding();
		if (_noExportConfirmed)
		{
			return HostCompatibilitySnapshot.NoExport("chainloader complete; no AAVideoExport loaded");
		}
		if (!_bindingAttempted)
		{
			return HostCompatibilitySnapshot.Pending("waiting for chainloader completion");
		}
		if (!_bindingHealthy)
		{
			return HostCompatibilitySnapshot.Unsupported(_bindingReason);
		}
		try
		{
			if (_publicRenderControl != null)
			{
				RenderControlState renderControlState = _publicRenderControl.Probe();
				if (!renderControlState.Healthy)
				{
					FailBinding(renderControlState.Reason);
					return HostCompatibilitySnapshot.Unsupported(_bindingReason);
				}
				if (!renderControlState.Ready)
				{
					return HostCompatibilitySnapshot.Pending(renderControlState.Reason);
				}
				return new HostCompatibilitySnapshot(Supported: true, ProbeHealthy: true, VideoExportAvailable: true, renderControlState.Owned, renderControlState.Reason);
			}
			object obj = _currentHost();
			if (obj == null || !_exportHostType.IsInstanceOfType(obj) || !(obj is UnityEngine.Object obj2) || obj2 == null)
			{
				return HostCompatibilitySnapshot.Pending("AAVideoExport managed host is unavailable");
			}
			bool flag = false;
			if (_panelForHost != null)
			{
				object obj3 = _panelForHost(obj);
				if (obj3 == null)
				{
					return HostCompatibilitySnapshot.Pending("AAVideoExport settings panel is unavailable");
				}
				flag = _panelVisible(obj3) || _panelBudget(obj3);
			}
			bool flag2 = flag || _clockIsActive() || _capturing(obj) || _busy(obj) || Stopwatch.GetTimestamp() < _entryHoldUntil;
			return new HostCompatibilitySnapshot(Supported: true, ProbeHealthy: true, VideoExportAvailable: true, flag2, flag ? "AAVideoExport settings panel owns rendering" : (flag2 ? "AAVideoExport export is active" : "host and export contract verified"));
		}
		catch (Exception error)
		{
			FailBinding("AAVideoExport state probe failed", error);
			return HostCompatibilitySnapshot.Unsupported(_bindingReason);
		}
	}

	private void OnChainloaderFinished()
	{
		_chainloaderFinished = true;
		_lastAssemblyCheckTicks = 0L;
	}

	private void EnsureHostIdentity()
	{
		if (_hostChecked)
		{
			return;
		}
		_hostChecked = true;
		try
		{
			using Process process = Process.GetCurrentProcess();
			string path = process.MainModule?.FileName;
			if (!string.Equals(Path.GetFileName(path), "AzureArchive.exe", StringComparison.OrdinalIgnoreCase))
			{
				_hostReason = "process is not AzureArchive.exe";
				return;
			}
			_gameRoot = ((!string.IsNullOrWhiteSpace(Paths.GameRootPath)) ? Paths.GameRootPath : (Path.GetDirectoryName(path) ?? string.Empty));
			Profile = _gameRoot.Length == 0 ? HostProfile.Unsupported : HostProfile.Resolve(
				ReadIdentityHash(Path.Combine(_gameRoot, "GameAssembly.dll")),
				ReadIdentityHash(Path.Combine(_gameRoot, "BepInEx", "interop", "Assembly-CSharp.dll")),
				ReadIdentityHash(Path.Combine(_gameRoot, "BepInEx", "interop", "UnityEngine.CoreModule.dll")));
			if (!Profile.Supported)
			{
				_hostReason = "unsupported AA host identity";
				return;
			}
			_hostSupported = true;
			_hostReason = "AA host identity verified: " + Profile.Name;
			_log.LogInfo(_hostReason + "; native patches=" + Profile.NativePatches + "; preview optimization=" + Profile.PreviewOptimization);
		}
		catch (Exception ex)
		{
			_hostReason = "host identity probe failed: " + ex.GetType().Name;
		}
	}

	private static string ReadIdentityHash(string path)
	{
		using var stream = File.OpenRead(path);
		using var hash = SHA256.Create();
		return Convert.ToHexString(hash.ComputeHash(stream));
	}

	private void EnsureVideoExportBinding()
	{
		if (_bindingAttempted || _noExportConfirmed || !_hostSupported)
		{
			return;
		}
		long timestamp = Stopwatch.GetTimestamp();
		if (_lastAssemblyCheckTicks != 0L && timestamp - _lastAssemblyCheckTicks < Stopwatch.Frequency)
		{
			return;
		}
		_lastAssemblyCheckTicks = timestamp;
		Assembly assembly = null;
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly2 in assemblies)
		{
			if (string.Equals(assembly2.GetName().Name, "AAVideoExport", StringComparison.Ordinal))
			{
				if (assembly != null)
				{
					_bindingAttempted = true;
					FailBinding("multiple AAVideoExport assemblies are loaded");
					return;
				}
				assembly = assembly2;
			}
		}
		if (assembly == null)
		{
			if (_chainloaderFinished)
			{
				_noExportConfirmed = true;
			}
			return;
		}
		_bindingAttempted = true;
		try
		{
			RenderControlClient client;
			string reason;
			switch (RenderControlClient.TryBind(assembly, _restoreControl, _renderReleased, delegate(string message)
			{
				_log.LogWarning(message);
			}, out client, out reason))
			{
			case RenderControlBinding.Invalid:
				FailBinding(reason);
				break;
			default:
				_publicRenderControl = client;
				_bindingHealthy = true;
				_bindingReason = reason;
				_log.LogInfo("Azurite " + reason + "; private exporter hooks and binary pinning are not used.");
				break;
			case RenderControlBinding.Absent:
			{
				if (!Profile.LegacyExporter)
				{
					FailBinding("this AA host requires AAVideoExport RenderControlV1; legacy private hooks are disabled");
					break;
				}
				if (!TryGetExportBuild(assembly, out var build))
				{
					FailBinding("unsupported AAVideoExport build");
					break;
				}
				Type type = assembly.GetType("AAVideoExport.Plugin.ExportHost", throwOnError: true);
				Type? type2 = assembly.GetType("AAVideoExport.Plugin.NativeExportClock", throwOnError: true);
				FieldInfo field = type.GetField("Current", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				MethodInfo methodInfo = type.GetProperty("Capturing", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(nonPublic: true);
				MethodInfo methodInfo2 = type.GetProperty("Busy", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(nonPublic: true);
				MethodInfo methodInfo3 = type2.GetProperty("IsActive", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(nonPublic: true);
				MethodInfo method = type.GetMethod("Prepare", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				MethodInfo method2 = type.GetMethod("Begin", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(Test) }, null);
				if (field == null || !field.IsStatic || field.FieldType != type || !IsBoolGetter(methodInfo, isStatic: false) || !IsBoolGetter(methodInfo2, isStatic: false) || !IsBoolGetter(methodInfo3, isStatic: true) || method == null || method.IsStatic || method.ReturnType != typeof(void) || method.GetParameters().Length != 1 || method.GetParameters()[0].ParameterType.FullName != "AAVideoExport.Core.ExportOptions" || method2 == null || method2.IsStatic || method2.ReturnType != typeof(void))
				{
					throw new MissingMemberException("export contract differs from verified metadata");
				}
				_exportHostType = type;
				_currentHost = Expression.Lambda<Func<object>>(Expression.Convert(Expression.Field(null, field), typeof(object)), Array.Empty<ParameterExpression>()).Compile();
				_capturing = CompileGetter(type, methodInfo);
				_busy = CompileGetter(type, methodInfo2);
				_clockIsActive = (Func<bool>)methodInfo3.CreateDelegate(typeof(Func<bool>));
				_prepare = method;
				_begin = method2;
				if (build.HasPanelBudget)
				{
					BindPanelContract(assembly, type);
				}
				if (_harmony == null)
				{
					throw new InvalidOperationException("adapter not initialized");
				}
				HarmonyMethod prefix = new HarmonyMethod(typeof(HostCompatibility), "ExportEntryPrefix")
				{
					priority = 800
				};
				_harmony.Patch(_prepare, prefix);
				_harmony.Patch(_begin, prefix);
				if (_applyPanelBudget != null)
				{
					HarmonyMethod prefix2 = new HarmonyMethod(typeof(HostCompatibility), "PanelBudgetPrefix")
					{
						priority = 800
					};
					_harmony.Patch(_applyPanelBudget, prefix2);
				}
				if (_captureOptimizationEnabled && build.CaptureOptimizationVerified && !ExportCaptureOptimization.Install(_harmony, type, enabled: true))
				{
					_log.LogWarning("Azurite export duplicate-capture optimization could not be installed; native capture path is unchanged.");
				}
				if (!HasOwnPrefix(_prepare) || !HasOwnPrefix(_begin) || (_applyPanelBudget != null && !HasOwnPrefix(_applyPanelBudget)))
				{
					throw new InvalidOperationException("export entry patch was not registered");
				}
				_bindingHealthy = true;
				_bindingReason = "host and export contract verified";
				_log.LogInfo("Azurite verified export compatibility for the current pinned AAVideoExport build" + (build.HasPanelBudget ? "; settings-panel render ownership included." : "."));
				break;
			}
			}
		}
		catch (Exception error)
		{
			RemoveEntryPatches();
			FailBinding("AAVideoExport adapter binding failed", error);
		}
	}

	private void BindPanelContract(Assembly assembly, Type hostType)
	{
		Type type = assembly.GetType("AAVideoExport.Plugin.NativeExportPanel", throwOnError: true);
		FieldInfo field = hostType.GetField("_panel", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		MethodInfo methodInfo = type.GetProperty("Visible", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(nonPublic: true);
		FieldInfo field2 = type.GetField("_idleBudget", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		MethodInfo method = type.GetMethod("ApplyIdleBudget", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
		MethodInfo method2 = type.GetMethod("ReleaseIdleBudget", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
		if (field == null || field.IsStatic || field.FieldType != type || !IsBoolGetter(methodInfo, isStatic: false) || field2 == null || field2.IsStatic || field2.FieldType != typeof(bool) || method == null || method.IsStatic || method.ReturnType != typeof(void) || method2 == null || method2.IsStatic || method2.ReturnType != typeof(void))
		{
			throw new MissingMemberException("export settings-panel ownership contract differs from verified metadata");
		}
		ParameterExpression parameterExpression = Expression.Parameter(typeof(object), "host");
		_panelForHost = Expression.Lambda<Func<object, object>>(Expression.Convert(Expression.Field(Expression.Convert(parameterExpression, hostType), field), typeof(object)), new ParameterExpression[1] { parameterExpression }).Compile();
		_panelVisible = CompileGetter(type, methodInfo);
		ParameterExpression parameterExpression2 = Expression.Parameter(typeof(object), "panel");
		_panelBudget = Expression.Lambda<Func<object, bool>>(Expression.Field(Expression.Convert(parameterExpression2, type), field2), new ParameterExpression[1] { parameterExpression2 }).Compile();
		_applyPanelBudget = method;
	}

	private bool HasOwnPrefix(MethodInfo method)
	{
		Patches patchInfo = Harmony.GetPatchInfo(method);
		if (patchInfo == null || _harmony == null)
		{
			return false;
		}
		foreach (Patch prefix in patchInfo.Prefixes)
		{
			if (prefix.owner == _harmony.Id)
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsBoolGetter(MethodInfo? method, bool isStatic)
	{
		if (method != null && method.ReturnType == typeof(bool) && method.IsStatic == isStatic)
		{
			return method.GetParameters().Length == 0;
		}
		return false;
	}

	private static Func<object, bool> CompileGetter(Type hostType, MethodInfo getter)
	{
		Type type = typeof(Func<, >).MakeGenericType(hostType, typeof(bool));
		Delegate value = getter.CreateDelegate(type);
		ParameterExpression parameterExpression = Expression.Parameter(typeof(object), "instance");
		return Expression.Lambda<Func<object, bool>>(Expression.Invoke(Expression.Constant(value, type), Expression.Convert(parameterExpression, hostType)), new ParameterExpression[1] { parameterExpression }).Compile();
	}

	private static void ExportEntryPrefix()
	{
		HostCompatibility activeAdapter = _activeAdapter;
		if (activeAdapter != null && !activeAdapter._disposed)
		{
			activeAdapter._entryHoldUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
			try
			{
				activeAdapter._restoreControl();
			}
			catch (Exception error)
			{
				activeAdapter.FailBinding("could not restore scheduling before export", error);
			}
			ExportCaptureOptimization.Reset();
		}
	}

	private static void PanelBudgetPrefix(object __instance)
	{
		HostCompatibility activeAdapter = _activeAdapter;
		if (activeAdapter == null || activeAdapter._disposed || !activeAdapter._bindingHealthy)
		{
			return;
		}
		try
		{
			if (activeAdapter._panelBudget != null && !activeAdapter._panelBudget(__instance))
			{
				activeAdapter._entryHoldUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
				activeAdapter._restoreControl();
			}
		}
		catch (Exception error)
		{
			activeAdapter.FailBinding("could not restore scheduling before export panel budget", error);
		}
	}

	private void FailBinding(string reason, Exception? error = null)
	{
		_bindingHealthy = false;
		_bindingReason = reason;
		long timestamp = Stopwatch.GetTimestamp();
		if (_lastWarningTicks == 0L || timestamp - _lastWarningTicks >= Stopwatch.Frequency * 10)
		{
			_lastWarningTicks = timestamp;
			_log.LogWarning((error == null) ? ("Azurite disabled throttling: " + reason + ".") : ("Azurite disabled throttling: " + reason + " (" + error.GetType().Name + ")."));
		}
	}

	private static bool Matches(string path, string expected)
	{
		if (!File.Exists(path))
		{
			return false;
		}
		using FileStream inputStream = File.OpenRead(path);
		using SHA256 sHA = SHA256.Create();
		return string.Equals(Convert.ToHexString(sHA.ComputeHash(inputStream)), expected, StringComparison.Ordinal);
	}

	private bool TryGetExportBuild(Assembly assembly, out ExportBuild build)
	{
		build = default(ExportBuild);
		if (string.IsNullOrEmpty(assembly.Location) || !File.Exists(assembly.Location))
		{
			return false;
		}
		using FileStream inputStream = File.OpenRead(assembly.Location);
		using SHA256 sHA = SHA256.Create();
		string key = Convert.ToHexString(sHA.ComputeHash(inputStream));
		if (VerifiedExportBuilds.TryGetValue(key, out build) && assembly.ManifestModule.ModuleVersionId == build.Mvid)
		{
			return true;
		}
		Guid moduleVersionId = assembly.ManifestModule.ModuleVersionId;
		if (!ExportContractFingerprint.TryVerify(assembly.Location, moduleVersionId, out string digest))
		{
			return false;
		}
		build = new ExportBuild(moduleVersionId, HasPanelBudget: true, CaptureOptimizationVerified: false);
		_log.LogInfo("Azurite verified unchanged export ownership IL contract: " + digest + ".");
		return true;
	}

	private void RemoveEntryPatches()
	{
		if (_harmony == null)
		{
			return;
		}
		MethodInfo[] array = new MethodInfo[3] { _prepare, _begin, _applyPanelBudget };
		foreach (MethodInfo methodInfo in array)
		{
			if (!(methodInfo == null))
			{
				try
				{
					_harmony.Unpatch(methodInfo, HarmonyPatchType.Prefix, _harmony.Id);
				}
				catch
				{
				}
			}
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			if (_chainloader != null)
			{
				_chainloader.Finished -= OnChainloaderFinished;
			}
			_publicRenderControl?.Dispose();
			_publicRenderControl = null;
			RemoveEntryPatches();
			ExportCaptureOptimization.Uninstall();
			if (_activeAdapter == this)
			{
				_activeAdapter = null;
			}
		}
	}
}
