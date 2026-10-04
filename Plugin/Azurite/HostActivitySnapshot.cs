namespace Azurite;

internal readonly record struct HostActivitySnapshot(bool IsEditor, bool CanThrottle, bool HasInteraction, string Reason, bool HasPreview)
{
	public static HostActivitySnapshot Safe(string reason, bool hasPreview = false)
	{
		return new HostActivitySnapshot(IsEditor: true, CanThrottle: true, HasInteraction: false, reason, hasPreview);
	}

	public static HostActivitySnapshot Blocked(string reason, bool isEditor, bool hasPreview = false)
	{
		return new HostActivitySnapshot(isEditor, CanThrottle: false, HasInteraction: true, reason, hasPreview);
	}

	public static HostActivitySnapshot Unknown(string reason, bool hasPreview = false)
	{
		return new HostActivitySnapshot(IsEditor: false, CanThrottle: false, HasInteraction: true, reason, hasPreview);
	}
}
