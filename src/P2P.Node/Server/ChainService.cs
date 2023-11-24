using Grpc.Core;
using Proto;

namespace P2P.Node.Services
{
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
                Console.WriteLine($"Node {request.NodeWantsToConnect.Id} asks for permission to connect. Deny, {_previousNode?.Id} already connected");
            }
            else
            {
                Console.WriteLine($"Node {request.NodeWantsToConnect.Id} asks for permission to connect. Allow");
            }
            return Task.FromResult(new AskPermissionToConnectResponse { CanConnect = canConnect, ConnectedNode = _previousNode });
        }

        public override Task<AskToDisconnectResponse> AskToDisconnect(AskToDisconnectRequest request, ServerCallContext context)
        {
            OnDisconnectRequest?.Invoke();
            Console.WriteLine($"Node {request.NodeAsksToDiconnect.Id} asked to disconnect, invoke disconnect");
            return Task.FromResult(new AskToDisconnectResponse { IsOk = true });
        }

        public override Task<ConnectResponse> Connect(ConnectRequest request, ServerCallContext context)
        {
            Console.WriteLine($"Node {request.NodeWantsToConnect.Id} is connected");
            _previousNode = request.NodeWantsToConnect;
            return Task.FromResult(new ConnectResponse { IsOk = true });
        }
    }
}
