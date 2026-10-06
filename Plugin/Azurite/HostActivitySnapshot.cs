namespace Azurite;

internal readonly record struct HostActivitySnapshot(bool IsEditor, bool CanThrottle, bool HasInteraction, string Reason, bool HasPreview, bool HasDynamicPreview, bool CanCachePreview = false, bool AmbientPreview = false)
{
	public static HostActivitySnapshot Safe(string reason, bool hasPreview = false)
	{
		return new HostActivitySnapshot(IsEditor: true, CanThrottle: true, HasInteraction: false, reason, hasPreview, HasDynamicPreview: false, CanCachePreview: hasPreview);
	}

	public static HostActivitySnapshot Ambient(string reason) => new(true, true, false, reason,
		HasPreview: true, HasDynamicPreview: true, CanCachePreview: false, AmbientPreview: true);

	public static HostActivitySnapshot Blocked(string reason, bool isEditor, bool hasPreview = false, bool hasDynamicPreview = false)
	{
		return new HostActivitySnapshot(isEditor, CanThrottle: false, HasInteraction: true, reason, hasPreview, hasDynamicPreview);
	}

	public static HostActivitySnapshot Unknown(string reason, bool hasPreview = false)
	{
		return new HostActivitySnapshot(IsEditor: false, CanThrottle: false, HasInteraction: true, reason, hasPreview, HasDynamicPreview: false);
	}
}
