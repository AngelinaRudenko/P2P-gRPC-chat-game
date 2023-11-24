using Grpc.Core;
using Grpc.Net.Client;
using P2P.Node.Models;
using Proto;

namespace P2P.Node.Services;

internal class ChatClientService
{
    private readonly int _currentNodeId;
    private readonly NodeSettings _currentNode;
    private readonly NodeSettings[] _nodes;
    private GrpcChannel? _nextNodeChannel;

    private readonly Timer _isNextNodeAliveTimer;

    public ChatClientService(int nodeId, NodeSettings node, NodeSettings[] nodes)
    {
        _currentNodeId = nodeId;
        _currentNode = node;
        _nodes = nodes;

        _isNextNodeAliveTimer = new Timer(IsNextNodeAlive, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task Start()
    {
        await EstablishConnection();

        _isNextNodeAliveTimer.Change(Timeout.InfiniteTimeSpan, TimeSpan.FromSeconds(5)); // check is alive status every 5 sec

        //var input = string.Empty;
        //while (input != "/q")
        //{
        //    input = Console.ReadLine();

        //    var client = new Proto.ChatService.ChatServiceClient(channel!);
        //    var result = await client.ChatAsync(new ChatRequest { Text = input });
        //    Console.WriteLine(result.IsOk);
        //}
    }

    private static int GetNextNodeId(int id, int nodesCount)
    {
        return id + 1 == nodesCount ? 0 : ++id;
    }


    private void IsNextNodeAlive(object? stateInfo)
    {
        if (_nextNodeChannel == null || !IsAlive(_nextNodeChannel).Result)
        {
            Console.WriteLine("Reconnect");
            EstablishConnection().Wait();
        }
    }
    private static async Task<bool> IsAlive(GrpcChannel channel)
    {
        var isAlive = await channel.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(1))
            .ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    return true;
                }
                else
                {
                    // Faulted
                    return false;
                }
            });

        return isAlive;
    }

    private async Task AskNodeToDisconnect(GrpcChannel channel)
    {
        var client = new Proto.ChainService.ChainServiceClient(channel);

        var result = await client.AskToDisconnectAsync(
            new AskToDisconnectRequest
            {
                NodeAsksToDiconnect = new Proto.Node
                {
                    Id = _currentNodeId,
                    Host = _currentNode.Host,
                    Port = _currentNode.Port
                }
            }, deadline: DateTime.UtcNow.AddSeconds(3));

        if (!result.IsOk)
        {
            // TODO: retry
        }
    }

    private async Task EstablishConnection()
    {
        var nextNodeId = _currentNodeId;
        while (true)
        {
            nextNodeId = GetNextNodeId(nextNodeId, _nodes.Length);

            if (nextNodeId == _currentNodeId)
            {
                Console.WriteLine("Couldn't connect to any node. Sleep for 30 sec");
                await Task.Delay(30000);
                continue;
            }

            var nextNode = _nodes[nextNodeId];

            var nextNodeChannel = GrpcChannel.ForAddress($"http://{nextNode.Host}:{nextNode.Port}",
                new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

            if (!await IsAlive(nextNodeChannel))
            {
                Console.WriteLine($"Node {nextNodeId} http://{nextNode.Host}:{nextNode.Port} is not alive");
                continue;
            }

            Console.WriteLine($"Node {nextNodeId} http://{nextNode.Host}:{nextNode.Port} is alive");

            try
            {
                var client = new Proto.ChainService.ChainServiceClient(nextNodeChannel);

                var askPermissionResult = await client.AskPermissionToConnectAsync(
                    new AskPermissionToConnectRequest
                    {
                        NodeWantsToConnect = new Proto.Node
                        {
                            Id = _currentNodeId,
                            Host = _currentNode.Host,
                            Port = _currentNode.Port
                        }
                    }, deadline: DateTime.UtcNow.AddSeconds(1));

                if (!askPermissionResult.CanConnect)
                {
                    if (askPermissionResult.ConnectedNode.Id == _currentNodeId)
                    {
                        Console.WriteLine($"Node was already connected to node {nextNodeId}");
                        _nextNodeChannel = nextNodeChannel;
                        break;
                    }

                    var previousNodeChannel = GrpcChannel.ForAddress(
                        $"http://{askPermissionResult.ConnectedNode.Host}:{askPermissionResult.ConnectedNode.Port}",
                        new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

                    await AskNodeToDisconnect(previousNodeChannel);
                }

                await Connect(nextNodeChannel);

                Console.WriteLine($"Connected to node {nextNodeId} http://{nextNode.Host}:{nextNode.Port}");

                _nextNodeChannel = nextNodeChannel;
                break;
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"Failed ot connect to node {nextNodeId} http://{nextNode.Host}:{nextNode.Port}");
            }
        }
    }

    private async Task Connect(GrpcChannel channel)
    {
        var client = new Proto.ChainService.ChainServiceClient(channel);

        var connectResult = await client.ConnectAsync(new ConnectRequest
        {
            NodeWantsToConnect = new Proto.Node
            {
                Id = _currentNodeId,
                Host = _currentNode.Host,
                Port = _currentNode.Port
            }
        }, deadline: DateTime.UtcNow.AddSeconds(3));

        if (!connectResult.IsOk)
        {
            // TODO: retry
        }
    }

    public void Disconnect()
    {
        // TODO
        _isNextNodeAliveTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }
}