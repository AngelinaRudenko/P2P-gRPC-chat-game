using Grpc.Core;
using P2P.Node.Models;

namespace P2P.Node.Server;

internal class ChatServer
{
    private Grpc.Core.Server? _server;
    private readonly NodeSettings _node;

    public ChatServer(NodeSettings node)
    {
        _node = node;
        ChainService = new ChainService();
        ChatService = new ChatService();
    }

    public ChainService ChainService { get; }
    public ChatService ChatService { get; }

    public async Task StartAsync()
    {
        try
        {
            _server = new Grpc.Core.Server
            {
                Services =
                {
                    Proto.ChatService.BindService(ChatService),
                    Proto.ChainService.BindService(ChainService)
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