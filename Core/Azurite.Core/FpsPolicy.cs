namespace Azurite.Core;

public static class FpsPolicy
{
	public static FpsPlan FromTier(int tier, int originalTargetFrameRate, int originalVSyncCount)
	{
		return tier switch
		{
			0 => new FpsPlan(60, 0),
			1 => new FpsPlan(120, 0),
			2 => new FpsPlan(-1, 0),
			_ => new FpsPlan(originalTargetFrameRate, originalVSyncCount),
		};
	}
}
