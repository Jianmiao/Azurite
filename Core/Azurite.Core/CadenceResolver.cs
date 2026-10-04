using System;

namespace Azurite.Core;

public static class CadenceResolver
{
	private const double TargetTolerance = 1.03;

	public static CadencePlan Resolve(double updateFps, double idleFps = 30.0, double deepFps = 15.0, int maxInterval = 120)
	{
		if (!double.IsFinite(updateFps) || updateFps <= 0.0)
		{
			return new CadencePlan(Valid: false, 1, 1, "unknown-update-rate");
		}
		if (!double.IsFinite(idleFps) || !double.IsFinite(deepFps) || idleFps <= 0.0 || deepFps <= 0.0 || deepFps > idleFps || maxInterval < 1)
		{
			return new CadencePlan(Valid: false, 1, 1, "invalid-cadence-targets");
		}
		double num = RequiredInterval(updateFps, idleFps);
		double num2 = RequiredInterval(updateFps, deepFps);
		bool flag = num > (double)maxInterval || num2 > (double)maxInterval;
		return new CadencePlan(Valid: true, ClampInterval(num, maxInterval), ClampInterval(num2, maxInterval), flag ? "interval-limit" : "measured-update-rate");
	}

	private static double RequiredInterval(double updateFps, double targetFps)
	{
		return Math.Ceiling(updateFps / targetFps / 1.03);
	}

	private static int ClampInterval(double required, int maximum)
	{
		if (required <= 1.0)
		{
			return 1;
		}
		if (!(required >= (double)maximum))
		{
			return (int)required;
		}
		return maximum;
	}
}
