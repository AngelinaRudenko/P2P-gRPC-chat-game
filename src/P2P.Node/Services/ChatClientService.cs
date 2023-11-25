﻿using Grpc.Core;
using Grpc.Net.Client;
using P2P.Node.Models;
using Proto;

namespace P2P.Node.Services;

internal class ChatClientService : IDisposable
{
    private readonly Proto.Node _currentNode;
    private readonly NodeSettings[] _nodes;
    private GrpcChannel? _nextNodeChannel;

    private readonly Timer _isNextNodeAliveTimer;

    public ChatClientService(int nodeId, NodeSettings[] nodes)
    {
        _currentNode = new Proto.Node
        {
            Id = nodeId,
            Host = nodes[nodeId].Host,
            Port = nodes[nodeId].Port
        };
        _nodes = nodes;

        _isNextNodeAliveTimer = new Timer(IsNextNodeAlive, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task StartAsync()
    {
        await EstablishConnectionAsync();

        _isNextNodeAliveTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5)); // check is alive status every 5 sec

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
        if (_nextNodeChannel == null || !IsAliveAsync(_nextNodeChannel).Result)
        {
            _isNextNodeAliveTimer.Change(Timeout.Infinite, Timeout.Infinite);

            ConsoleHelper.Debug("Reconnect");
            EstablishConnectionAsync().Wait();

            _isNextNodeAliveTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
    }
    private static async Task<bool> IsAliveAsync(GrpcChannel channel)
    {
        try
        {
            var isAlive = await channel.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(0.5))
                .ContinueWith(task => task.Status == TaskStatus.RanToCompletion);

            return isAlive;
        }
        catch
        {
            return false;
        }
    }

    private async Task EstablishConnectionAsync()
    {
        var nextNodeId = _currentNode.Id;
        while (true)
        {
            nextNodeId = GetNextNodeId(nextNodeId, _nodes.Length);

            if (nextNodeId == _currentNode.Id)
            {
                ConsoleHelper.Debug("Couldn't connect to any node. Sleep for 10 sec");
                await Task.Delay(TimeSpan.FromSeconds(10));
                continue;
            }

            var nextNodeChannel = GrpcChannel.ForAddress(
                _nodes[nextNodeId].ToString(),
                new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

            if (!await IsAliveAsync(nextNodeChannel))
            {
                ConsoleHelper.Debug($"Node {nextNodeId} is not alive");
                continue;
            }

            ConsoleHelper.Debug($"Node {nextNodeId} is alive");

            try
            {
                var nextNodeClient = new ChainService.ChainServiceClient(nextNodeChannel);

                var askPermissionResult = await nextNodeClient.AskPermissionToConnectAsync(
                    new AskPermissionToConnectRequest { NodeWantsToConnect = _currentNode },
                    deadline: DateTime.UtcNow.AddSeconds(1));

                if (!askPermissionResult.CanConnect)
                {
                    var previousNodeChannel = GrpcChannel.ForAddress(
                        _nodes[askPermissionResult.ConnectedNode.Id].ToString(),
                        new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

                    if (await IsAliveAsync(previousNodeChannel))
                    {
                        var previousNodeClient = new ChainService.ChainServiceClient(previousNodeChannel);

                        var askToDisconnectResult = await previousNodeClient.AskToDisconnectAsync(
                            new AskToDisconnectRequest { NodeAsksToDiconnect = _currentNode },
                            deadline: DateTime.UtcNow.AddSeconds(1));

                        if (!askToDisconnectResult.IsOk)
                        {
                            throw new Exception("Node doesn't agree to disconnect");
                        }
                    } 

                    await previousNodeChannel.ShutdownAsync();
                }

                var connectResult = await nextNodeClient.ConnectAsync(
                    new ConnectRequest { NodeWantsToConnect = _currentNode },
                    deadline: DateTime.UtcNow.AddSeconds(1));

                if (!connectResult.IsOk)
                {
                    throw new Exception($"Failed to connect to node {nextNodeId}");
                }

                ConsoleHelper.WriteGreen($"Connected to node {nextNodeId} http://{_nodes[nextNodeId].Host}:{_nodes[nextNodeId].Port}");

                _nextNodeChannel = nextNodeChannel;
                break;
            }
            catch
            {
                await nextNodeChannel.ShutdownAsync();
                ConsoleHelper.WriteRed($"Failed ot connect to node {nextNodeId} http://{_nodes[nextNodeId].Host}:{_nodes[nextNodeId].Port}");
            }
        }

        ElectLeader(Guid.NewGuid().ToString(), _currentNode.Id);
    }

    public void ElectLeader(string electionLoopId, int leaderId)
    {
        var client = new ChainService.ChainServiceClient(_nextNodeChannel);
        client.ElectLeaderAsync(new LeaderElectionRequest
            { ElectionLoopId = electionLoopId, LeaderId = int.Max(leaderId, _currentNode.Id) },
            deadline: DateTime.UtcNow.AddSeconds(1)); // do not wait
    }

    public void Disconnect()
    {
        ConsoleHelper.WriteRed("Disconnect");
        _nextNodeChannel?.ShutdownAsync().Wait();
        _nextNodeChannel = null;
        IsNextNodeAlive(null); // re-establish connection
    }

    public void Dispose()
    {
        _nextNodeChannel?.ShutdownAsync().Wait();
        _nextNodeChannel?.Dispose();
        _isNextNodeAliveTimer.Dispose();
    }
}