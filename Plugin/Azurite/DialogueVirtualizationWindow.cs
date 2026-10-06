using System;
using System.Collections.Generic;

namespace Azurite;

// Geometry is measured from AA's actual prefab. No user data is owned here.
internal static class DialogueVirtualizationWindow
{
    internal const int Overscan = 3;
    internal const int MaximumRows = 128;
    internal const int RowsPerFrame = 2;
    internal const double MillisecondsPerFrame = 4;

    // Rendering order is independent of model order. Populate the visible
    // viewport bottom-to-top first, then its offscreen overscan/pinned range.
    internal static int[] FillOrder(int[] desired, float firstY, float pitch, float top, float bottom)
    {
        int firstVisible = (int)Math.Floor((firstY - top) / pitch);
        int lastVisible = (int)Math.Floor((firstY - bottom) / pitch);
        var result = new List<int>(desired.Length);
        for (int n = desired.Length - 1; n >= 0; n--)
            if (desired[n] >= firstVisible && desired[n] <= lastVisible) result.Add(desired[n]);
        for (int n = desired.Length - 1; n >= 0; n--)
            if (desired[n] < firstVisible || desired[n] > lastVisible) result.Add(desired[n]);
        return result.ToArray();
    }

    internal static int[] Plan(int count, float firstY, float pitch, float top, float bottom, int selected, int required, int restored)
    {
        if (count < 0 || !float.IsFinite(firstY) || !float.IsFinite(pitch) || pitch <= 0 || !float.IsFinite(top) || !float.IsFinite(bottom) || top < bottom)
            throw new ArgumentException("Invalid dialogue viewport geometry");
        if (count == 0) return Array.Empty<int>();
        int start = Math.Clamp((int)Math.Floor((firstY - top) / pitch) - Overscan, 0, count - 1);
        int end = Math.Clamp((int)Math.Ceiling((firstY - bottom) / pitch) + Overscan, 0, count - 1);
        if (end - start + 1 > MaximumRows - 4) throw new InvalidOperationException("Visible dialogue range exceeds bounded pool");
        var indices = new SortedSet<int>();
        for (int i = start; i <= end; i++) indices.Add(i);
        // Native FirstOnTop asks the first physical child for its geometry.
        indices.Add(0);
        foreach (int index in new[] { selected, required, restored }) if (index >= 0 && index < count) indices.Add(index);
        int[] result = new int[indices.Count]; indices.CopyTo(result); return result;
    }
}
