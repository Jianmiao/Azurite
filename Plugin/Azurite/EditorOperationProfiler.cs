using System;
using System.Diagnostics;
using System.Reflection;
using AzureArchive.Automation;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Studio.Scripts;
using Studio.Scripts.Nodes;
using UnityEngine.SceneManagement;

namespace Azurite;

internal sealed class EditorOperationProfiler : IDisposable
{
	private sealed class Counter
	{
		public readonly string Name;

		public long Started;

		public long Completed;

		public long TotalTicks;

		public long MaximumTicks;

		public long PayloadBytes;

		public Counter(string name)
		{
			Name = name;
		}
	}

	private const string HarmonyId = "halocue.azurite.editor-operation-profile";

	private static EditorOperationProfiler? _active;

	[ThreadStatic]
	private static int _itemRefreshDepth;

	private readonly Action<string> _log;

	private readonly Harmony _harmony = new Harmony("halocue.azurite.editor-operation-profile");

	private readonly Process? _process;

	private readonly Counter[] _counters = new Counter[23]
	{
		new Counter("prepare-open"),
		new Counter("studio-load"),
		new Counter("inspector-load"),
		new Counter("sync-list"),
		new Counter("insert-current"),
		new Counter("insert-index"),
		new Counter("delete-current"),
		new Counter("list-item-init"),
		new Counter("list-item-refresh"),
		new Counter("list-item-set-index"),
		new Counter("item-phonetic-initialize"),
		new Counter("item-phonetic-parse"),
		new Counter("item-phonetic-display"),
		new Counter("item-phonetic-cleanup"),
		new Counter("project-file-read"),
		new Counter("project-byte-read"),
		new Counter("project-payload-parse"),
		new Counter("scene-load-request"),
		new Counter("studio-start"),
		new Counter("editor-session-begin"),
		new Counter("catalog-bind-project"),
		new Counter("catalog-capture-metadata"),
		new Counter("catalog-ensure-native")
	};

	private bool _installed;

	private bool _disposed;

	private double _nextSummary;

	private long _lastCpuTicks;

	private long _lastWallTicks;

	private bool _hasCpuSample;

	public EditorOperationProfiler(Action<string> log)
	{
		_log = log ?? throw new ArgumentNullException("log");
		try
		{
			_process = Process.GetCurrentProcess();
		}
		catch
		{
			_process = null;
		}
	}

	public bool Install(bool diagnosticDetailCounters = false)
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
			_itemRefreshDepth = 0;
			Patch(typeof(AuthoringEditorSession), "PrepareOpen", new Type[2]
			{
				typeof(string),
				typeof(bool)
			}, "BeginOpen", "EndOpen");
			TryPatchProjectRead("Read", "BeginProjectRead", "EndProjectRead");
			TryPatchProjectRead("ReadBytes", "BeginProjectBytes", "EndProjectBytes");
			TryPatchProjectRead("ReadBytesProject", "BeginProjectDeserialize", "EndProjectDeserialize");
			Patch(typeof(StudioCommon), "Load", new Type[1] { typeof(ProjectData) }, "BeginStudio", "EndStudio");
			Patch(typeof(SceneManager), "LoadSceneAsync", new[] { typeof(string) }, nameof(BeginSceneRequest), nameof(EndSceneRequest));
			Patch(typeof(StudioCommon), "Start", Type.EmptyTypes, nameof(BeginStudioStart), nameof(EndStudioStart));
			Patch(typeof(AuthoringEditorSession), "Begin", new[] { typeof(StudioCommon), typeof(ProjectData), typeof(string) }, nameof(BeginSession), nameof(EndSession));
			Patch(typeof(AuthoringResourceCatalog), "BindProject", new[] { typeof(ProjectData), typeof(string), typeof(string) }, nameof(BeginCatalogBind), nameof(EndCatalogBind));
			Patch(typeof(AuthoringResourceCatalog), "CaptureMetadata", new[] { typeof(bool) }, nameof(BeginCatalogCapture), nameof(EndCatalogCapture));
			Patch(typeof(AuthoringResourceCatalog), "EnsureNative", new[] { typeof(ScenarioResourceManager) }, nameof(BeginCatalogNative), nameof(EndCatalogNative));
			Patch(typeof(ScriptNodeInspector), "Load", new Type[2]
			{
				typeof(Node),
				typeof(bool)
			}, "BeginInspector", "EndInspector");
			Patch(typeof(ScriptNodeInspector), "SyncScriptList", new Type[3]
			{
				typeof(bool),
				typeof(bool),
				typeof(bool)
			}, "BeginSync", "EndSync");
			Patch(typeof(ScriptNodeInspector), "InsertScript", Type.EmptyTypes, "BeginInsert", "EndInsert");
			Patch(typeof(ScriptNodeInspector), "InsertScript", new Type[1] { typeof(int) }, "BeginInsertIndex", "EndInsertIndex");
			Patch(typeof(ScriptNodeInspector), "DeleteScript", Type.EmptyTypes, "BeginDelete", "EndDelete");
			if (diagnosticDetailCounters)
			{
				Patch(typeof(ScriptListItem), "Init", new Type[3]
				{
					typeof(ScriptNode),
					typeof(int),
					typeof(ScriptNodeInspector)
				}, "BeginItemInit", "EndItemInit");
				Patch(typeof(ScriptListItem), "Refresh", Type.EmptyTypes, "BeginItemRefresh", "EndItemRefresh", "FinalizeItemRefresh");
				Patch(typeof(ScriptListItem), "SetIndexAndSiblingIndex", new Type[1] { typeof(int) }, "BeginItemIndex", "EndItemIndex");
				Patch(typeof(PhoneticText), "Initialize", new Type[3]
				{
					typeof(string),
					typeof(int),
					typeof(bool)
				}, "BeginPhoneticInitialize", "EndPhoneticInitialize");
				Patch(typeof(PhoneticText), "ParseSizeAndPhonetic", new Type[3]
				{
					typeof(string),
					typeof(int),
					typeof(bool)
				}, "BeginPhoneticParse", "EndPhoneticParse");
				Patch(typeof(PhoneticText), "DisplayChunks", Type.EmptyTypes, "BeginPhoneticDisplay", "EndPhoneticDisplay");
				Patch(typeof(PhoneticText), "CleanUp", Type.EmptyTypes, "BeginPhoneticCleanup", "EndPhoneticCleanup");
			}
			_installed = true;
			_log("editor-operation profiling enabled for authoring entry points; detailCounters=" + diagnosticDetailCounters + "; project read/byte read/deserialize hooks are individually optional; phonetic counters scoped to ScriptListItem.Refresh; inclusive wall time only; no per-call logging or UIGrid hooks.");
			return true;
		}
		catch (Exception ex)
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
			_log("editor-operation profiling unavailable: " + ex.GetType().Name + "; " + ex.Message);
			return false;
		}
	}

	private void Patch(Type type, string name, Type[] arguments, string prefix, string postfix, string? finalizer = null)
	{
		MethodInfo methodInfo = AccessTools.Method(type, name, arguments) ?? throw new MissingMethodException(type.FullName, name);
		_harmony.Patch(methodInfo, new HarmonyMethod(typeof(EditorOperationProfiler), prefix), new HarmonyMethod(typeof(EditorOperationProfiler), postfix), null, (finalizer == null) ? null : new HarmonyMethod(typeof(EditorOperationProfiler), finalizer), null);
		Patches patchInfo = Harmony.GetPatchInfo(methodInfo);
		if (patchInfo == null || !patchInfo.Owners.Contains("halocue.azurite.editor-operation-profile"))
		{
			throw new InvalidOperationException("Authoring timing hook was not registered: " + name);
		}
	}

	private void TryPatchProjectRead(string name, string prefix, string postfix)
	{
		try
		{
			MethodInfo methodInfo = AccessTools.Method(typeof(ProjectFileStore), name) ?? throw new MissingMethodException(typeof(ProjectFileStore).FullName, name);
			_harmony.Patch(methodInfo, new HarmonyMethod(typeof(EditorOperationProfiler), prefix), new HarmonyMethod(typeof(EditorOperationProfiler), postfix));
			Patches patchInfo = Harmony.GetPatchInfo(methodInfo);
			if (patchInfo == null || !patchInfo.Owners.Contains(HarmonyId))
			{
				throw new InvalidOperationException("Project load timing hook was not registered: " + name);
			}
		}
		catch (Exception ex)
		{
			_log("project load timing hook " + name + " unavailable: " + ex.GetType().Name + "; other authoring diagnostics remain available.");
		}
	}

	public void Update(double now)
	{
		if (!_installed || _disposed || !double.IsFinite(now) || now < _nextSummary)
		{
			return;
		}
		_nextSummary = now + 5.0;
		long wallTicks = Stopwatch.GetTimestamp();
		long cpuTicks = 0L;
		try
		{
			if (_process != null)
			{
				cpuTicks = _process.TotalProcessorTime.Ticks;
			}
		}
		catch
		{
			_hasCpuSample = false;
		}
		if (_hasCpuSample && cpuTicks >= _lastCpuTicks && wallTicks >= _lastWallTicks)
		{
			double wallMilliseconds = (double)(wallTicks - _lastWallTicks) * 1000.0 / Stopwatch.Frequency;
			double cpuMilliseconds = (double)(cpuTicks - _lastCpuTicks) / TimeSpan.TicksPerMillisecond;
			double share = (wallMilliseconds <= 0.0) ? 0.0 : cpuMilliseconds / wallMilliseconds * 100.0;
			_log($"editor-operation-cpu t={now:F3}s processCpu={cpuMilliseconds:F3}ms processWall={wallMilliseconds:F3}ms cpuShare={share:F1}%; process-wide AA/mod time, not operation-specific.");
		}
		_lastCpuTicks = cpuTicks;
		_lastWallTicks = wallTicks;
		_hasCpuSample = cpuTicks != 0L;
		for (int i = 0; i < _counters.Length; i++)
		{
			Counter counter = _counters[i];
			long started;
			long completed;
			long totalTicks;
			long maximumTicks;
			long payloadBytes;
			lock (counter)
			{
				started = counter.Started;
				completed = counter.Completed;
				totalTicks = counter.TotalTicks;
				maximumTicks = counter.MaximumTicks;
				payloadBytes = counter.PayloadBytes;
				counter.Started = (counter.Completed = (counter.TotalTicks = (counter.MaximumTicks = (counter.PayloadBytes = 0L))));
			}
			if (started != 0L || completed != 0L)
			{
				double num = 1000.0 / (double)Stopwatch.Frequency;
				string projectNote = (i >= 14 && i <= 16) ? " ReadBytes time can include cache, copy and allocation; it does not alone prove physical disk latency." : string.Empty;
				_log($"editor-operation t={now:F3}s name={counter.Name} started={started} completed={completed} total={(double)totalTicks * num:F3}ms max={(double)maximumTicks * num:F3}ms mean={((completed == 0L) ? 0.0 : ((double)totalTicks * num / (double)completed)):F3}ms" + ((i == 15) ? $" payloadBytes={payloadBytes}" : string.Empty) + "; inclusive wall time, nested operations overlap. Uncompleted calls may span windows or throw." + projectNote);
			}
		}
	}

	private static void Begin(int index, out long state)
	{
		state = 0L;
		EditorOperationProfiler active = _active;
		if (active != null && !active._disposed)
		{
			Counter counter = active._counters[index];
			lock (counter)
			{
				counter.Started++;
			}
			state = Stopwatch.GetTimestamp();
		}
	}

	private static void End(int index, long state)
	{
		if (state == 0L)
		{
			return;
		}
		long num = Stopwatch.GetTimestamp() - state;
		EditorOperationProfiler active = _active;
		if (active == null || active._disposed || num < 0)
		{
			return;
		}
		Counter counter = active._counters[index];
		lock (counter)
		{
			counter.Completed++;
			counter.TotalTicks += num;
			if (num > counter.MaximumTicks)
			{
				counter.MaximumTicks = num;
			}
		}
	}

	private static void BeginOpen(out long __state)
	{
		Begin(0, out __state);
	}

	private static void EndOpen(long __state)
	{
		End(0, __state);
	}

	private static void BeginProjectRead(out long __state)
	{
		Begin(14, out __state);
	}

	private static void EndProjectRead(long __state)
	{
		End(14, __state);
	}

	private static void BeginProjectBytes(out long __state)
	{
		Begin(15, out __state);
	}

	private static void EndProjectBytes(long __state, Il2CppStructArray<byte>? __result)
	{
		End(15, __state);
		EditorOperationProfiler active = _active;
		if (__state == 0L || active == null || active._disposed || __result == null)
		{
			return;
		}
		Counter counter = active._counters[15];
		lock (counter)
		{
			counter.PayloadBytes += __result.Length;
		}
	}

	private static void BeginProjectDeserialize(out long __state)
	{
		Begin(16, out __state);
	}

	private static void EndProjectDeserialize(long __state)
	{
		End(16, __state);
	}

	private static void BeginStudio(out long __state)
	{
		Begin(1, out __state);
	}
	private static void BeginSceneRequest(out long __state) => Begin(17, out __state);
	private static void EndSceneRequest(long __state) => End(17, __state);
	private static void BeginStudioStart(out long __state) => Begin(18, out __state);
	private static void EndStudioStart(long __state) => End(18, __state);
	private static void BeginSession(out long __state) => Begin(19, out __state);
	private static void EndSession(long __state)
	{
		End(19, __state);
		// Read AA's already-computed metrics. Never invoke CatalogVersion or a
		// capture here: the probe must not build the catalog it is measuring.
		try
		{
			var metrics = AuthoringResourceCatalog.lastCaptureMetrics;
			// The parameterless native virtual dispatch emitted only the JObject
			// type name. The explicit formatter invokes JSON serialization itself.
			if (metrics != null) _active?._log("editor-entry native-catalog-metrics=" +
				metrics.ToString(Newtonsoft.Json.Formatting.None, Array.Empty<Newtonsoft.Json.JsonConverter>()));
		}
		catch (Exception e) { _active?._log("editor-entry catalog metrics unavailable: " + e.GetType().Name); }
	}
	private static void BeginCatalogBind(out long __state) => Begin(20, out __state);
	private static void EndCatalogBind(long __state) => End(20, __state);
	private static void BeginCatalogCapture(out long __state) => Begin(21, out __state);
	private static void EndCatalogCapture(long __state) => End(21, __state);
	private static void BeginCatalogNative(out long __state) => Begin(22, out __state);
	private static void EndCatalogNative(long __state) => End(22, __state);

	private static void EndStudio(long __state)
	{
		End(1, __state);
	}

	private static void BeginInspector(out long __state)
	{
		Begin(2, out __state);
	}

	private static void EndInspector(long __state)
	{
		End(2, __state);
	}

	private static void BeginSync(out long __state)
	{
		Begin(3, out __state);
	}

	private static void EndSync(long __state)
	{
		End(3, __state);
	}

	private static void BeginInsert(out long __state)
	{
		Begin(4, out __state);
	}

	private static void EndInsert(long __state)
	{
		End(4, __state);
	}

	private static void BeginInsertIndex(out long __state)
	{
		Begin(5, out __state);
	}

	private static void EndInsertIndex(long __state)
	{
		End(5, __state);
	}

	private static void BeginDelete(out long __state)
	{
		Begin(6, out __state);
	}

	private static void EndDelete(long __state)
	{
		End(6, __state);
	}

	private static void BeginItemInit(out long __state)
	{
		Begin(7, out __state);
	}

	private static void EndItemInit(long __state)
	{
		End(7, __state);
	}

	private static void BeginItemRefresh(out long __state)
	{
		Begin(8, out __state);
		if (__state != 0L)
		{
			_itemRefreshDepth++;
		}
	}

	private static void EndItemRefresh(long __state)
	{
		End(8, __state);
	}

	private static void FinalizeItemRefresh(long __state)
	{
		if (__state != 0L && _itemRefreshDepth > 0)
		{
			_itemRefreshDepth--;
		}
	}

	private static void BeginItemIndex(out long __state)
	{
		Begin(9, out __state);
	}

	private static void EndItemIndex(long __state)
	{
		End(9, __state);
	}

	private static void BeginInItemRefresh(int index, out long state)
	{
		state = 0L;
		if (_itemRefreshDepth > 0)
		{
			Begin(index, out state);
		}
	}

	private static void BeginPhoneticInitialize(out long __state)
	{
		BeginInItemRefresh(10, out __state);
	}

	private static void EndPhoneticInitialize(long __state)
	{
		End(10, __state);
	}

	private static void BeginPhoneticParse(out long __state)
	{
		BeginInItemRefresh(11, out __state);
	}

	private static void EndPhoneticParse(long __state)
	{
		End(11, __state);
	}

	private static void BeginPhoneticDisplay(out long __state)
	{
		BeginInItemRefresh(12, out __state);
	}

	private static void EndPhoneticDisplay(long __state)
	{
		End(12, __state);
	}

	private static void BeginPhoneticCleanup(out long __state)
	{
		BeginInItemRefresh(13, out __state);
	}

	private static void EndPhoneticCleanup(long __state)
	{
		End(13, __state);
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			if (_active == this)
			{
				_active = null;
			}
			_itemRefreshDepth = 0;
			if (_installed)
			{
				_harmony.UnpatchSelf();
			}
			_installed = false;
		}
	}
}
