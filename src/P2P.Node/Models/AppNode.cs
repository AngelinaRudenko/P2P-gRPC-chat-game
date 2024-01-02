using Grpc.Core;
using Grpc.Net.Client;

namespace P2P.Node.Models;

internal class AppNode : IDisposable
{
    public AppNode(string name, string host, int port)
    {
        Name = name;
        Host = host;
        Port = port;
        Channel = new Lazy<GrpcChannel>(() => GrpcChannel.ForAddress(ToString(), new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure }));
    }

    public string Name { get; }
    public string Host { get; }
    public int Port { get; }

    public Lazy<GrpcChannel> Channel { get; }

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

    public void Dispose()
    {
        if (Channel.IsValueCreated)
        {
            Channel.Value.ShutdownAsync().Wait();
            Channel.Value.Dispose();
        }
    }
}