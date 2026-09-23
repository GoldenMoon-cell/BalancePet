using System.Threading;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Lets an out-of-process extension replace the built-in bubble presentation
/// without loading plugin code into the BalancePet process.
/// </summary>
internal static class NotificationPresentationBridge
{
    internal const string PresenterMutexName = @"Local\BalancePet.NotificationPresenter.v1";

    public static bool IsExternalPresenterActive()
    {
        try
        {
            using var presenter = Mutex.OpenExisting(PresenterMutexName);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static async Task WaitForExternalPresenterAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (IsExternalPresenterActive()) return;
            await Task.Delay(40).ConfigureAwait(true);
        }
    }
}
