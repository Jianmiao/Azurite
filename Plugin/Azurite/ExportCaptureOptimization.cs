using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Azurite;

internal static class ExportCaptureOptimization
{
	private static bool _enabled;

	private static int _lastCapturedFrame = -1;

	private static MethodBase? _captureLoop;

	private static MethodBase? _captureFrame;

	private static Harmony? _harmony;

	public static bool Install(Harmony harmony, Type hostType, bool enabled)
	{
		_enabled = enabled;
		if (!enabled)
		{
			return true;
		}
		try
		{
			_captureLoop = AccessTools.Method(hostType, "CaptureLoopFrame");
			_captureFrame = AccessTools.Method(hostType, "CaptureFrame", new Type[1] { typeof(bool) });
			if (_captureLoop == null || _captureFrame == null)
			{
				return false;
			}
			_harmony = harmony;
			harmony.Patch(_captureLoop, new HarmonyMethod(typeof(ExportCaptureOptimization), "CaptureLoopPrefix"));
			harmony.Patch(_captureFrame, null, new HarmonyMethod(typeof(ExportCaptureOptimization), "CaptureFramePostfix"));
			Patches patchInfo = Harmony.GetPatchInfo(_captureLoop);
			return patchInfo != null && patchInfo.Prefixes.Any((Patch x) => x.owner == harmony.Id) && (Harmony.GetPatchInfo(_captureFrame)?.Postfixes.Any((Patch x) => x.owner == harmony.Id) ?? false);
		}
		catch
		{
			Uninstall();
			return false;
		}
	}

	public static void Reset()
	{
		_lastCapturedFrame = -1;
	}

	private static bool CaptureLoopPrefix()
	{
		if (!_enabled)
		{
			return true;
		}
		try
		{
			return _lastCapturedFrame != Time.frameCount;
		}
		catch
		{
			return true;
		}
	}

	private static void CaptureFramePostfix()
	{
		if (!_enabled)
		{
			return;
		}
		try
		{
			_lastCapturedFrame = Time.frameCount;
		}
		catch
		{
		}
	}

	public static void Uninstall()
	{
		if (_harmony != null)
		{
			try
			{
				if (_captureLoop != null)
				{
					_harmony.Unpatch(_captureLoop, HarmonyPatchType.Prefix, _harmony.Id);
				}
			}
			catch
			{
			}
			try
			{
				if (_captureFrame != null)
				{
					_harmony.Unpatch(_captureFrame, HarmonyPatchType.Postfix, _harmony.Id);
				}
			}
			catch
			{
			}
		}
		_harmony = null;
		_captureLoop = null;
		_captureFrame = null;
		_lastCapturedFrame = -1;
		_enabled = false;
	}
}
