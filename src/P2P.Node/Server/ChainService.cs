using Grpc.Core;
using Proto;

namespace P2P.Node.Server;

internal class ChainService : Proto.ChainService.ChainServiceBase
{
    // TODO: make thread safe
    private int _previousNodeId = -1;

    public int LeaderId { get; private set; } = -1;
    private string _electionLoopId = string.Empty;
    private bool _electionLoopInProgress;

    public event Action? OnDisconnect;

    public delegate void LeaderElectionHandler(string electionLoopId, int leaderId, DateTime leaderConnectionTimestamp);
    public event LeaderElectionHandler? OnLeaderElection;
    public event Action? OnLeaderElected;

    public override Task<AskPermissionToConnectResponse> AskPermissionToConnect(AskPermissionToConnectRequest request, ServerCallContext context)
    {
        var canConnect = _previousNodeId < 0 || _previousNodeId == request.NodeWantsToConnectId;
        ConsoleHelper.Debug(canConnect
            ? $"Allow node {request.NodeWantsToConnectId} to connect"
            : $"Deny node {request.NodeWantsToConnectId} to connect, {_previousNodeId} already connected");
        return Task.FromResult(new AskPermissionToConnectResponse { CanConnect = canConnect, ConnectedNodeId = _previousNodeId });
    }

    public override Task<AskToDisconnectResponse> AskToDisconnect(AskToDisconnectRequest request, ServerCallContext context)
    {
        OnDisconnect?.Invoke();
        ConsoleHelper.Debug($"Node {request.NodeAsksToDiconnectId} asked to disconnect, invoke disconnect");
        return Task.FromResult(new AskToDisconnectResponse { IsOk = true });
    }

    public override Task<ConnectResponse> Connect(ConnectRequest request, ServerCallContext context)
    {
        ConsoleHelper.Debug($"Node {request.NodeWantsToConnectId} is connected");
        _previousNodeId = request.NodeWantsToConnectId;
        return Task.FromResult(new ConnectResponse { IsOk = true });
    }

    public override Task<LeaderElectionResponse> ElectLeader(LeaderElectionRequest request, ServerCallContext context)
    {
        if (_electionLoopId.Equals(request.ElectionLoopId, StringComparison.InvariantCulture) == false)
        {
            ConsoleHelper.Debug($"Start election loop {request.ElectionLoopId}");
            _electionLoopInProgress = true;
            _electionLoopId = request.ElectionLoopId;
            OnLeaderElection?.Invoke(request.ElectionLoopId, request.LeaderId, request.LeaderConnectionTimestamp.ToDateTime());
        }
        else if (_electionLoopInProgress)
        {
            // else - leader found, need to propagate
            ConsoleHelper.WriteGreen($"Updating loop {request.ElectionLoopId} leader is {request.LeaderId}");
            LeaderId = request.LeaderId;
            _electionLoopInProgress = false;
            OnLeaderElected?.Invoke();
            OnLeaderElection?.Invoke(request.ElectionLoopId, request.LeaderId, request.LeaderConnectionTimestamp.ToDateTime());
        }

        return Task.FromResult(new LeaderElectionResponse { IsOk = true });
    }
}