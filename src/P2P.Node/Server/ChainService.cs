using Grpc.Core;
using Proto;

namespace P2P.Node.Server;

internal class ChainService : Proto.ChainService.ChainServiceBase
{
    // TODO: make thread safe
    private Proto.Node? _previousNode = null;

    public Proto.Node? Leader { get; private set; } = null;
    private string _electionLoopId = string.Empty;
    private bool _electionLoopInProgress;

    public event Action? OnDisconnect;

    public delegate void LeaderElectionHandler(LeaderElectionRequest request);
    public event LeaderElectionHandler? OnLeaderElection;
    public event LeaderElectionHandler? OnLeaderElectionResult;

    public override Task<AskPermissionToConnectResponse> AskPermissionToConnect(AskPermissionToConnectRequest request, ServerCallContext context)
    {
        var nodeWantsToConnect = request.NodeWantsToConnect;

        var canConnect = _previousNode == null || 
                         (_previousNode.Host == nodeWantsToConnect.Host && _previousNode.Port == nodeWantsToConnect.Port);

        ConsoleHelper.Debug(canConnect
            ? $"Allow node {nodeWantsToConnect.Host}:{nodeWantsToConnect.Port} to connect"
            : $"Deny node {nodeWantsToConnect.Host}:{nodeWantsToConnect.Port} to connect, node {_previousNode.Host}:{_previousNode.Port} already connected");
        
        return Task.FromResult(new AskPermissionToConnectResponse { CanConnect = canConnect, ConnectedNode = _previousNode });
    }

    public override Task<AskToDisconnectResponse> AskToDisconnect(AskToDisconnectRequest request, ServerCallContext context)
    {
        OnDisconnect?.Invoke();
        ConsoleHelper.Debug($"Node {request.NodeAsksToDiconnect.Host}:{request.NodeAsksToDiconnect.Port} asked to disconnect, invoke disconnect");
        return Task.FromResult(new AskToDisconnectResponse { IsOk = true });
    }

    public override Task<ConnectResponse> Connect(ConnectRequest request, ServerCallContext context)
    {
        ConsoleHelper.Debug($"Node {request.NodeWantsToConnect.Host}:{request.NodeWantsToConnect.Port} is connected");
        _previousNode = request.NodeWantsToConnect;
        return Task.FromResult(new ConnectResponse { IsOk = true });
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
            ConsoleHelper.WriteGreen($"Updating loop {request.ElectionLoopId} leader is {request.LeaderNode.Host}:{request.LeaderNode.Port}");
            Leader = request.LeaderNode;
            _electionLoopInProgress = false;
            OnLeaderElectionResult?.Invoke(request);
        }

        return Task.FromResult(new LeaderElectionResponse { IsOk = true });
    }
}