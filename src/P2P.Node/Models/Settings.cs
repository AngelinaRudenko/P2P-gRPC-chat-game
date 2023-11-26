namespace P2P.Node.Models;

internal class Settings
{
    public required int CurrentNodeId { get; set; }
    public required NodeSettings[] NodesSettings { get; set; } = null!;
    public required TimeoutSettings TimeoutSettings { get; set; } = null!;
}

internal class NodeSettings
{
    public required string Host { get; set; }
    public required int Port { get; set; }

    public override string ToString()
    {
        return $"http://{Host}:{Port}";
    }
}

/// <summary>
/// Settings are in seconds
/// </summary>
internal class TimeoutSettings
{
    public required double CommonRequestTimeout { get; set; }
    public required double DisconnectRequestTimeout { get; set; }
    public required double ReestablishConnectionPeriod { get; set; }
    public required double IsAliveTimerPeriod { get; set; }
    public required double IsAliveRequestTimeout { get; set; }
}