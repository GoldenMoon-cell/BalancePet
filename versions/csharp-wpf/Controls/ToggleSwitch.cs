using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

// The project enables both WPF and Windows Forms, so these two names exist twice. Aliasing
// once is clearer than qualifying every use.
using CheckBox = System.Windows.Controls.CheckBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace BalancePet.Wpf.Controls;

/// <summary>
/// A switch that can be dragged as well as clicked, crossfading between its two states.
/// </summary>
/// <remarks>
/// A CheckBox with a switch template still behaves like a CheckBox: the whole control is one
/// hit target that flips on release, and the thumb only ever teleports between two places.
/// That reads as a picture of a switch rather than as a switch. This adds the part people
/// actually try -- press the thumb, push it, let go -- and lets the colour follow the thumb
/// instead of arriving at the end.
///
/// <see cref="Progress"/> is the single value everything else is derived from: 0 is off, 1 is
/// on, and everything between is a thumb in flight. Dragging writes it directly, a click
/// animates it, and the template binds the thumb's offset and the track's fill to it, so
/// there is no second place where "how far along" is stored and no way for the thumb and the
/// colour to disagree.
/// </remarks>
public class ToggleSwitch : CheckBox
{
    /// <summary>How far the thumb travels, in pixels. Must match the template's geometry.</summary>
    public const double Travel = 20;

    /// <summary>Past this fraction of the travel, releasing settles on "on".</summary>
    private const double CommitThreshold = 0.5;

    /// <summary>A drag shorter than this is a click, and toggles rather than settling.</summary>
    private const double DragThreshold = 3;

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(ToggleSwitch),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnProgressChanged));

    /// <summary>Thumb offset in pixels, kept in step with <see cref="Progress"/>.</summary>
    public static readonly DependencyProperty ThumbOffsetProperty = DependencyProperty.Register(
        nameof(ThumbOffset), typeof(double), typeof(ToggleSwitch), new PropertyMetadata(0d));

    /// <summary>The inverse of <see cref="Progress"/>, so the template needs no converter.</summary>
    public static readonly DependencyProperty OffOpacityProperty = DependencyProperty.Register(
        nameof(OffOpacity), typeof(double), typeof(ToggleSwitch), new PropertyMetadata(1d));

    private bool _dragging;
    private bool _moved;
    private double _pressX;
    private double _pressProgress;

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public double ThumbOffset
    {
        get => (double)GetValue(ThumbOffsetProperty);
        private set => SetValue(ThumbOffsetProperty, value);
    }

    public double OffOpacity
    {
        get => (double)GetValue(OffOpacityProperty);
        private set => SetValue(OffOpacityProperty, value);
    }

    public ToggleSwitch()
    {
        // The default CheckBox keyboard and accessibility behaviour is what makes this
        // usable without a mouse, so none of it is replaced.
        IsChecked = false;
        Sync();
    }

    private static void OnProgressChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle) return;
        var progress = (double)e.NewValue;
        toggle.ThumbOffset = progress * Travel;
        toggle.OffOpacity = 1 - progress;
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        if (!_dragging) Animate(1);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        if (!_dragging) Animate(0);
    }

    private void Sync()
    {
        BeginAnimation(ProgressProperty, null);
        Progress = IsChecked == true ? 1 : 0;
    }

    private void Animate(double target)
    {
        var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        BeginAnimation(ProgressProperty, animation);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (!IsEnabled) return;
        _dragging = true;
        _moved = false;
        _pressX = e.GetPosition(this).X;
        _pressProgress = Progress;
        CaptureMouse();
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;

        var delta = e.GetPosition(this).X - _pressX;
        if (!_moved && Math.Abs(delta) < DragThreshold) return;
        _moved = true;

        // Written straight to the property rather than animated: the pointer is the clock.
        BeginAnimation(ProgressProperty, null);
        Progress = Math.Clamp(_pressProgress + delta / Travel, 0, 1);
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();

        if (!_moved)
        {
            // A press that never moved is a click. Let the base class flip it, which routes
            // through OnChecked/OnUnchecked and therefore animates.
            Sync();
            IsChecked = IsChecked != true;
            e.Handled = true;
            return;
        }

        // Settle to whichever end the thumb is nearer, then let that animate the rest of
        // the way. The value is set first and the animation follows, so a slow drag cannot
        // leave the switch agreeing with the pointer instead of with the state.
        var settled = Progress >= CommitThreshold;
        Progress = settled ? 1 : 0;
        if (IsChecked == settled) return;
        IsChecked = settled;
        e.Handled = true;
    }
}
