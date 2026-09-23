using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace BalancePet.NotificationCenter;

public sealed record NotificationBubble(string Text, string Detail, string Kind);

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

    public BubbleWindow()
    {
        InitializeComponent();
        ApplyTheme();
        SourceInitialized += (_, _) => EnableMousePassthrough();
        _triggerTimer.Tick += (_, _) => PollTrigger();
        _triggerTimer.Start();
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
        var root = new Grid { Width = InfoWidth, Height = InfoHeight, Opacity = 0, RenderTransform = new TranslateTransform(0, 8),
            Effect = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#66000000")!, BlurRadius = 3, ShadowDepth = 1, Opacity = 0.7 } };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var primary = new TextBlock { Text = item.Text, Foreground = (Brush)Resources["InfoTextBrush"], FontSize = 13,
            FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = InfoWidth };
        root.Children.Add(primary);
        TextBlock? detail = null;
        if (!string.IsNullOrWhiteSpace(item.Detail))
        {
            detail = new TextBlock { Text = item.Detail, Foreground = (Brush)Resources["InfoMutedBrush"], FontSize = 10.5,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = InfoWidth, Margin = new Thickness(0, 3, 0, 0) };
            Grid.SetRow(detail, 1); root.Children.Add(detail);
        }
        var underline = new Border { Height = 2, Width = 146, CornerRadius = new CornerRadius(1), Background = AccentFor(item.Kind),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetRow(underline, 2); root.Children.Add(underline);
        _visuals.Add(new InfoVisual(root, primary, detail, underline, item.Kind));
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
    }

    private static IReadOnlyList<Rect> SelectOrbitSlots(Rect pet, Rect workArea, int count, IReadOnlyList<Rect>? previous)
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

    private async Task AnimateInAsync(CancellationToken cancellation)
    {
        try { foreach (FrameworkElement item in InfoCanvas.Children) { Animate(item, 0, 1, 8, 0, EasingMode.EaseOut); await Task.Delay(StaggerMs, cancellation); } }
        catch (OperationCanceledException) { }
    }

    private async Task AnimateOutAsync(CancellationToken cancellation)
    {
        try { foreach (FrameworkElement item in InfoCanvas.Children) { Animate(item, item.Opacity, 0, 0, -6, EasingMode.EaseIn); await Task.Delay(StaggerMs, cancellation); }
            await Task.Delay(TransitionMs, cancellation); if (!_requestedVisible) Hide(); }
        catch (OperationCanceledException) { }
    }

    private static void Animate(FrameworkElement item, double fromOpacity, double toOpacity, double fromY, double toY, EasingMode mode)
    {
        var easing = new CubicEase { EasingMode = mode };
        item.BeginAnimation(OpacityProperty, new DoubleAnimation(fromOpacity, toOpacity, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = easing });
        if (item.RenderTransform is TranslateTransform translate) translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(fromY, toY, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = easing });
    }

    private Brush AccentFor(string kind) => kind switch
    {
        "balance" => (Brush)Resources["BalanceBrush"], "task" => (Brush)Resources["TaskBrush"], "account" => (Brush)Resources["AccountBrush"],
        "system" => (Brush)Resources["SystemBrush"], _ => (Brush)Resources["RefreshBrush"]
    };

    private void UpdateAdaptiveContrast()
    {
        if (!IsVisible || _visuals.Count == 0) return;
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return;
        try
        {
            foreach (var visual in _visuals)
            {
                if (!visual.Root.IsVisible || visual.Root.ActualWidth <= 0 || visual.Root.ActualHeight <= 0) continue;
                var luminance = SampleBackgroundLuminance(screen, visual.Root);
                if (luminance < 0) continue;
                var useLightText = ContrastRatio(0.955, luminance) >= ContrastRatio(0.014, luminance);
                visual.Primary.Foreground = Brush(useLightText ? "#F7FAFF" : "#111827");
                if (visual.Detail is not null)
                    visual.Detail.Foreground = Brush(useLightText ? "#DCE6F5" : "#334155");
                visual.Underline.Background = AdaptiveAccent(visual.Kind, useLightText);
                visual.Root.Effect = new DropShadowEffect
                {
                    Color = (Color)ColorConverter.ConvertFromString(useLightText ? "#E6000000" : "#CCFFFFFF")!,
                    BlurRadius = 2.2,
                    ShadowDepth = 0,
                    Opacity = 0.95
                };
            }
        }
        finally { ReleaseDC(IntPtr.Zero, screen); }
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

    private sealed record InfoVisual(Grid Root, TextBlock Primary, TextBlock? Detail, Border Underline, string Kind);
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
