using Grpc.Core;
using Proto;

namespace P2P.Node.Server;

internal class ChainService : Proto.ChainService.ChainServiceBase
{
    // TODO: make thread safe
    private Proto.Node? _previousNode;

    private string _electionLoopId = string.Empty;
    private bool _electionLoopInProgress = false;

    public event Action? OnDisconnectRequest;

    public delegate void LeaderElectionHandler(string electionLoopId, int leaderId);
    public event LeaderElectionHandler? OnLeaderElectionRequest;

    public override Task<AskPermissionToConnectResponse> AskPermissionToConnect(AskPermissionToConnectRequest request, ServerCallContext context)
    {
        var canConnect = _previousNode == null || _previousNode.Id == request.NodeWantsToConnect.Id;
        if (canConnect)
        {
            ConsoleHelper.Debug($"Node {request.NodeWantsToConnect.Id} asks permission to connect. Allow");
        }
        else
        {
            ConsoleHelper.Debug($"Node {request.NodeWantsToConnect.Id} asks permission to connect. Deny, {_previousNode?.Id} already connected");
        }
        return Task.FromResult(new AskPermissionToConnectResponse { CanConnect = canConnect, ConnectedNode = _previousNode });
    }

    public override Task<AskToDisconnectResponse> AskToDisconnect(AskToDisconnectRequest request, ServerCallContext context)
    {
        OnDisconnectRequest?.Invoke();
        ConsoleHelper.Debug($"Node {request.NodeAsksToDiconnect.Id} asked to disconnect, invoke disconnect");
        return Task.FromResult(new AskToDisconnectResponse { IsOk = true });
    }

    public override Task<ConnectResponse> Connect(ConnectRequest request, ServerCallContext context)
    {
        ConsoleHelper.Debug($"Node {request.NodeWantsToConnect.Id} is connected");
        _previousNode = request.NodeWantsToConnect;
        return Task.FromResult(new ConnectResponse { IsOk = true });
    }

    public override Task<LeaderElectionResponse> ElectLeader(LeaderElectionRequest request, ServerCallContext context)
    {
        if (_electionLoopId.Equals(request.ElectionLoopId, StringComparison.InvariantCulture) == false)
        {
            // new election loop
            _electionLoopInProgress = true;
            _electionLoopId = request.ElectionLoopId;
            OnLeaderElectionRequest?.Invoke(request.ElectionLoopId, request.LeaderId);
            ConsoleHelper.Debug($"Start updating loop {request.ElectionLoopId}");
        }
        else if (_electionLoopInProgress)
        {
            // else - leader found, need to propagate
            _electionLoopInProgress = false;
            ConsoleHelper.WriteGreen($"Updating loop {request.ElectionLoopId} leader is {request.LeaderId}");
            OnLeaderElectionRequest?.Invoke(request.ElectionLoopId, request.LeaderId);
        }

        return Task.FromResult(new LeaderElectionResponse { IsOk = true });
    }
}