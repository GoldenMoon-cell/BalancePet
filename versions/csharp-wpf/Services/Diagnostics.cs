using System.Windows;
using System.Windows.Input;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Placeholder for the diagnostic instrumentation used while tracking down the font
/// picker's faults. It is inert: nothing is written, no file is created, and every entry
/// point returns immediately.
/// </summary>
/// <remarks>
/// Kept as an empty shell rather than deleted, because the call sites sit inside layout and
/// input paths where a hasty removal is easy to get wrong, and a release is the wrong place to
/// discover that. The faults it was built for are fixed or recorded, so there is nothing left
/// to record. Delete the class and its call sites together when the result can be checked by
/// running the window rather than while packaging.
/// </remarks>
public static class Diagnostics
{
    public static bool Enabled => false;

    public static void Banner(string version) { }

    public static void Write(string area, string message) { }

    public static void Combo(string label, System.Windows.Controls.ComboBox? box) { }

    public static void BringIntoView(string label, object? sender, RequestBringIntoViewEventArgs e) { }

    public static void Thumbs(string label, DependencyObject root) { }
}
