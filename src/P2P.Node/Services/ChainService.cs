using Grpc.Core;
using P2P.Node.Models;
using Proto;

namespace P2P.Node.Services
{
    internal class ChainService : Proto.ChainService.ChainServiceBase
    {
        private NodeSettings? _previousNode;

        public override Task<ConnectToNodeResponse> AskPermissionToConnect(ConnectToNodeRequest request, ServerCallContext context)
        {
            if (_previousNode == null)
            {
                _previousNode = new NodeSettings { Host = request.Host, Port = request.Port };
                return Task.FromResult(new ConnectToNodeResponse { IsConnected = true });
            }

            // TODO: process more cases
            return Task.FromResult(new ConnectToNodeResponse { IsConnected = false });
        }
    }
}
