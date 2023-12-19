namespace P2P.Node.Models;

internal class AppNode
{
    public AppNode(string? name, string host, int port)
    {
        Name = name;
        Host = host;
        Port = port;
    }

    public AppNode(string host, int port) : this(null, host, port)
    {
    }

    public string? Name { get; }
    public string Host { get; }
    public int Port { get; }

    public override string ToString()
    {
        return $"http://{Host}:{Port}";
    }
}