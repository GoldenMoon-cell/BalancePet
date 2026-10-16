namespace BalancePet.Wpf.Services;

/// <summary>
/// Counts repeats of one task source, so that a single message can stand for a burst of them.
/// </summary>
/// <remarks>
/// The task integration reports a completion per finished task, and a working session
/// finishes them faster than a person reads them. One bubble per completion turns the pet
/// into a machine for interrupting whoever is watching it: the first is news, the tenth is
/// noise, and none of them gets read. A repeat of one source inside a short window is
/// therefore folded into the message already on screen, and the count moves into the label,
/// which is what a single message can no longer say by arriving again.
/// </remarks>
public sealed class TaskBubbleCounter
{
    /// <summary>
    /// How long one source keeps counting into the same message.
    /// </summary>
    /// <remarks>
    /// Long enough to cover a working session, short enough that a task finished tomorrow
    /// morning is not added to this evening's tally: the count reports a burst, and a burst
    /// that has paused is over.
    /// </remarks>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(90);

    private readonly Dictionary<string, Tally> _tallies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What one arrival means: which message it belongs to, how many arrivals that message
    /// now stands for, and what they spent between them.
    /// </summary>
    /// <param name="Key">Groups repeats. Same key, same message — the caller merges on it.</param>
    /// <param name="Count">Arrivals inside the window, including this one.</param>
    /// <param name="Spent">Everything the counted arrivals spent, added up.</param>
    public readonly record struct Reading(string Key, int Count, double Spent);

    /// <summary>
    /// Registers one arrival.
    /// </summary>
    /// <param name="source">The client that reported it, as shown to the user.</param>
    /// <param name="kind">
    /// What it is reporting. Kept separate from the source because a finished task, a running
    /// one, and a record that could not be attributed are three different messages even when
    /// they came from the same client.
    /// </param>
    /// <param name="spent">What this arrival spent, when that is known.</param>
    /// <param name="nowUtc">
    /// The current instant, passed in rather than read, so a caller can be tested without
    /// waiting ninety seconds to find out whether the window closes.
    /// </param>
    public Reading Register(string source, string kind, double spent, DateTime nowUtc)
    {
        var key = $"{source}|{kind}";
        var tally = _tallies.TryGetValue(key, out var previous) && nowUtc - previous.AtUtc <= Window
            ? previous with { Count = previous.Count + 1, Spent = previous.Spent + spent, AtUtc = nowUtc }
            : new Tally(1, spent, nowUtc);
        _tallies[key] = tally;
        return new Reading(key, tally.Count, tally.Spent);
    }

    private readonly record struct Tally(int Count, double Spent, DateTime AtUtc);
}
