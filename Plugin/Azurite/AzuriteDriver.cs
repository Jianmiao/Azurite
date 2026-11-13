using System;
using System.Diagnostics;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace Azurite;

internal sealed class AzuriteDriver : MonoBehaviour
{
	internal Action<double>? Tick;

	internal Action<double>? LateTick;

	internal Action? GuiTick;

	internal Action? Destroyed;

	internal Action? Quitting;

	public AzuriteDriver(IntPtr pointer)
		: base(pointer)
	{
	}

	public AzuriteDriver()
		: base(ClassInjector.DerivedConstructorPointer<AzuriteDriver>())
	{
		ClassInjector.DerivedConstructorBody(this);
	}

	private void Update()
	{
		try
		{
			Tick?.Invoke((double)Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
		}
		catch (Exception value)
		{
			UnityEngine.Debug.LogWarning($"Azurite driver tick failed: {value}");
			GuiTick = null;
			Destroyed?.Invoke();
			Tick = null;
		}
	}

	private void LateUpdate()
	{
		try
		{
			LateTick?.Invoke((double)Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
		}
		catch (Exception value)
		{
			UnityEngine.Debug.LogWarning($"Azurite driver late tick failed: {value}");
			GuiTick = null;
			Destroyed?.Invoke();
			Tick = null;
			LateTick = null;
		}
	}

	private void OnGUI()
	{
		try { GuiTick?.Invoke(); }
		catch (Exception value)
		{
			UnityEngine.Debug.LogWarning($"Azurite driver GUI callback failed: {value}");
			// Retire callbacks before restoration, which can itself report failure.
			GuiTick = null;
			Tick = null;
			LateTick = null;
			Destroyed?.Invoke();
		}
	}

	private void OnDestroy()
	{
		Action? destroyed = Destroyed;
		Tick = null;
		LateTick = null;
		GuiTick = null;
		Destroyed = null;
		Quitting = null;
		destroyed?.Invoke();
	}

	private void OnApplicationQuit()
	{
		// Terminal shutdown must drop presentation ownership before Unity tears
		// down localization/services. Live disable and export still restore.
		Action? quitting = Quitting;
		Quitting = null;
		Tick = null;
		LateTick = null;
		GuiTick = null;
		quitting?.Invoke();
	}
}
