namespace P2P.Node.Models;

internal class Settings
{
    public required TimeoutSettings TimeoutSettings { get; set; } = null!;
}

/// <summary>
/// Settings are in seconds
/// </summary>
internal class TimeoutSettings
{
    public required double CommonRequestTimeout { get; set; }
    public required double ConnectRequestTimeout { get; set; }
    public required double DisconnectRequestTimeout { get; set; }
    public required double IsAliveTimerPeriod { get; set; }
    public required double IsAliveRequestTimeout { get; set; }
}