namespace BalancePet.UsageAnalytics;

public sealed record UsageReport(
    UsageWindowStats FiveHours,
    UsageWindowStats TwentyFourHours,
    UsageWindowStats SevenDays,
    UsageWindowStats AllTime,
    IReadOnlyList<UsageEvent> RecentEvents)
{
    public static UsageReport Build(IReadOnlyList<UsageEvent> events, DateTimeOffset now)
    {
        var ordered = events.OrderByDescending(value => value.OccurredAt).ToArray();
        return new UsageReport(
            UsageWindowStats.Build(ordered, now - TimeSpan.FromHours(5), now),
            UsageWindowStats.Build(ordered, now - TimeSpan.FromHours(24), now),
            UsageWindowStats.Build(ordered, now - TimeSpan.FromDays(7), now),
            UsageWindowStats.Build(ordered, null, now),
            ordered.Take(100).ToArray());
    }
}

public sealed record UsageWindowStats(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    int Requests,
    int SuccessfulRequests,
    double? AverageFirstTokenMs,
    double? OutputPerSecond,
    DateTimeOffset? LatestEvent,
    double? AverageDurationMs)
{
    public bool HasTokenData { get; init; }
    public long TotalTokens => InputTokens + OutputTokens;
    /// <summary>
    /// Codex reports input_tokens as the complete prompt total, including the
    /// part served from the prompt cache.  Keep the uncached portion separate
    /// for the trend chart so its meaning matches relay dashboards.
    /// </summary>
    public long UncachedInputTokens => Math.Max(0, InputTokens - CacheReadTokens);
    public long CacheCreationTokens => CacheWriteTokens;
    public double CacheHitPercent
    {
        get
        {
            if (InputTokens <= 0) return 0;
            return Math.Clamp(CacheReadTokens * 100d / InputTokens, 0, 100);
        }
    }

    public double SuccessPercent => Requests == 0 ? 0 : SuccessfulRequests * 100d / Requests;

    public static UsageWindowStats Build(IEnumerable<UsageEvent> source, DateTimeOffset? from, DateTimeOffset to)
    {
        var values = source.Where(value => value.OccurredAt <= to && (!from.HasValue || value.OccurredAt >= from.Value)).ToArray();
        var ttft = values.Where(value => value.TimeToFirstTokenMs is >= 0).Select(value => (double)value.TimeToFirstTokenMs!.Value).ToArray();
        var throughputSamples = values.Where(value => value.OutputTokens is >= 0 && value.DurationMs is > 0)
            .Select(value => value.OutputTokens!.Value / (value.DurationMs!.Value / 1000d)).Where(value => value >= 0).ToArray();
        return new UsageWindowStats(
            Sum(values, value => value.InputTokens),
            Sum(values, value => value.OutputTokens),
            Sum(values, value => value.CacheReadTokens),
            Sum(values, value => value.CacheWriteTokens),
            values.Length,
            values.Count(value => value.Success == true),
            ttft.Length == 0 ? null : ttft.Average(),
            throughputSamples.Length == 0 ? null : throughputSamples.Average(),
            values.FirstOrDefault()?.OccurredAt,
            Average(values, value => value.DurationMs))
        {
            HasTokenData = values.Any(value => value.InputTokens.HasValue || value.OutputTokens.HasValue || value.CacheReadTokens.HasValue || value.CacheWriteTokens.HasValue)
        };
    }

    private static long Sum(IEnumerable<UsageEvent> values, Func<UsageEvent, long?> selector)
        => values.Sum(value => Math.Clamp(selector(value) ?? 0, 0, 10_000_000_000));

    private static double? Average(IEnumerable<UsageEvent> values, Func<UsageEvent, long?> selector)
    {
        var samples = values.Select(selector).Where(value => value is >= 0).Select(value => (double)value!.Value).ToArray();
        return samples.Length == 0 ? null : samples.Average();
    }
}

public static class UsageFormatting
{
    public static string Tokens(long value)
        => value >= 1_000_000 ? $"{value / 1_000_000d:0.##}M"
        : value >= 1_000 ? $"{value / 1_000d:0.##}K" : value.ToString("N0");

    public static string Milliseconds(double? value)
    {
        if (value is null) return "—";
        if (value.Value >= 3_600_000) return $"{value.Value / 3_600_000:0.##} h";
        if (value.Value >= 60_000) return $"{value.Value / 60_000:0.##} min";
        return value.Value >= 1000 ? $"{value.Value / 1000:0.##} s" : $"{value.Value:0} ms";
    }

    public static string FirstToken(double? value) => value is null ? "暂无数据" : Milliseconds(value);

    public static string Rate(double? value) => value is null ? "—" : $"{value.Value:0} tok/s";
}
