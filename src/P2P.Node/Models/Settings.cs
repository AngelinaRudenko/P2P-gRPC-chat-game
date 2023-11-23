namespace P2P.Node.Models;

internal class Settings
{
    public required int CurrentNodeId { get; set; }
    public required NodeSettings[] NodesSettings { get; set; } = null!;
}

internal class NodeSettings
{
    public required string Host { get; set; }
    public required int Port { get; set; }
}