using System;
using System.Collections.Generic;

namespace Azurite;

/// <summary>Pure identity-only plan used before touching any Unity object.</summary>
internal readonly record struct DialogueRowReusePlan(bool Valid, int[] ReuseBeforeIndices)
{
	public bool IsInsertion => Valid && ReuseBeforeIndices.Length > 0 && Array.IndexOf(ReuseBeforeIndices, -1) >= 0;

	public static bool TryCreate(IReadOnlyList<IntPtr> before, IReadOnlyList<IntPtr> after, out DialogueRowReusePlan plan)
	{
		plan = default;
		if (before == null || after == null || before.Count < 1 || Math.Abs(after.Count - before.Count) != 1) return false;
		var oldIds = new HashSet<IntPtr>();
		var newIds = new HashSet<IntPtr>();
		for (int i = 0; i < before.Count; i++) if (before[i] == IntPtr.Zero || !oldIds.Add(before[i])) return false;
		for (int i = 0; i < after.Count; i++) if (after[i] == IntPtr.Zero || !newIds.Add(after[i])) return false;
		int[] map = new int[after.Count];
		if (after.Count == before.Count + 1)
		{
			int old = 0;
			bool skipped = false;
			for (int current = 0; current < after.Count; current++)
			{
				if (old < before.Count && before[old] == after[current]) { map[current] = old++; continue; }
				if (skipped) return false;
				map[current] = -1;
				skipped = true;
			}
			if (old != before.Count || !skipped) return false;
		}
		else
		{
			int currentOld = 0;
			bool skipped = false;
			for (int current = 0; current < after.Count; current++)
			{
				if (currentOld < before.Count && before[currentOld] == after[current]) { map[current] = currentOld++; continue; }
				if (skipped || currentOld >= before.Count - 1) return false;
				currentOld++;
				skipped = true;
				if (before[currentOld] != after[current]) return false;
				map[current] = currentOld++;
			}
			if (!skipped)
			{
				// The removed identity may be the last row; no mismatch appears
				// while walking the new list in that case.
				if (currentOld != before.Count - 1) return false;
			}
			else if (currentOld != before.Count) return false;
		}
		plan = new DialogueRowReusePlan(true, map);
		return true;
	}
}
