using System.Windows;

namespace BalancePet.NotificationCenter;

/// <summary>
/// Where the hover information goes: around the pet while there is room, gathered into one
/// plate when there is not.
/// </summary>
/// <remarks>
/// The rule is "does an orbit fit", not "is the pet in a corner". A corner is one way to run
/// out of room, and a pet half off the bottom edge, or on a small monitor, or beside a taskbar
/// it has been dragged against, are others — all of which a position test would miss. What
/// matters is whether the items can sit around the pet without overlapping each other or
/// leaving the work area, which is a question about the rectangles and can simply be asked.
///
/// Pure geometry on purpose: no window, no drawing, nothing that needs a screen. That is what
/// makes the rule reviewable — the frames rendered from it are the rule, not a picture of it —
/// and it is the part of the rewrite that cannot be checked by looking at an animation.
/// </remarks>
internal static class RingLayout
{
    /// <summary>The last measured distance spread, for the checks to report.</summary>
    internal static double LastSpread { get; private set; }

    /// <summary>The last measured widest empty wedge, in degrees.</summary>
    internal static double LastWidestGap { get; private set; }

    /// <summary>How far a gathered plate sits from the pet, and how tall it is.</summary>
    private const double PlateGap = 20;
    private const double PlateHeight = 52;
    private const double PlateFootLift = 30;

    /// <summary>
    /// Whether every slot sits inside the work area and clear of the others.
    /// </summary>
    /// <remarks>
    /// Touching the edge is not a failure; a slot that is <em>outside</em> it is, because the
    /// part that hangs off is a part nobody can read. Overlap is measured with a small margin,
    /// since two plates a pixel apart read as one crowded mess even though they technically
    /// do not intersect.
    /// </remarks>
    public static bool OrbitFits(Rect workArea, IReadOnlyList<Rect> slots, double clearance = 6)
    {
        if (slots.Count == 0) return false;
        foreach (var slot in slots)
        {
            if (slot.Width <= 0 || slot.Height <= 0) return false;
            if (!workArea.Contains(slot)) return false;
        }

        for (var first = 0; first < slots.Count; first++)
        {
            for (var second = first + 1; second < slots.Count; second++)
            {
                var a = slots[first];
                var b = slots[second];
                a.Inflate(clearance / 2, clearance / 2);
                b.Inflate(clearance / 2, clearance / 2);
                if (a.IntersectsWith(b)) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// How far the information has gathered: 0 in orbit, 1 in one plate.
    /// </summary>
    /// <remarks>
    /// Measured as "how far this is from being an orbit" rather than as "does it fit". The
    /// first version of this rule asked whether the slots stayed inside the work area, and
    /// the checks showed it would never fire: the slot algorithm already fans the items
    /// inwards near an edge, so they never leave the screen — what they do instead is stop
    /// looking like an orbit. At a corner the fan collapses into a steep line, at wildly
    /// different distances from the pet, which is the thing that reads as wrong.
    ///
    /// So two properties of a real orbit are measured. The distances should be comparable, so
    /// no item sits far out while another hugs the pet; and the items should be spread around
    /// it, so no huge wedge of the circle is empty. Both are ratios, so the same thresholds
    /// hold on any monitor and at any pet size. Leaving the work area stays in as a third
    /// term, for a screen small enough that nothing fits however it is arranged.
    /// </remarks>
    public static double MergeProgress(Rect pet, Rect workArea, IReadOnlyList<Rect> slots)
    {
        if (slots.Count < 2) return 1;

        var centre = new Point(pet.Left + pet.Width / 2, pet.Top + pet.Height / 2);
        var distances = new List<double>(slots.Count);
        var angles = new List<double>(slots.Count);
        foreach (var slot in slots)
        {
            var point = new Point(slot.Left + slot.Width / 2, slot.Top + slot.Height / 2);
            distances.Add(Math.Max(1, Math.Sqrt(Math.Pow(point.X - centre.X, 2) + Math.Pow(point.Y - centre.Y, 2))));
            angles.Add(Math.Atan2(point.Y - centre.Y, point.X - centre.X) * 180 / Math.PI);
        }

        // How uneven the distances are: the spread as a fraction of the average.
        var mean = distances.Average();
        var spread = (distances.Max() - distances.Min()) / mean;

        // The widest empty wedge: a ring leaves every gap similar, a fan leaves one huge.
        angles.Sort();
        var widestGap = 0.0;
        for (var index = 0; index < angles.Count; index++)
        {
            var next = index + 1 < angles.Count ? angles[index + 1] : angles[0] + 360;
            widestGap = Math.Max(widestGap, next - angles[index]);
        }

        LastSpread = spread;
        LastWidestGap = widestGap;
        var uneven = Math.Clamp((spread - 0.55) / 0.60, 0, 1);
        var oneSided = Math.Clamp((widestGap - 150) / 110, 0, 1);
        var outside = Math.Clamp(slots.Max(slot => Outside(slot, workArea)) / 30.0, 0, 1);
        return Math.Max(outside, Math.Max(uneven, oneSided));
    }

    /// <summary>How far a rectangle reaches outside another, in pixels; zero when it fits.</summary>
    private static double Outside(Rect inner, Rect outer)
        => Math.Max(0, Math.Max(
            Math.Max(outer.Left - inner.Left, inner.Right - outer.Right),
            Math.Max(outer.Top - inner.Top, inner.Bottom - outer.Bottom)));

    /// <summary>
    /// The plate the items gather into: beside the pet, level with its feet, growing towards
    /// the middle of the screen.
    /// </summary>
    /// <remarks>
    /// Towards the middle, always, because the pet is usually in a corner when this happens —
    /// and a plate wider than the pet, centred on it, would run off the screen it is already
    /// against. Which side and which vertical band are decided by where the free room is, not
    /// by a fixed preference.
    /// </remarks>
    public static Rect MergedPlate(Rect pet, Rect workArea, double width)
    {
        var height = Math.Min(PlateHeight, Math.Max(24, pet.Height * 0.3));
        var bottom = pet.Bottom - PlateFootLift;

        // Horizontal: the side with more room wins.
        var roomLeft = pet.Left - workArea.Left;
        var roomRight = workArea.Right - pet.Right;
        var left = roomLeft >= roomRight
            ? pet.Left - PlateGap - width
            : pet.Right + PlateGap;

        // Kept inside the work area rather than assumed to fit: a plate that runs off the edge
        // is the failure this whole arrangement exists to avoid.
        left = Math.Clamp(left, workArea.Left + 8, Math.Max(workArea.Left + 8, workArea.Right - width - 8));
        var top = Math.Clamp(bottom - height, workArea.Top + 8, Math.Max(workArea.Top + 8, workArea.Bottom - height - 8));
        return new Rect(left, top, width, height);
    }

    /// <summary>
    /// Where one value sits inside the gathered plate, given how wide each of them is.
    /// </summary>
    /// <remarks>
    /// Measured from the widths rather than spread evenly: even spacing makes short values
    /// float apart and long ones collide, and the row is meant to read as one line of text
    /// with dividers between its parts.
    /// </remarks>
    public static Rect SegmentInPlate(Rect plate, IReadOnlyList<double> widths, int index, double padding = 16, double gap = 24)
    {
        var total = widths.Sum() + gap * Math.Max(0, widths.Count - 1);
        var cursor = plate.Left + Math.Max(padding, (plate.Width - total) / 2);
        for (var step = 0; step < index && step < widths.Count; step++) cursor += widths[step] + gap;
        var width = index < widths.Count ? widths[index] : 0;
        return new Rect(cursor, plate.Top + 3, width + 16, plate.Height - 6);
    }
}
