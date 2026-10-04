using System;

namespace Azurite;

internal readonly record struct PreviewResolutionPlan(int Width, int Height)
{
	public static bool TryCreate(int originalWidth, int originalHeight, double displayWidth, double displayHeight, out PreviewResolutionPlan plan)
	{
		plan = default(PreviewResolutionPlan);
		if (originalWidth < 16 || originalHeight < 16 || !double.IsFinite(displayWidth) || !double.IsFinite(displayHeight) || displayWidth <= 0.0 || displayHeight <= 0.0)
		{
			return false;
		}
		int num = GreatestCommonDivisor(originalWidth, originalHeight);
		int num2 = originalWidth / num;
		int num3 = originalHeight / num;
		int num4 = 16 / GreatestCommonDivisor(num2, 16);
		double num5 = Math.Ceiling(Math.Max(displayWidth / (double)num2, displayHeight / (double)num3) / (double)num4) * (double)num4;
		if (num5 <= 0.0 || num5 > (double)num)
		{
			return false;
		}
		double num6 = num5 * (double)num2;
		double num7 = num5 * (double)num3;
		if (num6 > (double)originalWidth * 0.95 || num7 > (double)originalHeight * 0.95 || num7 < 16.0)
		{
			return false;
		}
		plan = new PreviewResolutionPlan((int)num6, (int)num7);
		return true;
	}

	public bool DiffersByFivePercent(PreviewResolutionPlan other)
	{
		if (Width > 0 && Height > 0)
		{
			if (!((double)Math.Abs(other.Width - Width) >= (double)Width * 0.05))
			{
				return (double)Math.Abs(other.Height - Height) >= (double)Height * 0.05;
			}
			return true;
		}
		return false;
	}

	private static int GreatestCommonDivisor(int a, int b)
	{
		while (b != 0)
		{
			int num = a % b;
			a = b;
			b = num;
		}
		return a;
	}
}
