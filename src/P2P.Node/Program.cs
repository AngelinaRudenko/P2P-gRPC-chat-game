using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using P2P.Node.Models;
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

            var currentNode = settings.NodesSettings[settings.CurrentNodeId];

            var chatServer = new ChatServer(currentNode.Host, currentNode.Port);
            await chatServer.Start();

            bool foundNext = false;
            int nextNodeId = settings.CurrentNodeId;
            NodeSettings nextNode = currentNode;
            GrpcChannel? channel = null;
            while (!foundNext)
            {
                nextNodeId = GetNextNodeId(nextNodeId, settings.NodesSettings.Length);

                if (nextNodeId == settings.CurrentNodeId)
                {
                    Console.WriteLine("Couldn't connect to any node. Sleep for 30 sec");
                    await Task.Delay(30000);
                    continue;
                }

                nextNode = settings.NodesSettings[nextNodeId];

                channel = GrpcChannel.ForAddress($"http://{nextNode.Host}:{nextNode.Port}",
                    new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

                var isAlive = await channel.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(1)).ContinueWith(task =>
                {
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        Console.WriteLine($"Node {nextNodeId} http://{nextNode.Host}:{nextNode.Port} is alive");
                        return true;
                    }
                    else
                    {
                        // Faulted
                        Console.WriteLine($"Node {nextNodeId} http://{nextNode.Host}:{nextNode.Port} is not alive");
                        return false;
                    }
                });

                if (!isAlive)
                {
                    continue;
                }

                var client = new ChainService.ChainServiceClient(channel);

                try
                {
                    var result = await client.AskPermissionToConnectAsync(new ConnectToNodeRequest(), deadline: DateTime.UtcNow.AddSeconds(1));
                    foundNext = result.IsConnected;
                }
                catch (RpcException ex)
                {
                    Console.WriteLine($"Failed ot connect to node {nextNodeId} http://{nextNode.Host}:{nextNode.Port}");
                }
            }

            Console.WriteLine($"Connected to node {nextNodeId} http://{nextNode.Host}:{nextNode.Port}");

            var input = string.Empty;
            while (input != "/q")
            {
                input = Console.ReadLine();

                var client = new ChatService.ChatServiceClient(channel!);
                var result = await client.ChatAsync(new ChatRequest { Text = input });
                Console.WriteLine(result.IsOk);
            }

            await chatServer.Stop();
        }

        private static int GetNextNodeId(int id, int nodesCount)
        {
            return id + 1 == nodesCount ? 0 : ++id;
        }
    }
}