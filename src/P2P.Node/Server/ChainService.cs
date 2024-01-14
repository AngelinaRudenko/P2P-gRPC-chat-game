using Grpc.Core;
using P2P.Node.Configs;
using P2P.Node.Helpers;
using P2P.Node.Models;
using Proto;

namespace P2P.Node.Server;

internal class ChainService : Proto.ChainService.ChainServiceBase
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly TimeoutSettings _timeoutSettings;

    private readonly AppNode _currentNode;
    private string _electionLoopId = string.Empty;
    private bool _electionLoopInProgress;

    public delegate void LeaderElectionHandler(LeaderElectionRequest request);
    public event LeaderElectionHandler? OnLeaderElection;
    public event LeaderElectionHandler? OnLeaderElectionResult;

    public delegate void ChatHandler(ChatRequest request);
    public event Action? OnStartChat;
    public event ChatHandler? OnChat;

    public ChainService(AppNode currentNode, TimeoutSettings timeoutSettings)
    {
        _currentNode = currentNode;
        _timeoutSettings = timeoutSettings;
    }

    public AppTopology Topology { get; set; } = new();

    public override async Task<ConnectResponse> Connect(ConnectRequest request, ServerCallContext context)
    {
        var nodeWantsToConnect = SingletonMapper.Map<Proto.Node, AppNode>(request.NodeWantsToConnect);

        var  previousNodeTopology = new AppTopology { NextNode = _currentNode };
        
        if (_currentNode.Equals(nodeWantsToConnect))
        {
            Logger.Debug("I am the first one and leader");

            // response will be returned to node itself
            previousNodeTopology.PreviousNode = _currentNode;
            previousNodeTopology.NextNextNode = _currentNode;
            previousNodeTopology.Leader = _currentNode;
        }
        else if (_currentNode.Equals(Topology.NextNode))
        {
            Logger.Debug($"Node {nodeWantsToConnect.Name} wants to connect, allow since there is only one node in circle");
            
            Topology.NextNode = nodeWantsToConnect;
            Topology.NextNextNode = _currentNode;

            // response for node that wants to connect
            previousNodeTopology.PreviousNode = _currentNode;
            previousNodeTopology.NextNextNode = nodeWantsToConnect;
            previousNodeTopology.Leader = _currentNode;

            // do not await
#pragma warning disable CS4014
            Task.Run(() => OnStartChat?.Invoke());
#pragma warning restore CS4014
        }
        else if (Topology.PreviousNode != null)
        {
            var isAlive = await Topology.PreviousNode.Channel.Value.ConnectAsync()
                .WaitAsync(TimeSpan.FromSeconds(_timeoutSettings.IsAliveRequestTimeout))
                .ContinueWith(task => task.Status == TaskStatus.RanToCompletion);

            if (isAlive)
            {
                var previousNodeClient = new Proto.ChainService.ChainServiceClient(Topology.PreviousNode.Channel.Value);
                var askToDisconnectResult = await previousNodeClient.DisconnectAsync(
                    new DisconnectRequest { ConnectToNode = request.NodeWantsToConnect },
                    deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.DisconnectRequestTimeout));

                if (!askToDisconnectResult.IsOk)
                {
                    throw new Exception($"Previous node {Topology.PreviousNode.Name} deny {nodeWantsToConnect.Name} to connect");
                }

                Logger.Debug($"Allow node {nodeWantsToConnect.Name} to connect, previous node {Topology.PreviousNode.Name} is disconnected from current");

                // response for node that wants to connect
                previousNodeTopology.PreviousNode = Topology.PreviousNode;
                previousNodeTopology.NextNextNode = Topology.NextNode;
                previousNodeTopology.Leader = Topology.Leader;
            }
            else
            {
                Logger.Debug($"Allow node {nodeWantsToConnect.Name} to connect, previous node {Topology.PreviousNode.Name} is not alive");

                // response for node that wants to connect
                previousNodeTopology.PreviousNode = null; // previous node will leave its previous node the same
                previousNodeTopology.NextNextNode = Topology.NextNode;
                previousNodeTopology.Leader = Topology.Leader;
            }
        }
        else
        {
            throw new Exception("Unknown connect to request state");
        }

        Topology.PreviousNode = nodeWantsToConnect;

        NLogHelper.LogTopology(Logger, Topology);

        return await Task.FromResult(new ConnectResponse { IsOk = true, Topology = SingletonMapper.Map<AppTopology, Topology>(previousNodeTopology) });
    }

    public override async Task<DisconnectResponse> Disconnect(DisconnectRequest request, ServerCallContext context)
    {
        Logger.Debug($"Disconnect from node {Topology.NextNode?.Name} and connect to node {request.ConnectToNode.Name}");

        Topology.NextNextNode = Topology.NextNode;
        Topology.NextNode = SingletonMapper.Map<Proto.Node, AppNode>(request.ConnectToNode);

        if (Topology.PreviousNode != null)
        {
            try
            {
                var previousNodeClient = new Proto.ChainService.ChainServiceClient(Topology.PreviousNode.Channel.Value);
                await previousNodeClient.SetNextNextNodeAsync(
                    new SetNextNextNodeRequest { NextNextNode = SingletonMapper.Map<AppNode, Proto.Node>(Topology.NextNode) },
                    deadline: DateTime.UtcNow.AddSeconds(_timeoutSettings.CommonRequestTimeout));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to set next node for previous node {Topology.PreviousNode}");
            }
        }

        NLogHelper.LogTopology(Logger, Topology);

        return await Task.FromResult(new DisconnectResponse { IsOk = true });
    }

    public override Task<SetNextNextNodeResponse> SetNextNextNode(SetNextNextNodeRequest request, ServerCallContext context)
    {
        Logger.Debug($"Set new next next {request.NextNextNode.Name} instead of {Topology.NextNextNode?.Name}");
        Topology.NextNextNode = SingletonMapper.Map<Proto.Node, AppNode>(request.NextNextNode);
        NLogHelper.LogTopology(Logger, Topology);

        return Task.FromResult(new SetNextNextNodeResponse { IsOk = true });
    }

    public override Task<LeaderElectionResponse> ElectLeader(LeaderElectionRequest request, ServerCallContext context)
    {
        if (_electionLoopId.Equals(request.ElectionLoopId, StringComparison.InvariantCulture) == false)
        {
            Logger.Debug($"Start election loop {request.ElectionLoopId}");
            _electionLoopInProgress = true;
            _electionLoopId = request.ElectionLoopId;
            Task.Run(() => OnLeaderElection?.Invoke(request));
        }
        else if (_electionLoopInProgress)
        {
            // else - leader found, need to propagate
            Logger.Debug($"Updating loop {request.ElectionLoopId} is finished");
            Topology.Leader = SingletonMapper.Map<Proto.Node, AppNode>(request.LeaderNode);
            _electionLoopInProgress = false;
            Task.Run(() =>
            {
                OnLeaderElectionResult?.Invoke(request);
                OnStartChat?.Invoke();
            });
            NLogHelper.LogTopology(Logger, Topology);
        }

        return Task.FromResult(new LeaderElectionResponse { IsOk = true });
    }

    public override Task<ChatResponse> Chat(ChatRequest request, ServerCallContext context)
    {
        Task.Run(() => OnChat?.Invoke(request));
        return Task.FromResult(new ChatResponse { IsOk = true });
    }
}