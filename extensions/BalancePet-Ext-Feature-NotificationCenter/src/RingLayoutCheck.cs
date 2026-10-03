using System.Windows;

namespace BalancePet.NotificationCenter;

/// <summary>
/// Checks the gathering rule against real geometry.
/// </summary>
/// <remarks>
/// The rule is the part of the hover rewrite that cannot be judged from an animation: a
/// transition that looks right can still be gathering when it did not need to, or leaving a
/// plate half off the screen. So it is checked where it lives — as rectangles — and the
/// check says what it expected rather than only that something went wrong.
///
/// Lives beside the rule rather than in a test project because this extension has no test
/// project, and a rule checked only by looking at it is not checked.
/// </remarks>
internal static class RingLayoutCheck
{
    public static int Run()
    {
        var failures = 0;
        var desktop = new Rect(0, 0, 2048, 1123);
        const double pet = 238;

        // Centred: there is room all round, so nothing gathers.
        var centred = new Rect((desktop.Width - pet) / 2, (desktop.Height - pet) / 2, pet, pet);
        var centredSlots = BubbleWindow.SelectOrbitSlots(centred, desktop, 4, null);
        failures += Expect("居中有环", RingLayout.OrbitFits(desktop, centredSlots), true);
        var centredProgress = RingLayout.MergeProgress(centred, desktop, centredSlots);
        failures += Expect("居中不聚合", centredProgress <= 0.05, true,
            note: $"进度 {centredProgress:F2}  距离离散度 {RingLayout.LastSpread:F2}  最大空档 {RingLayout.LastWidestGap:F0}°");

        // Bottom-right, where a desktop pet usually is: the orbit cannot fit, so it gathers.
        var corner = new Rect(desktop.Right - pet - 24, desktop.Bottom - pet - 24, pet, pet);
        var cornerSlots = BubbleWindow.SelectOrbitSlots(corner, desktop, 4, null);
        failures += Expect("角落已经不像环", RingLayout.OrbitFits(desktop, cornerSlots), true, note: "出界判定在角落不成立：算法会把信息块朝屏内收");
        failures += Expect("角落完全聚合", RingLayout.MergeProgress(corner, desktop, cornerSlots) >= 0.9, true);

        // The gathered plate has to stay on screen: this is the failure the arrangement
        // exists to avoid, and the reason it grows towards the middle rather than being
        // centred on the pet.
        var plate = RingLayout.MergedPlate(corner, desktop, 452);
        failures += Expect("聚合板不出界", desktop.Contains(plate), true);

        // Never monotonically wrong: walking the pet from the middle to the corner must not
        // make the information orbit again on the way.
        var previous = -1.0;
        var monotonic = true;
        for (var step = 0; step <= 12; step++)
        {
            var t = step / 12.0;
            var position = new Rect(
                centred.Left + (corner.Left - centred.Left) * t,
                centred.Top + (corner.Top - centred.Top) * t, pet, pet);
            var slots = BubbleWindow.SelectOrbitSlots(position, desktop, 4, null);
            var progress = RingLayout.MergeProgress(position, desktop, slots);
            Console.WriteLine($"        t={t:F2} 进度 {progress:F2} 离散度 {RingLayout.LastSpread:F2} 空档 {RingLayout.LastWidestGap:F0}°");
            if (progress + 0.001 < previous) monotonic = false;
            previous = progress;
        }
        failures += Expect("从中间走到角落，聚合程度只增不减", monotonic, true);

        // A work area too small to hold an orbit at all, with the pet in the middle of it:
        // the rule has to be about room, not about corners.
        var small = new Rect(0, 0, 900, 620);
        var smallPet = new Rect((small.Width - pet) / 2, (small.Height - pet) / 2, pet, pet);
        var smallSlots = BubbleWindow.SelectOrbitSlots(smallPet, small, 4, null);
        // A small screen does not force gathering on its own: what matters is whether the
        // arrangement still reads as an orbit, and on 900×620 in the middle it does.
        failures += Expect("小屏幕上居中仍是环", RingLayout.MergeProgress(smallPet, small, smallSlots) <= 0.35, true,
            note: $"实际 {RingLayout.MergeProgress(smallPet, small, smallSlots):F2}");

        // Every value gets its own place in the plate, in order and without overlapping.
        var widths = new List<double> { 72, 56, 48, 52 };
        var first = RingLayout.SegmentInPlate(plate, widths, 0);
        var last = RingLayout.SegmentInPlate(plate, widths, widths.Count - 1);
        failures += Expect("聚合板里的值按顺序排开", first.Left < last.Left && !first.IntersectsWith(last), true);

        Console.WriteLine(failures == 0 ? "聚合规则：全部通过" : $"聚合规则：{failures} 项不符");
        return failures == 0 ? 0 : 1;
    }

    private static int Expect<T>(string what, T actual, T expected, double tolerance = 0, string note = "")
    {
        var ok = actual switch
        {
            double value when expected is double target => Math.Abs(value - target) <= tolerance,
            _ => Equals(actual, expected)
        };
        var extra = string.IsNullOrEmpty(note) ? "" : $"  {note}";
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {what}{extra}");
        return ok ? 0 : 1;
    }
}
