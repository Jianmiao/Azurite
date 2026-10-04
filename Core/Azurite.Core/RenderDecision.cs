namespace Azurite.Core;

public readonly record struct RenderDecision(RenderMode Mode, int Interval, string Reason);
