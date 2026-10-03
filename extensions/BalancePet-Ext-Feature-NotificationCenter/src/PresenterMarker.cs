using System.IO;
namespace BalancePet.NotificationCenter;

/// <summary>
/// The marker the host looks for to decide whether to show its own bubbles.
/// </summary>
/// <remarks>
/// The published contract is a named mutex: while an enabled extension holds
/// <c>Local\BalancePet.NotificationPresenter.v1</c>, the host keeps recording what happened
/// but stops drawing its own bubble. So taking over is holding the object and giving the
/// bubbles back is letting it go — no message has to be sent, and the change takes effect on
/// the host's next bubble.
///
/// Holding it can be refused: a low-integrity process cannot open an object a
/// medium-integrity one created, which is measured behaviour on this machine. A refusal is
/// reported and otherwise ignored — the extension still works, it just does not take the
/// host's bubbles away.
/// </remarks>
internal static class PresenterMarker
{
    private const string MutexName = @"Local\BalancePet.NotificationPresenter.v1";
    private static Mutex? _held;
    private static readonly object Gate = new();

    public static bool Holding
    {
        get { lock (Gate) { return _held is not null; } }
    }

    /// <summary>Holds the marker, or lets it go.</summary>
    public static void Apply(bool takeOver)
    {
        lock (Gate)
        {
            if (takeOver)
            {
                if (_held is not null) return;
                try
                {
                    _held = new Mutex(false, MutexName);
                }
                catch (Exception error) when (error is UnauthorizedAccessException or WaitHandleCannotBeOpenedException
                    or System.IO.IOException or NotSupportedException or System.Security.SecurityException)
                {
                    _held = null;
                    Log($"无法接管气泡：{error.Message}");
                }
                return;
            }

            try { _held?.Dispose(); } catch (Exception) { }
            _held = null;
        }
    }

    public static void Release() => Apply(false);

    private static void Log(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BalancePet");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "notification-center.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Being unable to write down a failure must not become one.
        }
    }
}
