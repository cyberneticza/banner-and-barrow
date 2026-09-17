using BannerAndBarrow.Simulation.Config;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Military;

/// <summary>Slot offsets for a Formation. Local space: X = right, Y = forward; the front rank is at the largest Y.</summary>
public static class FormationLayout
{
    public static FixVec2 SlotOffset(FormationDef def, int index, int count)
    {
        count = System.Math.Max(1, count);
        return def.Shape == FormationShape.Wedge ? WedgeSlot(def, index, count) : GridSlot(def, index, count);
    }

    /// <summary>How deep the formation is (front rank to rear rank), in tiles.</summary>
    public static Fix Depth(FormationDef def, int count)
    {
        count = System.Math.Max(1, count);
        if (def.Shape == FormationShape.Wedge) return def.Spacing * (WedgeRows(count) - 1);
        int width = GridWidth(def, count);
        int ranks = (count + width - 1) / width;
        return def.Spacing * (ranks - 1);
    }

    private static int GridWidth(FormationDef def, int count)
    {
        int width = def.Width > 0 ? def.Width : (int)IntMath.Sqrt((UInt128)(ulong)count);
        return System.Math.Clamp(width, 1, count);
    }

    private static FixVec2 GridSlot(FormationDef def, int index, int count)
    {
        int width = GridWidth(def, count);
        int ranks = (count + width - 1) / width;
        int rank = index / width;
        int file = index % width;
        int inRank = rank == ranks - 1 ? count - rank * width : width;
        // x centred on the rank; y: front rank forward, centred on the formation's middle.
        var x = (Fix.FromInt(file) - Fix.FromRatio(inRank - 1, 2)) * def.Spacing;
        var y = (Fix.FromRatio(ranks - 1, 2) - Fix.FromInt(rank)) * def.Spacing;
        return new FixVec2(x, y);
    }

    private static int WedgeRows(int count)
    {
        int rows = 0, placed = 0;
        while (placed < count)
        {
            placed += 2 * rows + 1;
            rows++;
        }
        return rows;
    }

    private static FixVec2 WedgeSlot(FormationDef def, int index, int count)
    {
        int rows = WedgeRows(count);
        int row = 0, start = 0;
        while (start + 2 * row + 1 <= index)
        {
            start += 2 * row + 1;
            row++;
        }
        int inRow = System.Math.Min(2 * row + 1, count - start);
        int file = index - start;
        var x = (Fix.FromInt(file) - Fix.FromRatio(inRow - 1, 2)) * def.Spacing;
        var y = (Fix.FromRatio(rows - 1, 2) - Fix.FromInt(row)) * def.Spacing;
        return new FixVec2(x, y);
    }
}
