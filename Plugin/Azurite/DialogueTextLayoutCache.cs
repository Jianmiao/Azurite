using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppSystem.Collections.Generic;
using Studio.Scripts;
using UnityEngine;

namespace Azurite;

internal sealed class DialogueTextLayoutCache : IDisposable
{
	private readonly record struct RefreshScope(ScriptListItem? Previous, bool Entered);

	private readonly record struct Pending(ScriptListItem? Row, string? Text, int Size, bool ApplyAll)
	{
		public bool Valid
		{
			get
			{
				if (Row != null)
				{
					return Text != null;
				}
				return false;
			}
		}
	}

	private sealed record Entry(PhoneticText Phonetic, ScriptListItem Row, LayoutKey Key, OutputState Output, SharedState Shared);

	// A row's list position changes during UIGrid reflow but does not change the
	// text layout. Scale/rotation remain part of the key because they can affect
	// the effective label geometry.
	private readonly record struct LayoutKey(string Text, int Size, bool ApplyAll, bool Unlimited, IntPtr Region, IntPtr Prefab, LabelStyle RegionStyle, LabelStyle PrefabStyle, Vector3 Scale, Quaternion Rotation, Vector3 RegionPosition, Vector3 RegionScale, Quaternion RegionRotation);

	private readonly record struct OutputState(IntPtr Chunks, int Count, ChunkState First, ChunkState Last, int ChildCount, float CurrentPosition, float RegionWidth);

	private readonly record struct ChunkState(IntPtr Chunk, IntPtr Instances, int Count, IntPtr First, IntPtr Last, string FirstText, string LastText, int FirstSize, int LastSize);

	private readonly record struct LabelStyle(IntPtr Font, IntPtr FontObject, IntPtr Material, int Width, int Height, int Size, FontStyle Style, NGUIText.Alignment Alignment, UIWidget.Pivot Pivot, int SpacingX, int SpacingY, bool FloatingSpacing, float FloatX, float FloatY, bool Encoding, NGUIText.SymbolStyle Symbols, UILabel.Overflow Overflow, bool Ellipsis, int LineWidth, int LineHeight, Color Color, bool Gradient, Color Top, Color Bottom, UILabel.Effect Effect, Color EffectColor, Vector2 EffectDistance)
	{
		public static LabelStyle Read(UILabel label)
		{
			return new LabelStyle(Pointer(label.trueTypeFont), Pointer(label.ambigiousFont), Pointer(label.material), label.width, label.height, label.fontSize, label.fontStyle, label.alignment, label.pivot, label.spacingX, label.spacingY, label.useFloatSpacing, label.floatSpacingX, label.floatSpacingY, label.supportEncoding, label.symbolStyle, label.overflowMethod, label.overflowEllipsis, label.lineWidth, label.lineHeight, label.color, label.applyGradient, label.gradientTop, label.gradientBottom, label.effectStyle, label.effectColor, label.effectDistance);
		}
	}

	private readonly record struct SharedState(IntPtr Font, IntPtr DynamicFont, int SpaceWidth, int FontSize, float FontScale, float Density, FontStyle Style, NGUIText.Alignment Alignment, Color Tint, int RectWidth, int RectHeight, int RegionWidth, int RegionHeight, int MaxLines, bool Gradient, Color Bottom, Color Top, bool Encoding, float SpacingX, float SpacingY, bool Premultiply, NGUIText.SymbolStyle Symbols, int FinalSize, float FinalSpacing, float LineHeight, float Baseline, bool UseSymbols, float Alpha, float SymbolScale, float SymbolOffset, int SymbolMaxHeight, bool SymbolCentered, GlyphState Glyph)
	{
		public static SharedState Read()
		{
			return new SharedState(Pointer(NGUIText.nguiFont), Pointer(NGUIText.dynamicFont), NGUIText.spaceWidth, NGUIText.fontSize, NGUIText.fontScale, NGUIText.pixelDensity, NGUIText.fontStyle, NGUIText.alignment, NGUIText.tint, NGUIText.rectWidth, NGUIText.rectHeight, NGUIText.regionWidth, NGUIText.regionHeight, NGUIText.maxLines, NGUIText.gradient, NGUIText.gradientBottom, NGUIText.gradientTop, NGUIText.encoding, NGUIText.spacingX, NGUIText.spacingY, NGUIText.premultiply, NGUIText.symbolStyle, NGUIText.finalSize, NGUIText.finalSpacingX, NGUIText.finalLineHeight, NGUIText.baseline, NGUIText.useSymbols, NGUIText.mAlpha, NGUIText.symbolScale, NGUIText.symbolOffset, NGUIText.symbolMaxHeight, NGUIText.symbolCentered, GlyphState.Read());
		}
	}

	private readonly record struct GlyphState(IntPtr Pointer, Vector2 V0, Vector2 V1, Vector2 U0, Vector2 U1, Vector2 U2, Vector2 U3, float Advance, int Channel)
	{
		public static GlyphState Read()
		{
			NGUIText.GlyphInfo glyph = NGUIText.glyph;
			if (glyph != null)
			{
				return new GlyphState(glyph.Pointer, glyph.v0, glyph.v1, glyph.u0, glyph.u1, glyph.u2, glyph.u3, glyph.advance, glyph.channel);
			}
			return default(GlyphState);
		}
	}

	private const string HarmonyId = "halocue.azurite.dialogue-text-layout-cache";

	private const int MaximumEntries = 2048;

	private const int MaximumTextLength = 8192;

	private static DialogueTextLayoutCache? _active;

	[ThreadStatic]
	private static ScriptListItem? _refreshRow;

	private readonly Action<string> _log;

	private readonly Harmony _harmony = new Harmony("halocue.azurite.dialogue-text-layout-cache");

	private readonly BoundedRowCache<IntPtr, Entry> _entries = new(MaximumEntries);

	private bool _installed;

	private bool _disposed;

	private bool _allowed;

	private bool _failed;

	private double _nextCleanup;

	private double _nextReport;

	private long _hits;

	private long _misses;

	private long _globalMismatch;

	private long _invalidated;

	private long _errors;

	public DialogueTextLayoutCache(Action<string> log)
	{
		_log = log ?? throw new ArgumentNullException("log");
	}

	public bool Install()
	{
		if (_disposed || _failed || (_active != null && _active != this))
		{
			return false;
		}
		if (_installed)
		{
			return true;
		}
		try
		{
			_active = this;
			Patch(typeof(ScriptListItem), "Refresh", Type.EmptyTypes, "BeforeRefresh", null, "AfterRefresh");
			Patch(typeof(PhoneticText), "Initialize", new Type[3]
			{
				typeof(string),
				typeof(int),
				typeof(bool)
			}, "BeforeInitialize", "AfterInitialize", null);
			Patch(typeof(PhoneticText), "CleanUp", Type.EmptyTypes, "BeforeCleanUp", null, null);
			_installed = true;
			_allowed = true;
			_log("Experimental dialogue layout cache installed; identical existing rows only, shared NGUI state must already match. First layout is unchanged.");
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
			_log("Dialogue layout cache unavailable: " + ex.GetType().Name + ".");
			return false;
		}
	}

	private void Patch(Type type, string name, Type[] parameters, string prefix, string? postfix, string? finalizer)
	{
		MethodInfo methodInfo = AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.FullName, name);
		_harmony.Patch(methodInfo, new HarmonyMethod(typeof(DialogueTextLayoutCache), prefix), (postfix == null) ? null : new HarmonyMethod(typeof(DialogueTextLayoutCache), postfix), null, (finalizer == null) ? null : new HarmonyMethod(typeof(DialogueTextLayoutCache), finalizer), null);
		Patches patchInfo = Harmony.GetPatchInfo(methodInfo);
		if (patchInfo == null || !patchInfo.Owners.Contains("halocue.azurite.dialogue-text-layout-cache"))
		{
			throw new InvalidOperationException("Layout cache hook was not registered: " + name);
		}
	}

	public void Update(double now, bool allowed = true)
	{
		if (_disposed || !_installed || !double.IsFinite(now))
		{
			return;
		}
		if (!allowed)
		{
			Suspend();
		}
		else if (!_failed)
		{
			_allowed = true;
		}
		if (now >= _nextCleanup)
		{
			_nextCleanup = now + 1.0;
			_invalidated += _entries.Prune(static value => value.Phonetic != null && value.Row != null, 64);
		}
		if (!(now < _nextReport))
		{
			_nextReport = now + 5.0;
			if (_hits != 0L || _misses != 0L || _invalidated != 0L || _errors != 0L)
			{
				_log($"dialogue-layout-cache hits={_hits} misses={_misses} sharedStateMisses={_globalMismatch} invalidations={_invalidated} errors={_errors} entries={_entries.Count} enabled={_allowed && !_failed}. Reuses already-created labels; no first-open acceleration claimed.");
				_hits = (_misses = (_globalMismatch = (_invalidated = (_errors = 0L))));
			}
		}
	}

	public void Suspend()
	{
		_allowed = false;
		_entries.Clear();
	}

	private static void BeforeRefresh(ScriptListItem __instance, out RefreshScope __state)
	{
		__state = new RefreshScope(_refreshRow, Entered: false);
		DialogueTextLayoutCache active = _active;
		if (active != null && active._allowed && !active._disposed && !active._failed)
		{
			__state = new RefreshScope(_refreshRow, Entered: true);
			_refreshRow = __instance;
		}
	}

	private static void AfterRefresh(RefreshScope __state)
	{
		if (__state.Entered)
		{
			_refreshRow = __state.Previous;
		}
	}

	private static bool BeforeInitialize(PhoneticText __instance, string original, int sizeOverride, bool applyOverrideToAll, out Pending __state)
	{
		__state = default(Pending);
		DialogueTextLayoutCache active = _active;
		if (active == null || !active._allowed || active._disposed || active._failed)
		{
			return true;
		}
		try
		{
			ScriptListItem refreshRow = _refreshRow;
			if (refreshRow == null || __instance == null || refreshRow.scriptPhonetic == null || refreshRow.scriptPhonetic.Pointer != __instance.Pointer || original == null || original.Length > 8192)
			{
				return true;
			}
			__state = new Pending(refreshRow, original, sizeOverride, applyOverrideToAll);
			IntPtr pointer = __instance.Pointer;
			if (active._entries.TryGetValue(pointer, out Entry value) && value.Row != null && value.Row.Pointer == refreshRow.Pointer && TryKey(__instance, original, sizeOverride, applyOverrideToAll, out var key) && value.Key == key && TryOutput(__instance, out var output) && value.Output == output)
			{
				if (value.Shared == SharedState.Read())
				{
					active._hits++;
					__state = default(Pending);
					return false;
				}
				active._globalMismatch++;
			}
			active._misses++;
			return true;
		}
		catch
		{
			active.FailClosed();
			__state = default(Pending);
			return true;
		}
	}

	private static void AfterInitialize(PhoneticText __instance, Pending __state)
	{
		DialogueTextLayoutCache active = _active;
		if (!__state.Valid || active == null || !active._allowed || active._disposed || active._failed)
		{
			return;
		}
		try
		{
			if (!(__instance == null) && !(__state.Row == null) && TryKey(__instance, __state.Text, __state.Size, __state.ApplyAll, out var key) && TryOutput(__instance, out var output))
			{
				IntPtr pointer = __instance.Pointer;
				active._invalidated += active._entries.Store(pointer, new Entry(__instance, __state.Row, key, output, SharedState.Read()));
			}
		}
		catch
		{
			active.FailClosed();
		}
	}

	private static void BeforeCleanUp(PhoneticText __instance)
	{
		DialogueTextLayoutCache active = _active;
		if (active == null || active._disposed)
		{
			return;
		}
		try
		{
			if (__instance != null && active._entries.Remove(__instance.Pointer))
			{
				active._invalidated++;
			}
		}
		catch
		{
			active.FailClosed();
		}
	}

	private void FailClosed()
	{
		_errors++;
		_failed = true;
		Suspend();
	}

	private static bool TryKey(PhoneticText text, string original, int size, bool applyAll, out LayoutKey key)
	{
		key = default(LayoutKey);
		UILabel regionText = text.regionText;
		GameObject textPrefab = text.textPrefab;
		if (regionText == null || textPrefab == null || !regionText.isActiveAndEnabled)
		{
			return false;
		}
		UILabel component = textPrefab.GetComponent<UILabel>();
		if (component == null)
		{
			return false;
		}
		Transform transform = text.transform;
		Transform transform2 = regionText.transform;
		key = new LayoutKey(original, size, applyAll, text.unlimitedLineWidth, regionText.Pointer, textPrefab.Pointer, LabelStyle.Read(regionText), LabelStyle.Read(component), transform.localScale, transform.localRotation, transform2.localPosition, transform2.localScale, transform2.localRotation);
		return true;
	}

	private static bool TryOutput(PhoneticText text, out OutputState output)
	{
		output = default(OutputState);
		Il2CppSystem.Collections.Generic.List<PhoneticText.TextChunk> textChunks = text.textChunks;
		Il2CppSystem.Collections.Generic.List<string> activeColorTags = text.activeColorTags;
		if (textChunks == null || textChunks.Count == 0 || activeColorTags == null || activeColorTags.Count != 0)
		{
			return false;
		}
		PhoneticText.TextChunk textChunk = textChunks[0];
		PhoneticText.TextChunk textChunk2 = textChunks[textChunks.Count - 1];
		if (textChunk == null || textChunk2 == null || !TryChunk(textChunk, out var state) || !TryChunk(textChunk2, out var state2))
		{
			return false;
		}
		output = new OutputState(textChunks.Pointer, textChunks.Count, state, state2, text.transform.childCount, text.currentPos, text.regionWidth);
		return true;
	}

	private static bool TryChunk(PhoneticText.TextChunk chunk, out ChunkState state)
	{
		state = default(ChunkState);
		Il2CppSystem.Collections.Generic.List<UILabel> instances = chunk.instances;
		if (instances == null || instances.Count == 0)
		{
			return false;
		}
		UILabel uILabel = instances[0];
		UILabel uILabel2 = instances[instances.Count - 1];
		if (uILabel == null || uILabel2 == null || uILabel.gameObject == null || uILabel2.gameObject == null || !uILabel.gameObject.activeInHierarchy || !uILabel2.gameObject.activeInHierarchy)
		{
			return false;
		}
		state = new ChunkState(chunk.Pointer, instances.Pointer, instances.Count, uILabel.Pointer, uILabel2.Pointer, uILabel.text, uILabel2.text, uILabel.fontSize, uILabel2.fontSize);
		return true;
	}

	private static IntPtr Pointer(Il2CppObjectBase? value)
	{
		return value?.Pointer ?? IntPtr.Zero;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			Suspend();
			_disposed = true;
			if (_active == this)
			{
				_active = null;
			}
			_refreshRow = null;
			if (_installed)
			{
				_harmony.UnpatchSelf();
			}
			_installed = false;
		}
	}
}
