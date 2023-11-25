using Grpc.Core;
using P2P.Node.Models;

namespace P2P.Node.Server;

internal class ChatServer
{
    private Grpc.Core.Server? _server;
    private readonly NodeSettings _node;

    public event Action? OnDisconnectRequest;

    public ChatServer(NodeSettings node)
    {
        _node = node;
    }

    private void InvokeDisconnect()
    {
        OnDisconnectRequest?.Invoke();
    }

    public async Task StartAsync()
    {
        try
        {
            var chainService = new ChainService();
            chainService.OnDisconnectRequest += InvokeDisconnect;

            _server = new Grpc.Core.Server
            {
                Services =
                {
                    Proto.ChatService.BindService(new ChatService()),
                    Proto.ChainService.BindService(chainService)
                },
                Ports = { new ServerPort(_node.Host, _node.Port, ServerCredentials.Insecure) }
            };

            _server.Start();
            ConsoleHelper.Debug($"Server is listening on {_node}");
        }
        catch (IOException e)
        {
            ConsoleHelper.WriteRed($"Server failed to start: {e.Message}");
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (_server != null)
        {
            ConsoleHelper.Debug("Server shutdown");
            await _server.ShutdownAsync();
        }
    }
}