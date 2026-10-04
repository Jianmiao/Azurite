using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Studio.Scripts;

namespace Azurite;

internal sealed class PlainTextLayoutFastPath : IDisposable
{
	private const string Owner = "halocue.azurite.plain-text-dispatch";

	private readonly Harmony _harmony = new Harmony("halocue.azurite.plain-text-dispatch");

	private readonly Action<string> _log;

	private static PlainTextLayoutFastPath? _active;

	[ThreadStatic]
	private static IntPtr _refreshText;

	private bool _installed;

	private bool _disposed;

	private long _shortcuts;

	private long _fallbacks;

	private double _nextSummary;

	public bool Enabled { get; set; } = true;

	public PlainTextLayoutFastPath(Action<string> log)
	{
		_log = log ?? throw new ArgumentNullException("log");
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
		if (_active != null)
		{
			return false;
		}
		try
		{
			MethodInfo methodInfo = AccessTools.Method(typeof(PhoneticText), "ParseSizeAndPhonetic", new Type[3]
			{
				typeof(string),
				typeof(int),
				typeof(bool)
			});
			if (methodInfo == null)
			{
				throw new MissingMethodException("PhoneticText.ParseSizeAndPhonetic");
			}
			_harmony.Patch(methodInfo, new HarmonyMethod(typeof(PlainTextLayoutFastPath), "BeforeParse"));
			MethodInfo original = AccessTools.Method(typeof(ScriptListItem), "Refresh", Type.EmptyTypes) ?? throw new MissingMethodException("ScriptListItem.Refresh");
			_harmony.Patch(original, new HarmonyMethod(typeof(PlainTextLayoutFastPath), "BeforeRefresh"), new HarmonyMethod(typeof(PlainTextLayoutFastPath), "AfterRefresh"), null, new HarmonyMethod(typeof(PlainTextLayoutFastPath), "FinishRefresh"), null);
			_active = this;
			_installed = true;
			_log("Plain dialogue parser shortcut enabled; native wrapping and text chunks retained.");
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
			_log("Plain dialogue parser shortcut unavailable: " + ex.GetType().Name);
			return false;
		}
	}

	private static bool BeforeParse(PhoneticText __instance, string __0, int __1, bool __2)
	{
		PlainTextLayoutFastPath active = _active;
		if (active == null || active._disposed || !active.Enabled)
		{
			return true;
		}
		if (!IsPlainDialogueText(__0, __1, __2) || !IsDialogueRow(__instance))
		{
			Interlocked.Increment(ref active._fallbacks);
			return true;
		}
		__instance.AddChunk(__0, null, __1);
		Interlocked.Increment(ref active._shortcuts);
		return false;
	}

	internal static bool IsPlainDialogueText(string? text, int sizeOverride, bool applyOverrideToAll)
	{
		bool flag = text == null;
		if (!flag)
		{
			int length = text.Length;
			bool flag2 = ((length > 4096 || length == 0) ? true : false);
			flag = flag2;
		}
		if (flag || sizeOverride != 42 || !applyOverrideToAll)
		{
			return false;
		}
		foreach (char c in text)
		{
			switch (c)
			{
			case '[':
			case '\\':
			case ']':
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag || char.IsControl(c) || char.IsSurrogate(c))
			{
				return false;
			}
		}
		return true;
	}

	private static bool IsDialogueRow(PhoneticText text)
	{
		try
		{
			if (text == null || text.regionText == null || text.textChunks == null || text.activeColorTags == null || text.activeColorTags.Count != 0 || text.textChunks.Count != 0 || text.currentPos != 0f)
			{
				return false;
			}
			return _refreshText != IntPtr.Zero && _refreshText == text.Pointer;
		}
		catch
		{
			return false;
		}
	}

	private static void BeforeRefresh(ScriptListItem __instance, out IntPtr __state)
	{
		__state = _refreshText;
		_refreshText = IntPtr.Zero;
		PlainTextLayoutFastPath active = _active;
		if (active == null || active._disposed || !active.Enabled)
		{
			return;
		}
		try
		{
			ScriptNodeInspector instance = ScriptNodeInspector.instance;
			if (!(instance == null) && instance.isActiveAndEnabled && !(__instance == null) && !(__instance.inspector == null) && !(__instance.inspector.Pointer != instance.Pointer))
			{
				PhoneticText scriptPhonetic = __instance.scriptPhonetic;
				if (scriptPhonetic != null)
				{
					_refreshText = scriptPhonetic.Pointer;
				}
			}
		}
		catch
		{
		}
	}

	private static void AfterRefresh(IntPtr __state)
	{
		_refreshText = __state;
	}

	private static void FinishRefresh(Exception? __exception, IntPtr __state)
	{
		_refreshText = __state;
	}

	public void Update(double now)
	{
		if (_installed && !_disposed && double.IsFinite(now) && !(now < _nextSummary))
		{
			_nextSummary = now + 5.0;
			long num = Interlocked.Exchange(ref _shortcuts, 0L);
			long num2 = Interlocked.Exchange(ref _fallbacks, 0L);
			if (num != 0L || num2 != 0L)
			{
				_log($"plain-text-dispatch t={now:F3}s shortcuts={num} nativeFallbacks={num2}; layout remains native.");
			}
		}
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
			if (_installed)
			{
				_harmony.UnpatchSelf();
			}
			_installed = false;
		}
	}
}
