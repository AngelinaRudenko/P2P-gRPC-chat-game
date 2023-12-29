namespace P2P.Node.Models;

internal class AppNode
{
    public AppNode(string name, string host, int port)
    {
        Name = name;
        Host = host;
        Port = port;
    }

    public string Name { get; }
    public string Host { get; }
    public int Port { get; }

    public override string ToString()
    {
        return $"http://{Host}:{Port}";
    }

    public override bool Equals(object? obj)
    {
        // ReSharper disable once UseNegatedPatternMatching
#pragma warning disable IDE0019
        var node = obj as AppNode;
#pragma warning restore IDE0019

        if (node == null)
        {
            return false;
        }

        return node.Host == Host && 
               node.Port == Port && 
               node.Name == Name;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Host, Port);
    }
}