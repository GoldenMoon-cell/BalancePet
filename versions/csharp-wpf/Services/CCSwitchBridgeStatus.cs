namespace BalancePet.Wpf.Services;

public enum CCSwitchBridgeStatusKind
{
    Connected,
    DatabaseMissing,
    DatabaseLocked,
    DatabaseCorrupt,
    DatabaseUnreadable,
    NoCurrentCodexAccount
}

public sealed record CCSwitchBridgeStatus(
    CCSwitchBridgeStatusKind Kind,
    int CurrentAccountCount);
