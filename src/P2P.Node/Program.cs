using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using P2P.Node.Models;
using P2P.Node.Server;
using P2P.Node.Services;
using Proto;

namespace P2P.Node
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Build a config object, using env vars and JSON providers.
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddEnvironmentVariables()
                .Build();

            // Get values from the config given their key and their target type.
            var settings = config.GetRequiredSection(nameof(Settings)).Get<Settings>() ??
                           throw new Exception("Configuration couldn't load");

            Console.WriteLine($"Node {settings.CurrentNodeId}");

            var currentNode = settings.NodesSettings[settings.CurrentNodeId];

            var chatServer = new ChatServer(currentNode.Host, currentNode.Port);
            await chatServer.Start();

            var chatClient = new ChatClientService(settings.CurrentNodeId, currentNode, settings.NodesSettings);
            await chatClient.Start();

            chatServer.OnDisconnectRequest += chatClient.Disconnect;

            while (true)
            {
            }

            await chatServer.Stop();
        }
    }
}