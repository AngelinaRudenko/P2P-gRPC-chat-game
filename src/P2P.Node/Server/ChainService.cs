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
    public GrpcChannel? NextNodeChannel { get; set; }
    public Proto.Node? Leader { get; private set; }
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

        if (nodeWantsToConnect.Host == _currentNode.Host && nodeWantsToConnect.Port == _currentNode.Port)
        {
            ConsoleHelper.Debug("Skip attempt to connect to itself");
            return await Task.FromResult(new ConnectResponse { IsOk = false, Topology = Topology });
        }

        if (Topology.PreviousNode == null)
        {
            ConsoleHelper.Debug($"Node {nodeWantsToConnect.Name} wants to connect, allow since no one connected");
        }
        else
        {
            ConsoleHelper.Debug($"Node {nodeWantsToConnect.Name} wants to connect, ask previous node {Topology.PreviousNode.Name} to disconnect");
            
            var previousNodeChannel = GrpcChannel.ForAddress($"http://{Topology.PreviousNode.Host}:{Topology.PreviousNode.Port}",
                new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });

            var isAlive = await previousNodeChannel.ConnectAsync()
                .WaitAsync(TimeSpan.FromSeconds(_timeoutSettings.IsAliveRequestTimeout))
                .ContinueWith(task => task.Status == TaskStatus.RanToCompletion);

            if (isAlive)
            {
                var previousNodeClient = new Proto.ChainService.ChainServiceClient(previousNodeChannel);
                var askToDisconnectResult = await previousNodeClient.DisconnectAsync(
                    new DisconnectRequest { DisconnectFromNode = _currentNode, ConnectToNode = nodeWantsToConnect },
                    deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.DisconnectRequestTimeout));

                if (!askToDisconnectResult.IsOk)
                {
                    throw new Exception($"Node {Topology.PreviousNode.Name} doesn't agree to disconnect");
                }
            }

            await previousNodeChannel.ShutdownAsync();
        }

        var previousNodeTopology = new Topology { PreviousNode = Topology.PreviousNode, NextNode = _currentNode, NextNextNode = Topology.NextNode };
        Topology.PreviousNode = nodeWantsToConnect;

        return await Task.FromResult(new ConnectResponse { IsOk = true, Topology = previousNodeTopology });
    }

    public override async Task<DisconnectResponse> Disconnect(DisconnectRequest request, ServerCallContext context)
    {
        var disconnectFromNode = request.DisconnectFromNode;

        if (Topology.NextNode?.Host == disconnectFromNode.Host && Topology.NextNode?.Port == disconnectFromNode.Port)
        {
            ConsoleHelper.Debug($"Disconnect from node {disconnectFromNode.Name} and connect to node {request.ConnectToNode.Name}");
            Topology.NextNextNode = Topology.NextNode;
            Topology.NextNode = request.ConnectToNode;
            await NextNodeChannel!.ShutdownAsync();
            NextNodeChannel = GrpcChannel.ForAddress($"http://{Topology.NextNode.Host}:{Topology.NextNode.Port}",
                new GrpcChannelOptions { Credentials = ChannelCredentials.Insecure });
        }
        else
        {
            ConsoleHelper.Debug($"Skip disconnect from unknown node {disconnectFromNode.Name}");
        }

        return await Task.FromResult(new DisconnectResponse { IsOk = true });
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
            ConsoleHelper.WriteGreen($"Updating loop {request.ElectionLoopId} leader is {request.LeaderNode.Name}");
            Leader = request.LeaderNode;
            _electionLoopInProgress = false;
            OnLeaderElectionResult?.Invoke(request);
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