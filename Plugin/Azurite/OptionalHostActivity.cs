using System;
using System.Reflection;

namespace Azurite;

// Generated bindings differ between native releases. The native identity gate
// remains mandatory; absent optional producers are absent on that release.
// A present but unreadable/mistyped producer remains active conservatively.
internal static class OptionalHostActivity
{
	internal static bool CurrentEffectActive(Test preview)
	{
		try { var effect = preview.currentBGEffectInstance; return effect != null && effect.activeInHierarchy; }
		catch { return true; }
	}
	private static readonly PropertyInfo? Search = Find(typeof(Catalog), "searchPending");
	private static readonly PropertyInfo? Effect = Find(typeof(Test), "customBGEffectInstance");
	private static readonly Func<Catalog, bool>? ReadSearch = Getter<Catalog, bool>(Search);
	private static readonly Func<Test, UnityEngine.GameObject>? ReadEffect = Getter<Test, UnityEngine.GameObject>(Effect);
	private static PropertyInfo? Find(Type type, string name) => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
	private static Func<T, TResult>? Getter<T, TResult>(PropertyInfo? property)
	{
		try { return property?.PropertyType == typeof(TResult) ? property.GetMethod?.CreateDelegate<Func<T, TResult>>() : null; }
		catch { return null; }
	}
	internal static bool SearchPending(Catalog catalog)
	{
		if (Search == null) return false;
		try { return ReadSearch == null || ReadSearch(catalog); }
		catch { return true; }
	}
	internal static bool CustomEffectActive(Test preview)
	{
		if (Effect == null) return false;
		try
		{
			if (ReadEffect == null) return true;
			var effect = ReadEffect(preview);
			return effect != null && effect.activeInHierarchy;
		}
		catch { return true; }
	}
}
