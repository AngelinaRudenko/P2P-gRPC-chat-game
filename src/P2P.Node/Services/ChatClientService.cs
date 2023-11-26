﻿using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using P2P.Node.Models;
using P2P.Node.Server;
using Proto;

namespace P2P.Node.Services;

internal class ChatClientService : IDisposable
{
    private readonly DateTime _startTimestamp;
    private readonly int _currentNodeId;
    private readonly NodeSettings[] _nodes;
    private GrpcChannel? _nextNodeChannel;
    private readonly Server.ChainService _chainService;
    private readonly Server.ChatService _chatService;

    private readonly Timer _isNextNodeAliveTimer;

    public ChatClientService(int nodeId, NodeSettings[] nodes, Server.ChainService chainService, Server.ChatService chatService)
    {
        _startTimestamp = DateTime.UtcNow;
        _currentNodeId = nodeId;
        _nodes = nodes;
        _chainService = chainService;
        _chatService = chatService;

        _isNextNodeAliveTimer = new Timer(IsNextNodeAlive, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task StartAsync()
    {
        await EstablishConnectionAsync();

        _chainService.OnDisconnectRequest += Disconnect;
        _chainService.OnLeaderElectionRequest += ElectLeader;
        _chainService.OnLeaderElected += StartChat;
        _chatService.OnChatRequest += Chat;

        _isNextNodeAliveTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5)); // check is alive status every 5 sec
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

            _isNextNodeAliveTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
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
        var nextNodeId = _currentNodeId;
        while (true)
        {
            nextNodeId = GetNextNodeId(nextNodeId, _nodes.Length);

            if (nextNodeId == _currentNodeId)
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
                var nextNodeClient = new Proto.ChainService.ChainServiceClient(nextNodeChannel);

                var askPermissionResult = await nextNodeClient.AskPermissionToConnectAsync(
                    new AskPermissionToConnectRequest { NodeWantsToConnectId = _currentNodeId },
                    deadline: DateTime.UtcNow.AddSeconds(1));

                if (!askPermissionResult.CanConnect)
                {
                    var previousNodeChannel = GrpcChannel.ForAddress(
                        _nodes[askPermissionResult.ConnectedNodeId].ToString(),
                        new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

                    if (await IsAliveAsync(previousNodeChannel))
                    {
                        var previousNodeClient = new Proto.ChainService.ChainServiceClient(previousNodeChannel);

                        var askToDisconnectResult = await previousNodeClient.AskToDisconnectAsync(
                            new AskToDisconnectRequest { NodeAsksToDiconnectId = _currentNodeId },
                            deadline: DateTime.UtcNow.AddSeconds(1));

                        if (!askToDisconnectResult.IsOk)
                        {
                            throw new Exception("Node doesn't agree to disconnect");
                        }
                    } 

                    await previousNodeChannel.ShutdownAsync();
                }

                var connectResult = await nextNodeClient.ConnectAsync(
                    new ConnectRequest { NodeWantsToConnectId = _currentNodeId },
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

        ElectLeader(Guid.NewGuid().ToString(), _currentNodeId, _startTimestamp);
    }

    public void ElectLeader(string electionLoopId, int leaderId, DateTime leaderConnectionTimestamp)
    {
        var client = new Proto.ChainService.ChainServiceClient(_nextNodeChannel);

        LeaderElectionRequest args;
        if (leaderConnectionTimestamp < _startTimestamp) // node started earlier than current node
        {
            args = new LeaderElectionRequest
            {
                ElectionLoopId = electionLoopId,
                LeaderId = leaderId,
                LeaderConnectionTimestamp = Timestamp.FromDateTime(leaderConnectionTimestamp)
            };
        }
        else // current node started earlier than node
        {
            args = new LeaderElectionRequest
            {
                ElectionLoopId = electionLoopId,
                LeaderId = _currentNodeId,
                LeaderConnectionTimestamp = Timestamp.FromDateTime(_startTimestamp)
            };
        }

        client.ElectLeaderAsync(args, deadline: DateTime.UtcNow.AddSeconds(1)); // do not wait
    }

    public void StartChat()
    {
        _chainService.OnLeaderElected -= StartChat;

        if (_chatService.ChatInProgress || _chainService.LeaderId != _currentNodeId)
        {
            Console.WriteLine("Game is in progress, wait for your turn");
            return;
        }

        Console.WriteLine("Start new game, write the message for the next player");
        var input = Console.ReadLine();

        var client = new Proto.ChatService.ChatServiceClient(_nextNodeChannel);
        // do not wait
        client.ChatAsync(new ChatRequest { Text = input, ChatId = Guid.NewGuid().ToString() }, deadline: DateTime.UtcNow.AddSeconds(1));
    }

    public void Chat(string receivedMessage, string chatId)
    {
        Console.WriteLine($"Previous player said '{receivedMessage}'. Write message to next player:");
        var input = Console.ReadLine();

        var client = new Proto.ChatService.ChatServiceClient(_nextNodeChannel);
        // do not wait
        client.ChatAsync(new ChatRequest { Text = input, ChatId = chatId }, deadline: DateTime.UtcNow.AddSeconds(1));
    }

    public void Disconnect()
    {
        ConsoleHelper.WriteRed("Disconnect from next node");
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