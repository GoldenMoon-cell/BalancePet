using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;

namespace BalancePet.NotificationCenter;

/// <summary>
/// One thing the ring has to say.
/// </summary>
/// <param name="Short">
/// The value alone, for when the items gather into a caption row. A plate 450 pixels wide has
/// no room for "当前登录方式 · 官方登录" four times over, and a row of values is what a caption
/// is; the sentence belongs to the orbit, where each item has room to be one.
/// </param>
public sealed record NotificationBubble(string Text, string Detail, string Kind, string Short = "");

public partial class BubbleWindow : Window
{
    private const int VkShift = 0x10;
    private const int TransitionMs = 180;
    private const int LayoutTransitionMs = 260;
    private const int StaggerMs = 90;
    private const double PetSurfaceSize = 238;
    private const double InfoWidth = 205;
    private const double InfoHeight = 48;
    private const double PetGap = 4;
    private const double SlotGap = 10;
    private const double OverlayPadding = 4;
    private readonly DispatcherTimer _triggerTimer = new() { Interval = TimeSpan.FromMilliseconds(45) };
    private readonly List<NotificationBubble> _items = [];
    private readonly List<InfoVisual> _visuals = [];
    private readonly List<Rect> _lastScreenTargets = [];
    private CancellationTokenSource? _animationCancellation;
    private bool _requestedVisible;
    private bool _hasPositionedItems;
    private Rect _overlayWorkArea = Rect.Empty;
    private Rect _lastLayoutPetBounds = Rect.Empty;
    private Rect _lastLayoutWorkArea = Rect.Empty;
    private string _itemsFingerprint = "";
    private string? _coreSettingsPath;
    private DateTime _coreSettingsWriteUtc = DateTime.MinValue;
    private PetPlacement _cachedPetPlacement = new(false, 1);
    private Border? _mergedPlate;
    private double _mergeProgress;
    private bool _mergeDark;
    private Ellipse? _wave;

    public BubbleWindow()
    {
        InitializeComponent();
        ApplyTheme();
        SourceInitialized += (_, _) => EnableMousePassthrough();
        _triggerTimer.Tick += (_, _) => PollTrigger();
        _triggerTimer.Start();
    }

    /// <summary>
    /// Lays the ring out for a preview, without the cursor and the Shift key that normally
    /// ask for it, and stops polling so nothing moves it afterwards.
    /// </summary>
    /// <remarks>
    /// Exists so the ring can be looked at — and reviewed — without hovering the pet with a
    /// key held down. The alternative was to synthesise mouse and keyboard input against a
    /// live desktop, which is a strange way to take a picture and cannot be repeated on a
    /// machine that is not being used at that moment.
    /// </remarks>
    internal void PreviewLayout(Rect petBounds, Rect workArea)
    {
        _triggerTimer.Stop();
        _requestedVisible = true;
        // Items first, then places for them: the slot count comes from how many items the
        // canvas holds, so asking for positions before building them asks for none.
        RenderItems();
        PositionAroundPet(petBounds, workArea);
        // The state they settle into, rather than the animation that gets them there. The
        // fade needs a running dispatcher, and a still picture wants the ring as it looks
        // once it has arrived — waiting for it here would mean blocking the thread that
        // would have to run it.
        UpdateAdaptiveContrast();
        foreach (var child in InfoCanvas.Children.OfType<FrameworkElement>())
        {
            child.Opacity = 1;
            if (child.RenderTransform is TranslateTransform offset) offset.Y = 0;
        }
    }

    public void UpdateItems(IReadOnlyList<NotificationBubble> items)
    {
        var next = items.Where(item => !string.IsNullOrWhiteSpace(item.Text)).Take(5).ToArray();
        var fingerprint = string.Join('\u001f', next.Select(item => $"{item.Kind}\u001e{item.Text}\u001e{item.Detail}"));
        if (string.Equals(fingerprint, _itemsFingerprint, StringComparison.Ordinal)) return;
        _itemsFingerprint = fingerprint;
        _items.Clear();
        _items.AddRange(next);
        if (!_requestedVisible) return;

        _animationCancellation?.Cancel();
        _animationCancellation?.Dispose();
        _animationCancellation = new CancellationTokenSource();
        RenderItems();
        QueueAdaptiveContrastAndAnimateIn(_animationCancellation.Token);
    }

    private void PollTrigger()
    {
        if (!TryGetPetBounds(out var petBounds, out var physicalPetBounds, out var workArea)) { SetRequestedVisible(false); return; }
        GetCursorPos(out var cursor);
        var hoveringPet = cursor.X >= physicalPetBounds.Left && cursor.X <= physicalPetBounds.Right
            && cursor.Y >= physicalPetBounds.Top && cursor.Y <= physicalPetBounds.Bottom;
        var shiftHeld = (GetAsyncKeyState(VkShift) & 0x8000) != 0;
        var shouldShow = hoveringPet && shiftHeld && _items.Count > 0;
        SetRequestedVisible(shouldShow);
        if (shouldShow) PositionAroundPet(petBounds, workArea);
    }

    private void SetRequestedVisible(bool value)
    {
        if (_requestedVisible == value) return;
        _requestedVisible = value;
        _animationCancellation?.Cancel();
        _animationCancellation?.Dispose();
        _animationCancellation = new CancellationTokenSource();
        if (value)
        {
            RenderItems();
            if (!IsVisible) Show();
            QueueAdaptiveContrastAndAnimateIn(_animationCancellation.Token);
        }
        else if (IsVisible)
        {
            _ = AnimateOutAsync(_animationCancellation.Token);
        }
    }

    private void RenderItems()
    {
        InfoCanvas.Children.Clear();
        _visuals.Clear();
        _lastScreenTargets.Clear();
        _hasPositionedItems = false;
        foreach (var item in _items) InfoCanvas.Children.Add(CreateInfoItem(item));
        if (TryGetPetBounds(out var petBounds, out _, out var workArea)) PositionAroundPet(petBounds, workArea);
    }

    private void QueueAdaptiveContrastAndAnimateIn(CancellationToken cancellation)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (cancellation.IsCancellationRequested || !_requestedVisible) return;
            UpdateAdaptiveContrast();
            _ = AnimateInAsync(cancellation);
        }), DispatcherPriority.Render);
    }

    private FrameworkElement CreateInfoItem(NotificationBubble item)
    {
        // The plate is a sibling of the text, not its parent. Nesting the text inside the
        // plate and fading the plate hides the text with it — which is what the first version
        // of this did, and why every item vanished the moment the values gathered.
        var root = new Border { Width = InfoWidth, Height = InfoHeight };
        var layers = new Grid();
        var plate = new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#66000000")!, BlurRadius = 3, ShadowDepth = 1, Opacity = 0.7 }
        };
        var content = new Grid { Margin = new Thickness(12, 5, 12, 6) };
        layers.Children.Add(plate);
        layers.Children.Add(content);
        root.Child = layers;
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // The accent as a dot beside the title rather than a rule beneath it: it marks the
        // kind without asking to be read as an underline, and it leaves the item short enough
        // to sit inside the slot the layout was built around.
        var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var primary = new TextBlock { Text = item.Text, Foreground = (Brush)Resources["InfoTextBrush"], FontSize = 13,
            FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = InfoWidth - 34 };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(dot);
        title.Children.Add(primary);
        content.Children.Add(title);

        TextBlock? detail = null;
        if (!string.IsNullOrWhiteSpace(item.Detail))
        {
            detail = new TextBlock { Text = item.Detail, Foreground = (Brush)Resources["InfoMutedBrush"], FontSize = 10.5,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = InfoWidth - 24, Margin = new Thickness(14, 3, 0, 0) };
            Grid.SetRow(detail, 1); content.Children.Add(detail);
        }
        _visuals.Add(new InfoVisual(root, plate, primary, detail, dot, item.Kind, item.Text,
            string.IsNullOrWhiteSpace(item.Short) ? item.Text : item.Short));
        return root;
    }

    private void PositionAroundPet(Rect petBounds, Rect workArea)
    {
        var workAreaChanged = !NearlyEqual(_overlayWorkArea, workArea);
        if (_hasPositionedItems
            && !workAreaChanged
            && NearlyEqual(_lastLayoutPetBounds, petBounds)
            && NearlyEqual(_lastLayoutWorkArea, workArea)) return;

        if (workAreaChanged)
        {
            _overlayWorkArea = workArea;
            Left = workArea.Left;
            Top = workArea.Top;
            Width = Math.Max(1, workArea.Width);
            Height = Math.Max(1, workArea.Height);
            _hasPositionedItems = false;
            _lastScreenTargets.Clear();
        }

        var previous = _lastScreenTargets.Count == InfoCanvas.Children.Count ? _lastScreenTargets.ToArray() : null;
        var slots = SelectOrbitSlots(petBounds, workArea, InfoCanvas.Children.Count, previous);
        if (slots.Count == 0) return;
        for (var index = 0; index < slots.Count; index++)
        {
            var element = InfoCanvas.Children[index];
            var targetX = slots[index].Left - workArea.Left;
            var targetY = slots[index].Top - workArea.Top;
            if (!_hasPositionedItems)
            {
                Canvas.SetLeft(element, targetX);
                Canvas.SetTop(element, targetY);
            }
            else if (index >= _lastScreenTargets.Count || !NearlyEqual(_lastScreenTargets[index], slots[index]))
            {
                AnimateCanvasPosition(element, targetX, targetY);
            }
        }
        _lastScreenTargets.Clear();
        _lastScreenTargets.AddRange(slots);
        _lastLayoutPetBounds = petBounds;
        _lastLayoutWorkArea = workArea;
        _hasPositionedItems = true;
        GatherIfTheOrbitIsLost(petBounds, workArea, slots);
    }

    /// <summary>
    /// Moves the items between the two arrangements, by however much the orbit has been lost.
    /// </summary>
    /// <remarks>
    /// Called on every tick rather than animated, because this is what follows a drag: the pet
    /// moves a pixel, the measure changes a little, and the items move a little with it. An
    /// animation here would still be running when the next tick arrived, and the two would
    /// fight over the same position.
    ///
    /// The items keep their text the whole way and only trade their own plates for a shared
    /// one. That is what makes it read as gathering rather than as one picture being replaced
    /// by another: every value stays legible, and the detail line — which has no room in a
    /// caption row — fades as the row closes up.
    /// </remarks>
    private void GatherIfTheOrbitIsLost(Rect petBounds, Rect workArea, IReadOnlyList<Rect> slots)
    {
        if (_visuals.Count == 0 || slots.Count < _visuals.Count) return;

        var progress = RingLayout.MergeProgress(petBounds, workArea, slots);
        _mergeProgress = progress;

        // Measured from the text, so the gathered row is as wide as what it has to say rather
        // than as wide as the slots happened to be.
        // The gathered row shows values, not sentences: a caption has no room for prose, and
        // four sentences side by side need a plate twice as wide as the pet. Each item swaps
        // its text at the halfway point of the gathering, when the sentence has stopped being
        // readable at that width anyway.
        var gathered = progress > 0.5;
        var widths = _visuals
            .Select(visual => Math.Max(52, Measure(visual, gathered) + 20))
            .ToArray();
        var plateWidth = widths.Sum() + 24 * (widths.Length - 1) + 32;
        var plate = RingLayout.MergedPlate(petBounds, workArea, plateWidth);

        _mergedPlate ??= new Border
        {
            CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1), Opacity = 0
        };
        if (!BehindCanvas.Children.Contains(_mergedPlate)) BehindCanvas.Children.Add(_mergedPlate);
        _mergedPlate.Width = plate.Width;
        _mergedPlate.Height = plate.Height;
        Canvas.SetLeft(_mergedPlate, plate.Left - workArea.Left);
        Canvas.SetTop(_mergedPlate, plate.Top - workArea.Top);
        // The plate arrives during the last part of the gathering, not from the first pixel of
        // it: a wide, half-transparent band appearing behind a pet whose values are still
        // scattered reads as a glitch rather than as something closing up.
        _mergedPlate.Opacity = Math.Clamp((progress - 0.45) / 0.55, 0, 1);
        // The shared plate takes the finish of whatever the items had, so the row does not
        // change colour as it gathers.
        _mergedPlate.Background = Brush(_mergeDark ? "#EE141C26" : "#F5FFFFFF");
        _mergedPlate.BorderBrush = Brush(_mergeDark ? "#33FFFFFF" : "#220F172A");

        for (var index = 0; index < _visuals.Count; index++)
        {
            var visual = _visuals[index];
            var slot = slots[index];
            var orbiting = new Rect(
                slot.Left + (slot.Width - InfoWidth) / 2, slot.Top + (slot.Height - InfoHeight) / 2,
                InfoWidth, InfoHeight);
            var segment = RingLayout.SegmentInPlate(plate, widths, index);
            var target = Interpolate(orbiting, segment, progress);

            // Both ways, on every tick. Going short and never coming back is what the first
            // version did, and the preview's own diagnostics caught it: the pet was dragged
            // back into open space and every item still read "42.80 CNY" instead of
            // "余额 42.80 CNY".
            var wanted = gathered ? visual.Short : visual.Full;
            if (!string.Equals(visual.Primary.Text, wanted, StringComparison.Ordinal)) visual.Primary.Text = wanted;
            visual.Root.Width = target.Width;
            Canvas.SetLeft(visual.Root, target.Left - workArea.Left);
            Canvas.SetTop(visual.Root, target.Top - workArea.Top);
            // The item's own plate dissolves into the shared one; the text does not.
            visual.Plate.Opacity = 1 - progress;
            if (visual.Detail is not null) visual.Detail.Opacity = 1 - progress;
        }

        // Said out loud when a preview asks, because this is where a picture of a settled
        // state hides its own faults: an item with no plate, or one showing its short form
        // while it is still in orbit, reads as a design decision rather than as a bug.
        if (Diagnose)
        {
            Console.WriteLine($"  诊断 进度={progress:F2} 聚拢={gathered} 板不透明度={_mergedPlate?.Opacity ?? 0:F2} 板宽={_mergedPlate?.Width ?? 0:F0}");
            foreach (var item in _visuals)
                Console.WriteLine($"    「{item.Primary.Text}」 板={item.Plate.Opacity:F2} 宽={item.Root.Width:F0} 位置=({Canvas.GetLeft(item.Root):F0},{Canvas.GetTop(item.Root):F0}) 实际文字宽={item.Primary.ActualWidth:F0}");
        }
    }

    /// <summary>Where the wave leaves from, when a preview is rendering instead of the screen.</summary>
    internal Point? PreviewWaveCentre { get; set; }

    /// <summary>
    /// Puts the entrance at a moment in its course, for a frame-by-frame capture.
    /// </summary>
    /// <remarks>
    /// Values rather than animations, sampled from the same curve the animation uses. With no
    /// visible window the animation clock barely advances, so pumping the dispatcher between
    /// captures produced fourteen frames of nothing and one of the finished state. Sampling
    /// the motion directly makes every frame a fact about the design, which is what a review
    /// needs; the live path still animates, with these same numbers.
    /// </remarks>
    /// <param name="elapsedMs">How far into the entrance, in milliseconds.</param>
    internal void PreviewEntranceAt(double elapsedMs)
    {
        var elements = InfoCanvas.Children.OfType<FrameworkElement>().ToArray();
        if (elements.Length == 0) return;

        var centre = PreviewWaveCentre ?? new Point(ActualWidth / 2, ActualHeight / 2);
        var distances = elements.Select(item => CentreDistance(item, centre)).ToArray();
        var farthest = Math.Max(1, distances.Max());

        // The wave: out to its full reach over 540 ms, fading as it goes.
        if (_wave is not null)
        {
            var eased = 1 - Math.Pow(1 - Math.Clamp(elapsedMs / 540.0, 0, 1), 3);
            if (_wave.RenderTransform is ScaleTransform transform)
            {
                transform.ScaleX = eased;
                transform.ScaleY = eased;
            }
            _wave.Opacity = 0.55 * (1 - eased);
        }

        // Printed when a preview asks. The wave not appearing in the captured frames is the
        // one thing that could not be seen from the frames themselves, and guessing at why
        // cost a round of changes that changed nothing.
        if (Diagnose)
        {
            var scale = (_wave?.RenderTransform as ScaleTransform)?.ScaleX ?? -1;
            Console.WriteLine($"  涟漪 存在={_wave is not null} 在层里={_wave is not null && BehindCanvas.Children.Contains(_wave)}"
                + $" 不透明度={_wave?.Opacity ?? -1:F2} 缩放={scale:F2} 直径={_wave?.Width ?? -1:F0}"
                + $" 描边={( _wave?.Stroke as SolidColorBrush)?.Color.ToString() ?? "无"} 层内元素={BehindCanvas.Children.Count}");
        }

        // Each item wakes when the wave reaches it, then fades and rises into place.
        for (var index = 0; index < elements.Length; index++)
        {
            var reached = Math.Clamp((elapsedMs - 340 * distances[index] / farthest) / TransitionMs, 0, 1);
            var eased = 1 - Math.Pow(1 - reached, 3);
            elements[index].Opacity = eased;
            if (elements[index].RenderTransform is TranslateTransform offset) offset.Y = 6 * (1 - eased);
        }
    }

    /// <summary>Puts the wave in place for a capture, without animating it.</summary>
    /// <remarks>
    /// The ring has to exist before a sampled frame can move it, and the animation that would
    /// normally create and grow it cannot be relied on off-screen.
    /// </remarks>
    internal void PreviewPrepareWave()
    {
        var centre = PreviewWaveCentre ?? new Point(ActualWidth / 2, ActualHeight / 2);
        var farthest = InfoCanvas.Children.OfType<FrameworkElement>()
            .Select(item => CentreDistance(item, centre))
            .DefaultIfEmpty(1)
            .Max();
        if (_wave is not null) BehindCanvas.Children.Remove(_wave);
        StartWave(centre, Math.Max(1, farthest));
        // Thicker for the capture only, to tell "not painted at all" apart from "too thin to
        // be seen at this size". The live ring keeps its two pixels.
        if (_wave is not null) _wave.StrokeThickness = 8;
        if (_wave is not null)
        {
            _wave.BeginAnimation(OpacityProperty, null);
            if (_wave.RenderTransform is ScaleTransform transform)
            {
                transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }
        }
    }

    /// <summary>
    /// Starts the entrance for a frame-by-frame capture.
    /// </summary>
    /// <remarks>
    /// The items are hidden first because a preview lays them out in their settled state, and
    /// capturing that would photograph the end of the animation over and over. Any animation
    /// already on them is cleared, so the fade starts from zero rather than from wherever the
    /// last capture left it.
    /// </remarks>
    internal void PreviewStartEntrance()
    {
        foreach (var item in InfoCanvas.Children.OfType<FrameworkElement>())
        {
            item.BeginAnimation(OpacityProperty, null);
            item.Opacity = 0;
        }
        _ = AnimateInAsync(CancellationToken.None);
    }

    /// <summary>Recolours the items once they have been laid out. Used by the preview.</summary>
    /// <remarks>
    /// The colours come from measuring each item against what is behind it, so they can only
    /// be chosen after the items have a size — and a preview that colours them before its
    /// layout pass renders them with no plates at all, which reads as a missing design.
    /// </remarks>
    internal void RefreshAdaptiveContrast() => UpdateAdaptiveContrast();

    /// <summary>Prints what the gathering decided. Set by the preview renderer.</summary>
    internal static bool Diagnose { get; set; }

    /// <summary>
    /// How wide an item wants to be. Measured when the layout has run, estimated from the
    /// character count when it has not — the previews lay items out before anything has been
    /// arranged, and a width of zero there would collapse the whole row.
    /// </summary>
    private static double Measure(InfoVisual visual, bool gathered)
    {
        var text = gathered ? visual.Short : visual.Full;
        return visual.Primary.ActualWidth > 1 && !gathered
            ? visual.Primary.ActualWidth
            : text.Sum(character => character > 0x2E80 ? 13.5 : 7.2) + 14;
    }

    private static Rect Interpolate(Rect from, Rect to, double amount) => new(
        from.Left + (to.Left - from.Left) * amount,
        from.Top + (to.Top - from.Top) * amount,
        from.Width + (to.Width - from.Width) * amount,
        from.Height + (to.Height - from.Height) * amount);

    /// <summary>
    /// Where the items go around the pet: a ring in open space, a fan towards the screen's
    /// middle when the pet is near an edge. Internal so the sketch renderer can lay items
    /// out with the same algorithm the real ring uses, which is what makes a sketch an
    /// answer about this design rather than about a drawing of it.
    /// </summary>
    internal static IReadOnlyList<Rect> SelectOrbitSlots(Rect pet, Rect workArea, int count, IReadOnlyList<Rect>? previous)
    {
        if (count <= 0) return [];
        var petCenterX = pet.Left + pet.Width / 2;
        var petCenterY = pet.Top + pet.Height / 2;
        var protectedPet = pet;
        protectedPet.Inflate(PetGap, PetGap);

        var cornerFan = TrySelectCornerFan(pet, workArea, protectedPet, count);
        if (cornerFan is not null) return cornerFan;

        // Pick all visible positions from one orbital field. At screen edges the
        // full ring becomes a fan, but its points still follow angular targets
        // around the pet instead of collapsing into horizontal or vertical rows.
        var clearanceX = pet.Width / 2 + InfoWidth / 2 + PetGap;
        var clearanceY = pet.Height / 2 + InfoHeight / 2 + PetGap;
        var candidates = new List<OrbitCandidate>();
        foreach (var scale in new[] { 1d, 1.08d, 1.16d, 1.26d, 1.38d, 1.52d, 1.68d, 1.86d })
        {
            for (var degrees = 0; degrees < 360; degrees += 4)
            {
                var radians = degrees * Math.PI / 180d;
                var cosine = Math.Cos(radians);
                var sine = Math.Sin(radians);
                var horizontalDistance = clearanceX / Math.Max(0.0001, Math.Abs(cosine));
                var verticalDistance = clearanceY / Math.Max(0.0001, Math.Abs(sine));
                var distance = Math.Min(horizontalDistance, verticalDistance) * scale;
                var candidate = new Rect(
                    petCenterX + distance * cosine - InfoWidth / 2,
                    petCenterY + distance * sine - InfoHeight / 2,
                    InfoWidth,
                    InfoHeight);
                if (FitsInWorkArea(candidate, workArea) && !candidate.IntersectsWith(protectedPet))
                    candidates.Add(new OrbitCandidate(candidate, degrees, scale));
            }
        }

        var targets = BuildOrbitTargets(candidates, count,
            previous is null ? 18d : EstimateRotation(petCenterX, petCenterY, previous));
        if (targets.Count == count)
        {
            var states = new List<OrbitState> { new(new Rect[count], 0) };
            for (var itemIndex = 0; itemIndex < count && states.Count > 0; itemIndex++)
            {
                var index = itemIndex;
                var ranked = candidates
                    .Select(candidate => new
                    {
                        Candidate = candidate,
                        Score = AngularDistance(candidate.Angle, targets[index]) * 120
                            + Math.Abs(candidate.Scale - 1) * 560
                            + (previous is not null && index < previous.Count && !previous[index].IsEmpty
                                ? CenterDistance(candidate.Rect, previous[index]) * 0.65
                                : 0)
                    })
                    .OrderBy(entry => entry.Score)
                    .Take(180)
                    .ToArray();
                var next = new List<OrbitState>();
                foreach (var state in states)
                {
                    var accepted = 0;
                    foreach (var entry in ranked)
                    {
                        if (state.Slots.Take(index).Any(existing => Overlaps(existing, entry.Candidate.Rect))) continue;
                        var slots = (Rect[])state.Slots.Clone();
                        slots[index] = entry.Candidate.Rect;
                        next.Add(new OrbitState(slots, state.Score + entry.Score));
                        if (++accepted == 45) break;
                    }
                }
                states = next.OrderBy(state => state.Score).Take(400).ToList();
            }
            if (states.Count > 0) return states[0].Slots;
        }

        // Safety fallback for unusually small work areas: retain a compact grid,
        // but only after the free-angle orbit cannot provide enough positions.
        var selected = new List<Rect>(count);
        if (selected.Count < count)
        {
            var horizontalStep = InfoWidth + SlotGap;
            var verticalStep = InfoHeight + SlotGap;
            var fallback = new List<(Rect Rect, double Distance)>();
            for (var row = -4; row <= 4; row++)
            for (var column = -3; column <= 3; column++)
            {
                var candidate = new Rect(
                    pet.Left + pet.Width / 2 - InfoWidth / 2 + column * horizontalStep,
                    pet.Top + pet.Height / 2 - InfoHeight / 2 + row * verticalStep,
                    InfoWidth,
                    InfoHeight);
                if (candidate.IntersectsWith(protectedPet) || !FitsInWorkArea(candidate, workArea)) continue;
                var dx = candidate.Left + candidate.Width / 2 - (pet.Left + pet.Width / 2);
                var dy = candidate.Top + candidate.Height / 2 - (pet.Top + pet.Height / 2);
                fallback.Add((candidate, dx * dx + dy * dy));
            }
            foreach (var entry in fallback.OrderBy(value => value.Distance))
            {
                if (selected.Any(existing => Overlaps(existing, entry.Rect))) continue;
                selected.Add(entry.Rect);
                if (selected.Count == count) break;
            }
        }
        return selected;
    }

    private static IReadOnlyList<Rect>? TrySelectCornerFan(Rect pet, Rect workArea, Rect protectedPet, int count)
    {
        const double edgeThreshold = 32;
        var nearLeft = pet.Left - workArea.Left <= edgeThreshold;
        var nearRight = workArea.Right - pet.Right <= edgeThreshold;
        var nearTop = pet.Top - workArea.Top <= edgeThreshold;
        var nearBottom = workArea.Bottom - pet.Bottom <= edgeThreshold;
        if (!(nearLeft || nearRight) || !(nearTop || nearBottom)) return null;

        var (startAngle, endAngle) = (nearRight, nearBottom) switch
        {
            (true, true) => (240d, 160d),
            (true, false) => (120d, 200d),
            (false, true) => (300d, 380d),
            _ => (60d, -20d)
        };
        var centerX = pet.Left + pet.Width / 2;
        var centerY = pet.Top + pet.Height / 2;
        var radiusX = pet.Width / 2 + InfoWidth / 2 + 56;
        var radiusY = pet.Height / 2 + InfoHeight / 2 + 67;
        var slots = new List<Rect>(count);
        for (var index = 0; index < count; index++)
        {
            var progress = count == 1 ? .5 : index / (count - 1d);
            var radians = (startAngle + (endAngle - startAngle) * progress) * Math.PI / 180d;
            var candidate = new Rect(
                centerX + radiusX * Math.Cos(radians) - InfoWidth / 2,
                centerY + radiusY * Math.Sin(radians) - InfoHeight / 2,
                InfoWidth,
                InfoHeight);
            if (!FitsInWorkArea(candidate, workArea)
                || candidate.IntersectsWith(protectedPet)
                || slots.Any(existing => Overlaps(existing, candidate))) return null;
            slots.Add(candidate);
        }
        return slots;
    }

    private static IReadOnlyList<double> BuildOrbitTargets(IReadOnlyList<OrbitCandidate> candidates, int count, double preferredRotation)
    {
        var angles = candidates.Select(candidate => candidate.Angle).Distinct().OrderBy(angle => angle).ToArray();
        if (angles.Length == 0) return [];
        var largestGap = -1d;
        var arcStart = angles[0];
        for (var index = 0; index < angles.Length; index++)
        {
            var nextIndex = (index + 1) % angles.Length;
            var gap = (angles[nextIndex] - angles[index] + 360) % 360;
            if (gap <= largestGap) continue;
            largestGap = gap;
            arcStart = angles[nextIndex];
        }

        if (largestGap <= 8)
            return Enumerable.Range(0, count).Select(index => NormalizeAngle(preferredRotation + index * 360d / count)).ToArray();

        var span = 360 - largestGap;
        var inset = Math.Min(8, span / Math.Max(2, count * 2));
        if (count == 1) return [NormalizeAngle(arcStart + span / 2)];
        return Enumerable.Range(0, count)
            .Select(index => NormalizeAngle(arcStart + inset + (span - inset * 2) * index / (count - 1d)))
            .ToArray();
    }

    private static double EstimateRotation(double centerX, double centerY, IReadOnlyList<Rect> previous)
    {
        if (previous.Count == 0 || previous[0].IsEmpty) return 18;
        var radians = Math.Atan2(
            previous[0].Top + previous[0].Height / 2 - centerY,
            previous[0].Left + previous[0].Width / 2 - centerX);
        var firstAngle = radians * 180d / Math.PI;
        if (firstAngle < 0) firstAngle += 360;
        return firstAngle;
    }

    private static double NormalizeAngle(double angle) => (angle % 360 + 360) % 360;

    private static void AnimateCanvasPosition(UIElement element, double targetX, double targetY)
    {
        var currentX = Canvas.GetLeft(element);
        var currentY = Canvas.GetTop(element);
        if (double.IsNaN(currentX)) currentX = targetX;
        if (double.IsNaN(currentY)) currentY = targetY;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        Canvas.SetLeft(element, targetX);
        Canvas.SetTop(element, targetY);
        element.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(currentX, targetX, TimeSpan.FromMilliseconds(LayoutTransitionMs))
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        }, HandoffBehavior.SnapshotAndReplace);
        element.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(currentY, targetY, TimeSpan.FromMilliseconds(LayoutTransitionMs))
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private static double AngularDistance(double first, double second)
    {
        var distance = Math.Abs(first - second) % 360;
        return Math.Min(distance, 360 - distance);
    }

    private static double CenterDistance(Rect first, Rect second)
    {
        var dx = first.Left + first.Width / 2 - (second.Left + second.Width / 2);
        var dy = first.Top + first.Height / 2 - (second.Top + second.Height / 2);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool NearlyEqual(Rect first, Rect second)
        => Math.Abs(first.Left - second.Left) < 0.5
            && Math.Abs(first.Top - second.Top) < 0.5
            && Math.Abs(first.Width - second.Width) < 0.5
            && Math.Abs(first.Height - second.Height) < 0.5;

    private static bool FitsInWorkArea(Rect candidate, Rect workArea)
        => candidate.Left >= workArea.Left + OverlayPadding
            && candidate.Top >= workArea.Top + OverlayPadding
            && candidate.Right <= workArea.Right - OverlayPadding
            && candidate.Bottom <= workArea.Bottom - OverlayPadding;

    private static bool Overlaps(Rect first, Rect second)
    {
        first.Inflate(SlotGap / 2, SlotGap / 2);
        return first.IntersectsWith(second);
    }

    /// <summary>
    /// Brings the items in behind a wave that leaves the pet.
    /// </summary>
    /// <remarks>
    /// Each item wakes when the wave reaches it rather than on a fixed schedule, so the order
    /// is the order of the scene rather than the order of a list — and the ring says that what
    /// appeared came from the pet, which a fade cannot say. A fixed stagger looked almost the
    /// same in a still and quite different in motion: it reads as four things taking turns,
    /// where this reads as one thing spreading.
    /// </remarks>
    private async Task AnimateInAsync(CancellationToken cancellation)
    {
        try
        {
            var centre = new Point(ActualWidth / 2, ActualHeight / 2);
            if (TryGetPetBounds(out var petBounds, out _, out var workArea))
                centre = new Point(
                    petBounds.Left + petBounds.Width / 2 - workArea.Left,
                    petBounds.Top + petBounds.Height / 2 - workArea.Top);

            var elements = InfoCanvas.Children.OfType<FrameworkElement>().ToArray();
            var distances = elements.Select(item => CentreDistance(item, centre)).ToArray();
            var farthest = distances.Length == 0 ? 1 : Math.Max(1, distances.Max());
            StartWave(centre, farthest);

            for (var index = 0; index < elements.Length; index++)
            {
                var delay = (int)Math.Round(340 * distances[index] / farthest);
                Animate(elements[index], 0, 1, 6, 0, EasingMode.EaseOut, delay);
            }
            await Task.Delay(560, cancellation);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>One ring leaving the pet, fading as it goes.</summary>
    private void StartWave(Point centre, double farthest)
    {
        var diameter = (farthest + 70) * 2;
        _wave = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            StrokeThickness = 2,
            Stroke = AccentFor("balance"),
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0, 0)
        };
        Canvas.SetLeft(_wave, centre.X - diameter / 2);
        Canvas.SetTop(_wave, centre.Y - diameter / 2);
        BehindCanvas.Children.Add(_wave);

        var grow = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(540)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var fade = new DoubleAnimation(0.55, 0, TimeSpan.FromMilliseconds(540)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (_wave.RenderTransform is ScaleTransform transform)
        {
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
        _wave.BeginAnimation(OpacityProperty, fade);
    }

    private static double CentreDistance(FrameworkElement element, Point centre)
        => Math.Sqrt(
            Math.Pow(Canvas.GetLeft(element) + element.ActualWidth / 2 - centre.X, 2)
            + Math.Pow(Canvas.GetTop(element) + element.ActualHeight / 2 - centre.Y, 2));

    private async Task AnimateOutAsync(CancellationToken cancellation)
    {
        try { foreach (FrameworkElement item in InfoCanvas.Children) { Animate(item, item.Opacity, 0, 0, -6, EasingMode.EaseIn); await Task.Delay(StaggerMs, cancellation); }
            await Task.Delay(TransitionMs, cancellation);
            if (!_requestedVisible)
            {
                Hide();
                if (_wave is not null) { BehindCanvas.Children.Remove(_wave); _wave = null; }
                if (_mergedPlate is not null) _mergedPlate.Opacity = 0;
            } }
        catch (OperationCanceledException) { }
    }

    private static void Animate(FrameworkElement item, double fromOpacity, double toOpacity, double fromY, double toY, EasingMode mode, int delayMs = 0)
    {
        var easing = new CubicEase { EasingMode = mode };
        var begin = TimeSpan.FromMilliseconds(delayMs);
        item.BeginAnimation(OpacityProperty, new DoubleAnimation(fromOpacity, toOpacity, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = easing, BeginTime = begin });
        if (item.RenderTransform is TranslateTransform translate) translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(fromY, toY, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = easing, BeginTime = begin });
    }

    private Brush AccentFor(string kind) => kind switch
    {
        "balance" => (Brush)Resources["BalanceBrush"], "task" => (Brush)Resources["TaskBrush"], "account" => (Brush)Resources["AccountBrush"],
        "system" => (Brush)Resources["SystemBrush"], _ => (Brush)Resources["RefreshBrush"]
    };

    /// <summary>
    /// Measures the backdrop behind an item, in the ordinary case by reading the screen.
    /// </summary>
    /// <remarks>
    /// A preview sets this, because the picture it is composing is not on the screen yet:
    /// reading the screen there would measure whatever window happens to be open behind the
    /// review, and would choose the text colour for the wrong backdrop — which is a picture
    /// of a ring nobody will ever see.
    /// </remarks>
    internal Func<FrameworkElement, double>? BackdropLuminance { get; set; }

    private void UpdateAdaptiveContrast()
    {
        if (_visuals.Count == 0) return;
        var sampler = BackdropLuminance;
        if (sampler is null && !IsVisible) return;
        var screen = sampler is null ? GetDC(IntPtr.Zero) : IntPtr.Zero;
        if (sampler is null && screen == IntPtr.Zero) return;
        try
        {
            foreach (var visual in _visuals)
            {
                // Visibility is only required when the colour comes from the screen: a preview
                // window is never shown, and skipping these left every plate without a
                // background — which looks like a missing design rather than a missing brush.
                if (sampler is null && !visual.Root.IsVisible) continue;
                if (visual.Root.ActualWidth <= 0 || visual.Root.ActualHeight <= 0) continue;
                var luminance = sampler is not null ? sampler(visual.Root) : SampleBackgroundLuminance(screen, visual.Root);
                if (luminance < 0) continue;
                var useLightText = ContrastRatio(0.955, luminance) >= ContrastRatio(0.014, luminance);
                visual.Primary.Foreground = Brush(useLightText ? "#F7FAFF" : "#111827");
                if (visual.Detail is not null)
                    visual.Detail.Foreground = Brush(useLightText ? "#DCE6F5" : "#334155");
                visual.Accent.Background = AdaptiveAccent(visual.Kind, useLightText);
                // The plate, chosen from the same measurement as the text: a surface that
                // carries its own contrast cannot be defeated by whatever is behind it.
                // While the items are gathered, the shared plate takes its finish from the
                // same measurement, so the row does not change colour as it closes up.
                if (_mergeProgress > 0.5) _mergeDark = useLightText;
                visual.Plate.Background = Brush(useLightText ? "#E6141C26" : "#F2FFFFFF");
                visual.Plate.BorderBrush = Brush(useLightText ? "#33FFFFFF" : "#220F172A");
                visual.Plate.Effect = new DropShadowEffect
                {
                    Color = (Color)ColorConverter.ConvertFromString(useLightText ? "#E6000000" : "#CCFFFFFF")!,
                    BlurRadius = 2.2,
                    ShadowDepth = 0,
                    Opacity = 0.95
                };
            }
        }
        finally { if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen); }
    }

    private static double SampleBackgroundLuminance(IntPtr screen, FrameworkElement element)
    {
        var origin = element.PointToScreen(new Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(element);
        var width = Math.Max(1, element.ActualWidth * dpi.DpiScaleX);
        var height = Math.Max(1, element.ActualHeight * dpi.DpiScaleY);
        var samples = new List<double>(15);
        foreach (var xFactor in new[] { 0.04, 0.27, 0.50, 0.73, 0.96 })
        foreach (var yFactor in new[] { 0.14, 0.50, 0.82 })
        {
            var colorRef = GetPixel(screen, (int)Math.Round(origin.X + width * xFactor), (int)Math.Round(origin.Y + height * yFactor));
            if (colorRef == uint.MaxValue) continue;
            var red = colorRef & 0xFF;
            var green = (colorRef >> 8) & 0xFF;
            var blue = (colorRef >> 16) & 0xFF;
            samples.Add(RelativeLuminance(red / 255d, green / 255d, blue / 255d));
        }
        if (samples.Count == 0) return -1;
        samples.Sort();
        return samples[samples.Count / 2];
    }

    private static double RelativeLuminance(double red, double green, double blue)
    {
        static double Linear(double value) => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        return 0.2126 * Linear(red) + 0.7152 * Linear(green) + 0.0722 * Linear(blue);
    }

    private static double ContrastRatio(double foreground, double background)
        => (Math.Max(foreground, background) + 0.05) / (Math.Min(foreground, background) + 0.05);

    private static Brush AdaptiveAccent(string kind, bool bright) => Brush((kind, bright) switch
    {
        ("balance", true) => "#48F2D7", ("balance", false) => "#00796F",
        ("task", true) => "#8DB7FF", ("task", false) => "#294784",
        ("account", true) => "#D0A7FF", ("account", false) => "#7545AD",
        ("system", true) => "#FFD166", ("system", false) => "#9A5B12",
        (_, true) => "#D7E0EF", _ => "#52627A"
    });

    private void ApplyTheme()
    {
        var isLight = true;
        try { using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); isLight = (key?.GetValue("AppsUseLightTheme") as int? ?? 1) != 0; }
        catch (Exception) { }
        Resources["InfoTextBrush"] = Brush(isLight ? "#17233A" : "#F5F8FE"); Resources["InfoMutedBrush"] = Brush(isLight ? "#52637F" : "#C4D0E3");
        Resources["BalanceBrush"] = Brush(isLight ? "#078C82" : "#2DE1C2"); Resources["TaskBrush"] = Brush(isLight ? "#344F91" : "#78A9FF");
        Resources["AccountBrush"] = Brush(isLight ? "#8A5CC7" : "#B586FF"); Resources["SystemBrush"] = Brush(isLight ? "#C47A28" : "#F4B942");
        Resources["RefreshBrush"] = Brush(isLight ? "#66758E" : "#93A3BA");
    }

    private bool TryGetPetBounds(out Rect logicalBounds, out NativeRect physicalBounds, out Rect workArea)
    {
        logicalBounds = Rect.Empty; physicalBounds = default; workArea = Rect.Empty; var handle = FindWindow(null, "BalancePet");
        if (handle == IntPtr.Zero || !IsWindowVisible(handle) || !GetWindowRect(handle, out var windowRect)) return false;
        var dpi = GetDpiForWindow(handle); var scale = dpi > 0 ? dpi / 96d : 1d;
        var placement = ReadPetPlacement();
        var logicalPetSize = PetSurfaceSize * placement.Scale;
        var petPixels = (int)Math.Round(logicalPetSize * scale);
        physicalBounds = placement.Flipped
            ? new NativeRect { Left = windowRect.Left, Top = windowRect.Bottom - petPixels, Right = windowRect.Left + petPixels, Bottom = windowRect.Bottom }
            : new NativeRect { Left = windowRect.Right - petPixels, Top = windowRect.Bottom - petPixels, Right = windowRect.Right, Bottom = windowRect.Bottom };
        logicalBounds = new Rect(physicalBounds.Left / scale, physicalBounds.Top / scale, logicalPetSize, logicalPetSize);
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo))
        {
            workArea = new Rect(
                monitorInfo.Work.Left / scale,
                monitorInfo.Work.Top / scale,
                (monitorInfo.Work.Right - monitorInfo.Work.Left) / scale,
                (monitorInfo.Work.Bottom - monitorInfo.Work.Top) / scale);
        }
        else
        {
            workArea = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        }
        return true;
    }

    private PetPlacement ReadPetPlacement()
    {
        try
        {
            _coreSettingsPath ??= ResolveCoreSettingsPath();
            if (!File.Exists(_coreSettingsPath)) return _cachedPetPlacement;
            var writeUtc = File.GetLastWriteTimeUtc(_coreSettingsPath);
            if (writeUtc == _coreSettingsWriteUtc) return _cachedPetPlacement;
            using var document = JsonDocument.Parse(File.ReadAllText(_coreSettingsPath));
            var root = document.RootElement;
            var flipped = root.TryGetProperty("flipped", out var flippedValue) && flippedValue.ValueKind == JsonValueKind.True;
            var petScale = root.TryGetProperty("pet_scale", out var scaleValue) && scaleValue.TryGetDouble(out var configuredScale)
                ? Math.Clamp(configuredScale, 0.6, 1.4)
                : 1;
            _cachedPetPlacement = new PetPlacement(flipped, petScale);
            _coreSettingsWriteUtc = writeUtc;
            return _cachedPetPlacement;
        }
        catch (IOException) { return _cachedPetPlacement; }
        catch (JsonException) { return _cachedPetPlacement; }
        catch (UnauthorizedAccessException) { return _cachedPetPlacement; }
    }

    private static string ResolveCoreSettingsPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("BALANCEPET_CSHARP_CONFIG");
        return string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BalancePet", "csharp-settings.json")
            : configuredPath;
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color)!);
    private void EnableMousePassthrough() { var handle = new WindowInteropHelper(this).Handle; var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64(); SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExTransparent | WsExToolWindow | WsExNoActivate)); }
    protected override void OnClosed(EventArgs e) { _animationCancellation?.Cancel(); _animationCancellation?.Dispose(); _triggerTimer.Stop(); base.OnClosed(e); }

    private sealed record InfoVisual(Border Root, Border Plate, TextBlock Primary, TextBlock? Detail, Border Accent, string Kind, string Full, string Short);
    private sealed record OrbitCandidate(Rect Rect, double Angle, double Scale);
    private sealed record OrbitState(Rect[] Slots, double Score);
    private sealed record PetPlacement(bool Flipped, double Scale);

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string windowName);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr deviceContext, int x, int y);
    private const uint MonitorDefaultToNearest = 2;
    private const int GwlExStyle = -20; private const long WsExTransparent = 0x00000020L; private const long WsExToolWindow = 0x00000080L; private const long WsExNoActivate = 0x08000000L;
    private static IntPtr GetWindowLongPtr(IntPtr window, int index) => IntPtr.Size == 8 ? GetWindowLongPtr64(window, index) : new IntPtr(GetWindowLong32(window, index));
    private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(window, index, value) : new IntPtr(SetWindowLong32(window, index, value.ToInt32()));
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong32(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(IntPtr window, int index, int value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
}
