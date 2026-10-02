using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Writes what the interface is actually doing to a file, so a fault that only appears while
/// someone is using the window can be read afterwards instead of guessed at.
/// </summary>
/// <remarks>
/// This exists because three faults were chased for several rounds by inferring a mechanism
/// from a description, changing code to match, and asking for another install. None of the
/// inferences held. A description of a symptom is not evidence of a cause, and this is the
/// cheapest way to get evidence from a machine that cannot be reproduced on.
///
/// The log is append-only and plain text, and its path is printed at startup so it can be
/// found without knowing where it lives.
/// </remarks>
public static class Diagnostics
{
    private static readonly object Gate = new();

    /// <summary>
    /// Where the log goes. An environment variable can redirect it, in the same way the
    /// settings file can be redirected, which is what makes the log checkable from a test
    /// harness: a process started from a sandboxed tree inherits a low integrity label and is
    /// refused when it writes to a medium integrity location.
    /// </summary>
    public static string FilePath { get; } =
        Environment.GetEnvironmentVariable("BALANCEPET_DIAGNOSTICS_LOG") is { Length: > 0 } redirected
            ? redirected
            : System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BalancePet",
                "diagnostics.log");

    /// <summary>Whether anything has actually reached the file. Reported, not assumed.</summary>
    public static string? LastError { get; private set; }

    public static void Write(string area, string message)
    {
        try
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff}  [{area}]  {message}{Environment.NewLine}";
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
                System.IO.File.AppendAllText(FilePath, line, Encoding.UTF8);
                LastError = null;
            }
        }
        catch (Exception error)
        {
            // Kept rather than swallowed: a diagnostic that cannot write must say so, or the
            // absence of a log is indistinguishable from the absence of a fault.
            LastError = $"{FilePath}: {error.GetType().Name} {error.Message}";
        }
    }

    public static void Banner(string version)
    {
        Write("start", new string('=', 70));
        Write("start", $"BalancePet {version} 诊断版本，日志写到 {FilePath}");
        Write("start", $"机器 {Environment.MachineName}  系统 {Environment.OSVersion}");
    }

    // ---- the three reported faults ------------------------------------------------

    /// <summary>
    /// Everything that decides what a combo box shows when it is closed.
    /// </summary>
    /// <remarks>
    /// The box went blank after a face was picked and came back when the window was reopened.
    /// That narrows it to whatever the template resolves at that moment, so all of it is
    /// recorded: the item, the item the template is offered, the text actually on screen, and
    /// whether a binding is still alive on that text or something replaced it with a literal.
    /// </remarks>
    public static void Combo(string label, System.Windows.Controls.ComboBox? box)
    {
        if (box is null) { Write("combo", $"{label}: 不存在"); return; }
        try
        {
            box.ApplyTemplate();
            var text = box.Template?.FindName("SelectionText", box) as System.Windows.Controls.TextBlock;
            var binding = text is null ? null : BindingOperations.GetBindingExpression(text, System.Windows.Controls.TextBlock.TextProperty);
            var header = $"SelectedItem=<{box.SelectedItem}> ({box.SelectedItem?.GetType().Name})  " +
                         $"SelectionBoxItem=<{box.SelectionBoxItem}> ({box.SelectionBoxItem?.GetType().Name})  " +
                         $"Items={box.Items.Count}";
            var shown = text is null
                ? "模板里没有 SelectionText"
                : $"SelectionText.Text=<{text.Text}>  绑定={(binding is null ? "无（已被代码赋值覆盖）" : "在")}";
            Write("combo", $"{label}: {header}");
            Write("combo", $"{label}: {shown}");
            if (text is not null)
            {
                Write("combo", $"{label}: 显示字体=<{text.FontFamily}> 字号={text.FontSize} " +
                               $"前景={Describe(text.Foreground)} 可见={text.IsVisible} " +
                               $"尺寸={text.ActualWidth:0.##}x{text.ActualHeight:0.##}");
            }
        }
        catch (Exception error) { Write("combo", $"{label}: 读取失败 {error.Message}"); }
    }

    /// <summary>Records a bring-into-view request with who asked for it and where from.</summary>
    /// <remarks>
    /// The list scrolled by whole rows whenever the pointer rested on the last, partly cut row.
    /// Something is asking for that row to be shown; this says what, and the stack says who.
    /// </remarks>
    public static void BringIntoView(string label, object? sender, RequestBringIntoViewEventArgs e)
    {
        var scroll = FindAncestor<ScrollViewer>(sender as DependencyObject);
        var before = scroll?.VerticalOffset ?? double.NaN;
        var unit = scroll is null ? "?" : VirtualizingPanel.GetScrollUnit(scroll).ToString();
        Write("bring", $"{label}: target=<{Describe(e.TargetObject)}> rect={e.TargetRect} " +
                       $"偏移前={before:0.##} ScrollUnit={unit}");
        Write("bring", $"{label}: 调用栈 {new StackTrace(1, true)}");
    }

    /// <summary>
    /// A scrollbar thumb and everything above it that could be cutting it short.
    /// </summary>
    /// <remarks>
    /// The element measured full height while both the running window and an off-screen render
    /// drew it at roughly half that. Rather than argue about which layer clips it, every layer
    /// is written down with its clip setting.
    /// </remarks>
    public static void Thumbs(string label, DependencyObject root)
    {
        var found = 0;
        foreach (var thumb in Descendants<Thumb>(root))
        {
            found++;
            // Layout numbers alone cannot tell a clamp from a cover-up, so the drawing state is
            // recorded alongside them: render size, both kinds of clip, transform, and the
            // bounds the visual tree actually occupies.
            var pillForBounds = Descendants<Border>(thumb).FirstOrDefault();
            Write("thumb", $"{label}:   Render 尺寸={thumb.RenderSize} 布局裁剪={(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(thumb) as System.Windows.Media.RectangleGeometry)?.Rect.ToString() ?? "none"} Clip={Describe(thumb.Clip)} 变换={thumb.RenderTransform}");
            if (pillForBounds is not null)
            {
                Write("thumb", $"{label}:   Border Render 尺寸={pillForBounds.RenderSize} 布局裁剪={System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(pillForBounds)} Clip={Describe(pillForBounds.Clip)} 变换={pillForBounds.RenderTransform}");
            }
            try
            {
                var bounds = System.Windows.Media.VisualTreeHelper.GetDescendantBounds(thumb);
                Write("thumb", $"{label}:   子树实际占位 {bounds}");
            }
            catch (Exception error) { Write("thumb", $"{label}:   占位读取失败 {error.Message}"); }
            var bar = FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(thumb);
            var inBar = bar is null ? double.NaN : thumb.TransformToAncestor(bar).Transform(new System.Windows.Point(0, 0)).Y;
            Write("thumb", $"{label}: Thumb {thumb.ActualWidth:0.##} x {thumb.ActualHeight:0.##} " +
                           $"可见={thumb.IsVisible} 透明度={thumb.Opacity} 在轨道内的 y={inBar:0.##}");
            if (bar is not null)
            {
                Write("thumb", $"{label}:   滚动条 {bar.ActualWidth:0.##} x {bar.ActualHeight:0.##} " +
                               $"轨道 {bar.Track?.ActualHeight:0.##} 视口={bar.Track?.ViewportSize:0.##} " +
                               $"范围={bar.Track?.Maximum:0.##} 值={bar.Value:0.##} 方向反转={bar.Track?.IsDirectionReversed}");
            }
            var pill = Descendants<Border>(thumb).FirstOrDefault();
            if (pill is not null)
            {
                Write("thumb", $"{label}:   内层 Border {pill.ActualWidth:0.##} x {pill.ActualHeight:0.##} " +
                               $"Margin={pill.Margin} CornerRadius={pill.CornerRadius.TopLeft:0.##} " +
                               $"Opacity={pill.Opacity} 背景={Describe(pill.Background)}");
            }
            for (var node = VisualTreeHelper.GetParent(thumb); node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is not FrameworkElement element) continue;
                Write("thumb", $"{label}:   祖先 {element.GetType().Name}{(string.IsNullOrEmpty(element.Name) ? "" : "#" + element.Name)} " +
                               $"{element.ActualWidth:0.##} x {element.ActualHeight:0.##} " +
                               $"ClipToBounds={element.ClipToBounds} Margin={element.Margin}");
                if (element is ScrollViewer or System.Windows.Controls.Primitives.ScrollBar) break;
            }
        }
        Write("thumb", $"{label}: 共 {found} 个滑块");
    }

    // ---- helpers ------------------------------------------------------------------

    private static string Describe(object? value) => value switch
    {
        null => "null",
        FrameworkElement element => $"{element.GetType().Name}{(string.IsNullOrEmpty(element.Name) ? "" : "#" + element.Name)}",
        SolidColorBrush brush => $"SolidColorBrush({brush.Color})",
        _ => value.ToString() ?? "?"
    };

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
