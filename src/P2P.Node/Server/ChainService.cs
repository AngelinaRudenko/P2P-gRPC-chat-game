using Grpc.Core;
using Proto;

namespace P2P.Node.Server;

internal class ChainService : Proto.ChainService.ChainServiceBase
{
    // TODO: make thread safe
    private Proto.Node? _previousNode;

    public event Action? OnDisconnectRequest;

    public override Task<AskPermissionToConnectResponse> AskPermissionToConnect(AskPermissionToConnectRequest request, ServerCallContext context)
    {
        var canConnect = _previousNode == null;
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
}