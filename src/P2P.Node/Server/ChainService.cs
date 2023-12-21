using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using P2P.Node.Models;
using Proto;

namespace P2P.Node.Server;

internal class ChainService : Proto.ChainService.ChainServiceBase, IDisposable
{
    private readonly TimeoutSettings _timeoutSettings;

    private readonly Proto.Node _currentNode;
    public Topology Topology { get; set; } = new();
    public GrpcChannel? PreviousNodeChannel { get; set; }
    public GrpcChannel? NextNodeChannel { get; set; }

    private string _electionLoopId = string.Empty;
    private bool _electionLoopInProgress;

    public delegate void LeaderElectionHandler(LeaderElectionRequest request);
    public event LeaderElectionHandler? OnLeaderElection;
    public event LeaderElectionHandler? OnLeaderElectionResult;

    public ChainService(Proto.Node currentNode, TimeoutSettings timeoutSettings)
    {
        _currentNode = currentNode;
        _timeoutSettings = timeoutSettings;
    }

    public override async Task<ConnectResponse> Connect(ConnectRequest request, ServerCallContext context)
    {
        var nodeWantsToConnect = request.NodeWantsToConnect;

        var previousNodeTopology = new Topology
        {
            PreviousNode = Topology.PreviousNode, NextNode = _currentNode, NextNextNode = Topology.NextNode,
            Leader = Topology.Leader
        };
        
        if (nodeWantsToConnect.Host == _currentNode.Host && nodeWantsToConnect.Port == _currentNode.Port)
        {
            ConsoleHelper.Debug("I am the first one and leader");
            previousNodeTopology.Leader = _currentNode;
            previousNodeTopology.NextNextNode = _currentNode;
            Topology = previousNodeTopology;
        }
        else
        {
            ConsoleHelper.Debug($"Node {nodeWantsToConnect.Name} wants to connect, ask previous node {Topology.PreviousNode.Name} to disconnect");

            var wasOnlyNodeInCircle = Topology.NextNode.Host == _currentNode.Host && Topology.NextNode.Port == _currentNode.Port;

            if (wasOnlyNodeInCircle)
            {
                previousNodeTopology.NextNextNode = nodeWantsToConnect;
            }

            PreviousNodeChannel = GrpcChannel.ForAddress($"http://{Topology.PreviousNode.Host}:{Topology.PreviousNode.Port}",
                new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

            var isAlive = await PreviousNodeChannel.ConnectAsync()
                .WaitAsync(TimeSpan.FromSeconds(_timeoutSettings.IsAliveRequestTimeout))
                .ContinueWith(task => task.Status == TaskStatus.RanToCompletion);

            if (isAlive)
            {
                var previousNodeClient = new Proto.ChainService.ChainServiceClient(PreviousNodeChannel);
                var askToDisconnectResult = await previousNodeClient.DisconnectAsync(
                    new DisconnectRequest { ConnectToNode = nodeWantsToConnect },
                    deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.DisconnectRequestTimeout));

                if (!askToDisconnectResult.IsOk)
                {
                    throw new Exception($"Node {Topology.PreviousNode.Name} doesn't agree to disconnect");
                }

                ConsoleHelper.Debug($"Allow node {nodeWantsToConnect.Name} to connect, previous node {Topology.PreviousNode.Name} is disconnected from current");
            }
            else
            {
                ConsoleHelper.Debug($"Allow node {nodeWantsToConnect.Name} to connect, previous node {Topology.PreviousNode.Name} is not alive");
                previousNodeTopology.PreviousNode = null;

                if (Topology.NextNextNode.Host == Topology.PreviousNode.Host &&
                    Topology.NextNextNode.Port == Topology.PreviousNode.Port)
                {
                    Topology.NextNextNode = _currentNode;
                }
            }

            if (wasOnlyNodeInCircle)
            {
                // do not await
                Task.Run(() =>
                {
                    OnLeaderElectionResult?.Invoke(new LeaderElectionRequest
                    {
                        ElectionLoopId = "start",
                        LeaderNode = _currentNode,
                        LeaderConnectionTimestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                    });
                });
            }
        }

        Topology.PreviousNode = nodeWantsToConnect;
        ConsoleHelper.LogTopology(Topology);

        return await Task.FromResult(new ConnectResponse { IsOk = true, Topology = previousNodeTopology });
    }

    public override async Task<DisconnectResponse> Disconnect(DisconnectRequest request, ServerCallContext context)
    {
        ConsoleHelper.Debug($"Disconnect from node {Topology.NextNode.Name} and connect to node {request.ConnectToNode.Name}");
        var oldNextNode = Topology.NextNode;
        Topology.NextNode = request.ConnectToNode;
        await NextNodeChannel!.ShutdownAsync();
        NextNodeChannel = GrpcChannel.ForAddress($"http://{Topology.NextNode.Host}:{Topology.NextNode.Port}",
            new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

        PreviousNodeChannel ??= GrpcChannel.ForAddress($"http://{Topology.PreviousNode.Host}:{Topology.PreviousNode.Port}",
            new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

        var isAlive = await PreviousNodeChannel.ConnectAsync()
            .WaitAsync(TimeSpan.FromSeconds(_timeoutSettings.IsAliveRequestTimeout))
            .ContinueWith(task => task.Status == TaskStatus.RanToCompletion);

        if (isAlive)
        {
            Topology.NextNextNode = oldNextNode;

            var previousNodeClient = new Proto.ChainService.ChainServiceClient(PreviousNodeChannel);
            await previousNodeClient.SetNextNextNodeAsync(
                new SetNextNextNodeRequest { NextNextNode = Topology.NextNode },
                deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));
        }

        ConsoleHelper.LogTopology(Topology);

        return await Task.FromResult(new DisconnectResponse { IsOk = true });
    }

    public override Task<SetNextNextNodeResponse> SetNextNextNode(SetNextNextNodeRequest request, ServerCallContext context)
    {
        ConsoleHelper.Debug($"Set new next next {request.NextNextNode.Name} instead of {Topology.NextNextNode.Name}");
        Topology.NextNextNode = request.NextNextNode;
        ConsoleHelper.LogTopology(Topology);

        return Task.FromResult(new SetNextNextNodeResponse { IsOk = true });
    }

    public override Task<LeaderElectionResponse> ElectLeader(LeaderElectionRequest request, ServerCallContext context)
    {
        if (_electionLoopId.Equals(request.ElectionLoopId, StringComparison.InvariantCulture) == false)
        {
            ConsoleHelper.Debug($"Start election loop {request.ElectionLoopId}");
            _electionLoopInProgress = true;
            _electionLoopId = request.ElectionLoopId;
            OnLeaderElection?.Invoke(request);
        }
        else if (_electionLoopInProgress)
        {
            // else - leader found, need to propagate
            ConsoleHelper.WriteGreen($"Updating loop {request.ElectionLoopId} is finished");
            Topology.Leader = request.LeaderNode;
            _electionLoopInProgress = false;
            OnLeaderElectionResult?.Invoke(request);
            ConsoleHelper.LogTopology(Topology);
        }

        return Task.FromResult(new LeaderElectionResponse { IsOk = true });
    }

    public bool ChatInProgress { get; set; }
    public string ChatId { get; set; } = string.Empty;

    public delegate void ChatHandler(ChatRequest request);
    public event ChatHandler? OnChat;
    public event ChatHandler? OnChatResults;

    public override Task<ChatResponse> Chat(ChatRequest request, ServerCallContext context)
    {
        if (ChatId.Equals(request.ChatId, StringComparison.InvariantCulture) == false)
        {
            ChatInProgress = true;
            ChatId = request.ChatId;
            OnChat?.Invoke(request);
        }
        else if (ChatInProgress)
        {
            // chat loop finished, propagate results
            ChatInProgress = false;
            OnChatResults?.Invoke(request);
        }

        return Task.FromResult(new ChatResponse { IsOk = true });
    }

    public void Dispose()
    {
        NextNodeChannel?.ShutdownAsync().Wait();
        NextNodeChannel?.Dispose();
    }
}