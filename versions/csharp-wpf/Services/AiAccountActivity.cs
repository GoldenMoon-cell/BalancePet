namespace BalancePet.Wpf.Services;

/// <summary>
/// Metadata-only account state derived from the active CC Switch provider.
/// Credentials are never carried in this model; the token field is a SHA-256
/// fingerprint used only to match a local monitor profile.
/// </summary>
public sealed record AiAccountActivity(
    string State,
    string Provider,
    string AccountType,
    string AccountLabel,
    string Source,
    string Endpoint,
    string TokenFingerprint,
    double? ReportedBalance,
    string Currency);
