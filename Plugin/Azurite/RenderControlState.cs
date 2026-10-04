namespace Azurite;

internal readonly record struct RenderControlState(bool Healthy, bool Ready, bool Owned, string Reason);
